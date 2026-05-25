import asyncio

from fastapi import FastAPI, File, HTTPException, UploadFile
import httpx
from pydantic import BaseModel
import re
import json
import base64
from fastapi.responses import StreamingResponse

from .config import get_settings
from .db import ping_database
from .document_ingest import extract_pdf_text
from .repositories import (
    archive_document,
    archive_collection,
    create_collection,
    create_domain,
    create_text_document,
    delete_content_unit,
    get_recent_context_units,
    has_user_chat_backup_files,
    list_collection_documents,
    list_document_chunks,
    list_collections,
    list_domains,
    list_retrieval_profiles,
    list_user_chat_backup_files,
    mark_user_chat_backup_files_restored,
    upsert_app_user,
    upsert_user_chat_backup_file,
    update_collection,
)


class CreateDomainRequest(BaseModel):
    domainCode: str
    domainType: str
    displayName: str
    description: str | None = None


class CreateCollectionRequest(BaseModel):
    domainCode: str
    collectionCode: str
    displayName: str
    description: str | None = None


class CreateTextDocumentRequest(BaseModel):
    collectionCode: str
    sourceName: str
    bodyText: str
    sourceType: str = "pasted_text"


class UpdateCollectionRequest(BaseModel):
    displayName: str
    description: str | None = None


class AskRequest(BaseModel):
    prompt: str
    shortMemoryCollectionCode: str
    longTermCollectionCodes: list[str] = []
    model: str | None = None
    history: list[dict[str, str]] = []


class ChatBackupUserRequest(BaseModel):
    windowsUserName: str
    windowsSid: str | None = None
    displayName: str | None = None
    identityKeyKind: str
    identityKeyValue: str


class ChatBackupFileUpsertRequest(ChatBackupUserRequest):
    rootCollectionCode: str
    rootDisplayName: str
    fileName: str
    payloadBase64: str
    contentHashBase64: str
    compressionType: str
    encryptionType: str
    keyVersion: int = 1
    clientModifiedUtc: str
    clientMachineName: str | None = None
    appVersion: str | None = None
    isDeleted: bool = False


def _generate_with_ollama(settings, prompt: str, model: str | None = None) -> dict[str, object]:
    response = httpx.post(
        f"{settings.ollama_base_url}/api/generate",
        json={
            "model": model or settings.ollama_chat_model,
            "prompt": prompt,
            "stream": False,
        },
        timeout=120,
    )
    response.raise_for_status()
    return response.json()


async def _stream_with_ollama(settings, prompt: str, model: str | None = None):
    async with httpx.AsyncClient(timeout=120) as client:
        async with client.stream(
            "POST",
            f"{settings.ollama_base_url}/api/generate",
            json={
                "model": model or settings.ollama_chat_model,
                "prompt": prompt,
                "stream": True,
            },
        ) as response:
            response.raise_for_status()
            async for line in response.aiter_lines():
                if not line:
                    continue
                yield json.loads(line)


def _normalize_title(raw: str) -> str:
    title = (raw or "").strip()
    title = re.sub(r"^['\"]+|['\"]+$", "", title)
    title = re.sub(r"\s+", " ", title)
    title = re.sub(r"^[\-\u2022\*\d\.\)\(:\s]+", "", title)
    words = title.split()
    if len(words) > 3:
        title = " ".join(words[:3])
    return title[:120] or "Untitled response"


def _fallback_title(prompt: str) -> str:
    cleaned = re.sub(r"[^\w\s-]", " ", prompt or "")
    cleaned = re.sub(r"\s+", " ", cleaned).strip()
    words = cleaned.split()
    return " ".join(words[:3])[:120] or "Untitled response"


def _generate_title(settings, prompt: str, answer: str) -> str:
    title_prompt = (
        "Write a concise chat title for the following user question and answer. "
        "Use at most 3 words. "
        "Return only the title, no quotes, no punctuation, no markdown, and no explanation.\n\n"
        f"Question: {prompt}\n"
        f"Answer: {answer}\n"
    )
    try:
        title_payload = _generate_with_ollama(settings, title_prompt, model=settings.ollama_title_model)
        title = _normalize_title(title_payload.get("response", ""))
        return title if title != "Untitled response" else _fallback_title(prompt)
    except Exception:
        return _fallback_title(prompt)


