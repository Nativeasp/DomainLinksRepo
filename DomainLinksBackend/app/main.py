from fastapi import FastAPI, File, HTTPException, UploadFile
import httpx
from pydantic import BaseModel
import re

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
    list_collection_documents,
    list_document_chunks,
    list_collections,
    list_domains,
    list_retrieval_profiles,
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


def _generate_with_ollama(settings, prompt: str, model: str | None = None) -> str:
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
    return response.json().get("response", "")


def _normalize_title(raw: str) -> str:
    title = (raw or "").strip()
    title = re.sub(r"^['\"]+|['\"]+$", "", title)
    title = re.sub(r"\s+", " ", title)
    return title[:120] or "Untitled response"


def _generate_title(settings, prompt: str, answer: str) -> str:
    title_prompt = (
        "Write a short title for the following user question and answer. "
        "Return only the title, no quotes.\n\n"
        f"Question: {prompt}\n"
        f"Answer: {answer}\n"
    )
    raw_title = _generate_with_ollama(settings, title_prompt, model=settings.ollama_title_model)
    return _normalize_title(raw_title)


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

        answer = _generate_with_ollama(settings, compiled_prompt, model=request.model).strip()
        title = _generate_title(settings, prompt, answer)
        return {
            "answer": answer,
            "title": title,
            "sources": sources,
            "usedCollectionCodes": collection_codes,
        }

    return app


app = create_app()