def _generate_prompt_title(settings, prompt: str) -> str:
    title_prompt = (
        "Write a concise chat title for this user prompt. "
        "Use at most 3 words. "
        "Return only the title, no quotes, no punctuation, no markdown, and no explanation.\n\n"
        f"Prompt: {prompt}\n"
    )
    try:
        title_payload = _generate_with_ollama(settings, title_prompt, model=settings.ollama_title_model)
        title = _normalize_title(title_payload.get("response", ""))
        return title if title != "Untitled response" else _fallback_title(prompt)
    except Exception:
        return _fallback_title(prompt)


def _extract_metrics(payload: dict[str, object], model: str | None = None) -> dict[str, object]:
    completion_tokens = int(payload.get("eval_count") or 0)
    prompt_tokens = int(payload.get("prompt_eval_count") or 0)
    total_tokens = prompt_tokens + completion_tokens
    eval_duration_ns = int(payload.get("eval_duration") or 0)
    total_duration_ns = int(payload.get("total_duration") or 0)
    tokens_per_second = 0.0
    if eval_duration_ns > 0 and completion_tokens > 0:
        tokens_per_second = completion_tokens / (eval_duration_ns / 1_000_000_000)

    duration_seconds = 0.0
    if total_duration_ns > 0:
        duration_seconds = total_duration_ns / 1_000_000_000

    return {
        "modelName": payload.get("model") or model or "",
        "totalTokens": total_tokens,
        "promptTokens": prompt_tokens,
        "completionTokens": completion_tokens,
        "durationSeconds": duration_seconds,
        "tokensPerSecond": tokens_per_second,
        "createdAtUtc": payload.get("created_at"),
    }


def create_app() -> FastAPI:
    settings = get_settings()
    app = FastAPI(
        title="DomainLinks Backend",
        version="0.1.0",
        description="SQL Server backed retrieval and local AI provider service.",
    )

    @app.get("/health")
    def health() -> dict[str, object]:
        payload: dict[str, object] = {
            "status": "ok",
            "environment": settings.env,
            "default_provider": settings.default_llm_provider,
            "sql_server": settings.sql_server,
            "sql_database": settings.sql_database,
        }
        try:
            payload["database"] = ping_database(settings)
        except Exception as exc:
            payload["database"] = {
                "reachable": False,
                "error": str(exc),
            }
        return payload

    @app.get("/config")
    def config() -> dict[str, object]:
        return settings.public_config()

    @app.get("/domains")
    def domains() -> list[dict[str, object]]:
        return list_domains(settings)

    @app.post("/domains")
    def add_domain(request: CreateDomainRequest) -> dict[str, object]:
        return create_domain(
            settings,
            domain_code=request.domainCode,
            domain_type=request.domainType,
            display_name=request.displayName,
            description=request.description,
        )

    @app.get("/collections")
    def collections(domainCode: str | None = None) -> list[dict[str, object]]:
        return list_collections(settings, domainCode)

    @app.post("/collections")
    def add_collection(request: CreateCollectionRequest) -> dict[str, object]:
        return create_collection(
            settings,
            domain_code=request.domainCode,
            collection_code=request.collectionCode,
            display_name=request.displayName,
            description=request.description,
        )

    @app.put("/collections/{collectionCode}")
    def edit_collection(collectionCode: str, request: UpdateCollectionRequest) -> dict[str, object]:
        return update_collection(
            settings,
            collection_code=collectionCode,
            display_name=request.displayName,
            description=request.description,
        )

    @app.delete("/collections/{collectionCode}")
    def delete_collection(collectionCode: str) -> dict[str, object]:
        archive_collection(settings, collectionCode)
        return {"status": "archived", "collectionCode": collectionCode}

    @app.post("/documents/text")
    def add_text_document(request: CreateTextDocumentRequest) -> dict[str, object]:
        return create_text_document(
            settings,
            collection_code=request.collectionCode,
            source_name=request.sourceName,
            body_text=request.bodyText,
            source_type=request.sourceType,
        )

    @app.post("/documents/pdf")
    async def add_pdf_document(
        collectionCode: str,
        file: UploadFile = File(...),
    ) -> dict[str, object]:
        if not file.filename or not file.filename.lower().endswith(".pdf"):
            raise HTTPException(status_code=400, detail="Only PDF uploads are supported on this endpoint.")

        pdf_bytes = await file.read()
        extracted_text, stats = extract_pdf_text(pdf_bytes)
        if not extracted_text.strip():
            raise HTTPException(status_code=400, detail="No usable text was extracted from the PDF.")

        result = create_text_document(
            settings,
            collection_code=collectionCode,
            source_name=file.filename,
            body_text=extracted_text,
            source_type="pdf_upload",
        )
        result["ExtractionStats"] = stats
        return result

    @app.get("/documents")
    def documents(collectionCode: str) -> list[dict[str, object]]:
        return list_collection_documents(settings, collectionCode)

    @app.delete("/documents/{documentId}")
    def delete_document(documentId: str) -> dict[str, object]:
        archive_document(settings, documentId)
        return {"status": "archived", "documentId": documentId}

    @app.get("/documents/{documentId}/chunks")
    def document_chunks(documentId: str) -> list[dict[str, object]]:
        return list_document_chunks(settings, documentId)

    @app.delete("/content-units/{contentUnitId}")
    def delete_chunk(contentUnitId: str) -> dict[str, object]:
        delete_content_unit(settings, contentUnitId)
        return {"status": "deleted", "contentUnitId": contentUnitId}

    @app.get("/retrieval-profiles")
    def retrieval_profiles() -> list[dict[str, object]]:
        return list_retrieval_profiles(settings)

    @app.post("/ask")
    def ask(request: AskRequest) -> dict[str, object]:
        prompt = request.prompt.strip()
        if not prompt:
            return {"error": "Prompt is required."}

        collection_codes = [request.shortMemoryCollectionCode, *request.longTermCollectionCodes]
        context_units = get_recent_context_units(settings, collection_codes)

        context_lines: list[str] = []
        sources: list[dict[str, object]] = []
        for unit in context_units:
            collection_display = unit.get("CollectionDisplayName") or unit.get("CollectionCode")
            source_name = unit.get("SourceName") or "unknown"
            body = unit.get("BodyText") or ""
            context_lines.append(f"[{collection_display} | {source_name}] {body}")
            sources.append(
                {
                    "collectionCode": unit.get("CollectionCode"),
                    "collectionDisplayName": collection_display,
                    "sourceName": source_name,
                    "contentUnitId": unit.get("ContentUnitId"),
                }
            )

        history_lines = []
        for item in request.history:
            role = (item.get("role") or "").strip().lower()
            content = (item.get("content") or "").strip()
            if role and content:
                history_lines.append(f"{role.title()}: {content}")

        compiled_prompt = (
            "Answer the user using the provided short-memory and durable-domain context when relevant. "
            "If the context is thin or missing, say so clearly.\n\n"
            f"Conversation so far:\n{chr(10).join(history_lines) if history_lines else 'No previous conversation.'}\n\n"
            f"User prompt:\n{prompt}\n\n"
            f"Context:\n{chr(10).join(context_lines) if context_lines else 'No stored context was found.'}\n"
        )

        answer_payload = _generate_with_ollama(settings, compiled_prompt, model=request.model)
        answer = str(answer_payload.get("response", "")).strip()
        title = _generate_title(settings, prompt, answer)
        return {
            "answer": answer,
            "title": title,
            "sources": sources,
            "usedCollectionCodes": collection_codes,
            "metrics": _extract_metrics(answer_payload, model=request.model),
        }

    @app.post("/ask/stream")
    async def ask_stream(request: AskRequest):
        prompt = request.prompt.strip()
        if not prompt:
            raise HTTPException(status_code=400, detail="Prompt is required.")

        collection_codes = [request.shortMemoryCollectionCode, *request.longTermCollectionCodes]
        context_units = get_recent_context_units(settings, collection_codes)

        context_lines: list[str] = []
        sources: list[dict[str, object]] = []
        for unit in context_units:
            collection_display = unit.get("CollectionDisplayName") or unit.get("CollectionCode")
            source_name = unit.get("SourceName") or "unknown"
            body = unit.get("BodyText") or ""
            context_lines.append(f"[{collection_display} | {source_name}] {body}")
            sources.append(
                {
                    "collectionCode": unit.get("CollectionCode"),
                    "collectionDisplayName": collection_display,
                    "sourceName": source_name,
                    "contentUnitId": unit.get("ContentUnitId"),
                }
            )

        history_lines = []
        for item in request.history:
            role = (item.get("role") or "").strip().lower()
            content = (item.get("content") or "").strip()
            if role and content:
                history_lines.append(f"{role.title()}: {content}")

        compiled_prompt = (
            "Answer the user using the provided short-memory and durable-domain context when relevant. "
            "If the context is thin or missing, say so clearly.\n\n"
            f"Conversation so far:\n{chr(10).join(history_lines) if history_lines else 'No previous conversation.'}\n\n"
            f"User prompt:\n{prompt}\n\n"
            f"Context:\n{chr(10).join(context_lines) if context_lines else 'No stored context was found.'}\n"
        )

        async def event_stream():
            answer_parts: list[str] = []
            provisional_title = _fallback_title(prompt)
            yield json.dumps({"type": "title", "title": provisional_title}) + "\n"
            title_task = asyncio.create_task(asyncio.to_thread(_generate_prompt_title, settings, prompt))
            emitted_generated_title = False
            try:
                async for payload in _stream_with_ollama(settings, compiled_prompt, model=request.model):
                    if title_task.done() and not emitted_generated_title:
                        try:
                            generated_title = title_task.result()
                        except Exception:
                            generated_title = provisional_title
                        if generated_title and generated_title != provisional_title:
                            yield json.dumps({"type": "title", "title": generated_title}) + "\n"
                        emitted_generated_title = True

                    chunk = payload.get("response") or ""
                    if chunk:
                        answer_parts.append(chunk)
                        yield json.dumps({"type": "delta", "delta": chunk}) + "\n"

                    if payload.get("done"):
                        answer = "".join(answer_parts).strip()
                        title = _generate_title(settings, prompt, answer)
                        yield json.dumps(
                            {
                                "type": "final",
                                "answer": answer,
                                "title": title,
                                "sources": sources,
                                "usedCollectionCodes": collection_codes,
                                "metrics": _extract_metrics(payload, model=request.model),
                            }
                        ) + "\n"
            except Exception as exc:
                yield json.dumps({"type": "error", "error": str(exc)}) + "\n"
            finally:
                if not title_task.done():
                    title_task.cancel()

        return StreamingResponse(event_stream(), media_type="application/x-ndjson")

    @app.post("/chat-backups/check")
    def check_chat_backups(request: ChatBackupUserRequest) -> dict[str, object]:
        user = upsert_app_user(
            settings,
            windows_user_name=request.windowsUserName,
            windows_sid=request.windowsSid,
            display_name=request.displayName,
        )
        app_user_id = str(user.get("AppUserId") or "")
        has_backups = has_user_chat_backup_files(settings, app_user_id) if app_user_id else False
        file_count = len(list_user_chat_backup_files(settings, app_user_id, include_payload=False)) if has_backups else 0
        return {
            "hasBackups": has_backups,
            "fileCount": file_count,
        }

    @app.post("/chat-backups/restore")
    def restore_chat_backups(request: ChatBackupUserRequest) -> dict[str, object]:
        user = upsert_app_user(
            settings,
            windows_user_name=request.windowsUserName,
            windows_sid=request.windowsSid,
            display_name=request.displayName,
        )
        app_user_id = str(user.get("AppUserId") or "")
        if not app_user_id:
            return {"files": []}

        files = list_user_chat_backup_files(settings, app_user_id, include_payload=True)
        mark_user_chat_backup_files_restored(settings, app_user_id)
        return {
            "files": [
                {
                    "rootCollectionCode": file.get("RootCollectionCode"),
                    "rootDisplayName": file.get("RootDisplayName"),
                    "fileName": file.get("FileName"),
                    "payloadBase64": base64.b64encode(file.get("FileContentCompressedEncrypted") or b"").decode("ascii"),
                    "contentHashBase64": base64.b64encode(file.get("ContentHashSha256") or b"").decode("ascii"),
                    "compressionType": file.get("CompressionType"),
                    "encryptionType": file.get("EncryptionType"),
                    "keyVersion": file.get("KeyVersion"),
                    "clientModifiedUtc": file.get("ClientModifiedUtc"),
                }
                for file in files
            ]
        }

    @app.put("/chat-backups/file")
    def upsert_chat_backup_file(request: ChatBackupFileUpsertRequest) -> dict[str, object]:
        user = upsert_app_user(
            settings,
            windows_user_name=request.windowsUserName,
            windows_sid=request.windowsSid,
            display_name=request.displayName,
        )
        app_user_id = str(user.get("AppUserId") or "")
        if not app_user_id:
            raise HTTPException(status_code=400, detail="Unable to resolve the application user.")

        upsert_user_chat_backup_file(
            settings,
            app_user_id=app_user_id,
            root_collection_code=request.rootCollectionCode,
            root_display_name=request.rootDisplayName,
            file_name=request.fileName,
            payload_bytes=base64.b64decode(request.payloadBase64),
            content_hash_bytes=base64.b64decode(request.contentHashBase64),
            compression_type=request.compressionType,
            encryption_type=request.encryptionType,
            key_version=request.keyVersion,
            client_modified_utc=request.clientModifiedUtc,
            client_machine_name=request.clientMachineName,
            app_version=request.appVersion,
            is_deleted=request.isDeleted,
        )
        return {"status": "ok"}

    return app


app = create_app()
