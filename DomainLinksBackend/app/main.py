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
    create_control_from_suggestion,
    create_collection,
    create_domain,
    create_text_document,
    delete_collection,
    delete_domain,
    delete_content_unit,
    get_collection_delete_preview,
    get_control_suggestion_context,
    get_domain_delete_preview,
    get_recent_context_units,
    get_domain_assist_context,
    has_user_chat_backup_files,
    list_collection_documents,
    list_document_chunks,
    list_collections,
    list_controls_for_branch,
    list_control_types,
    list_domains,
    list_domain_orientations,
    list_domain_types,
    reorder_root_domains,
    list_retrieval_profiles,
    list_user_chat_backup_files,
    mark_user_chat_backup_files_restored,
    upsert_app_user,
    upsert_user_chat_backup_file,
    update_collection,
    update_domain,
)


class CreateDomainRequest(BaseModel):
    domainCode: str
    domainTypeId: int | None = None
    domainOrientationId: int | None = None
    domainParentId: str | None = None
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


class UpdateDomainRequest(BaseModel):
    displayName: str
    description: str | None = None
    domainTypeId: int | None = None
    domainOrientationId: int | None = None


class ReorderDomainSiblingsRequest(BaseModel):
    parentDomainId: str | None = None
    orientationCode: str | None = None
    orderedDomainCodes: list[str]


class DomainAssistRequest(BaseModel):
    domainCode: str
    instruction: str
    draftText: str | None = None
    model: str | None = None


class DomainChildSuggestionRequest(BaseModel):
    parentDomainCode: str
    instruction: str
    draftText: str | None = None
    model: str | None = None


class ExecuteDomainChildSuggestionRequest(BaseModel):
    parentDomainCode: str
    displayName: str
    description: str | None = None
    domainType: str
    domainCode: str | None = None


class ControlSuggestionRequest(BaseModel):
    branchRootDomainCode: str
    mode: str = "options"
    idea: str | None = None
    controlTypeCode: str | None = None
    count: int = 5
    model: str | None = None


class ExecuteControlSuggestionRequest(BaseModel):
    domainCode: str
    controlTypeCode: str
    displayName: str
    description: str | None = None
    controlObjective: str | None = None
    evidenceExpectation: str | None = None
    controlCode: str | None = None


class PromptPreviewResponse(BaseModel):
    model: str
    systemPrompt: str
    userPrompt: str


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


def _build_domain_assist_prompt_parts(
    domain_context: dict[str, object],
    instruction: str,
    draft_text: str | None,
) -> tuple[str, str]:
    domain = domain_context.get("domain") or {}
    child_domains = domain_context.get("childDomains") or []
    collections = domain_context.get("collections") or []
    documents = domain_context.get("documents") or []

    child_lines = [
        f"- {item.get('displayName')} ({item.get('domainType') or 'Unknown type'})"
        for item in child_domains
    ]
    collection_lines = [
        f"- {item.get('DisplayName')} [{item.get('CollectionCode')}] docs={item.get('DocumentCount') or 0}"
        for item in collections
    ]
    document_lines = [
        f"- {item.get('SourceName')} ({item.get('SourceType') or 'unknown'}) in {item.get('CollectionDisplayName')} chunks={item.get('ChunkCount') or 0}"
        for item in documents
    ]

    system_prompt = (
        "You are helping curate a local RAG domain store for an internal knowledge workspace. "
        "Write concise, clear domain wording that improves retrieval usefulness. "
        "Preserve organizational meaning. Avoid generic filler and avoid marketing tone. "
        "Help distinguish the selected domain from sibling domains and describe what belongs in it. "
        "Return only the suggested wording or answer requested by the user. "
        "Do not use bullet points unless the user explicitly asks for them."
    )
    user_prompt = (
        f"Selected domain name: {domain.get('DisplayName') or ''}\n"
        f"Selected domain code: {domain.get('DomainCode') or ''}\n"
        f"Selected domain type: {domain.get('DomainType') or ''}\n"
        f"Parent path: {domain_context.get('parentPath') or '(root)'}\n"
        f"Current domain description/context text:\n{draft_text if draft_text is not None else (domain.get('Description') or '')}\n\n"
        f"Child domains:\n{chr(10).join(child_lines) if child_lines else 'None'}\n\n"
        f"Collections in this domain:\n{chr(10).join(collection_lines) if collection_lines else 'None'}\n\n"
        f"Recent documents in this domain:\n{chr(10).join(document_lines) if document_lines else 'None'}\n\n"
        f"User instruction:\n{instruction.strip()}\n"
    )
    return system_prompt, user_prompt


def _build_domain_assist_prompt(
    domain_context: dict[str, object],
    instruction: str,
    draft_text: str | None,
) -> str:
    system_prompt, user_prompt = _build_domain_assist_prompt_parts(domain_context, instruction, draft_text)
    return f"{system_prompt}\n\n{user_prompt}"


def _build_child_domain_suggestion_prompt_parts(
    domain_context: dict[str, object],
    instruction: str,
    draft_text: str | None,
    domain_types: list[dict[str, object]],
) -> tuple[str, str]:
    domain = domain_context.get("domain") or {}
    child_domains = domain_context.get("childDomains") or []
    child_lines = [
        f"- {item.get('displayName')} ({item.get('domainType') or 'Unknown type'})"
        for item in child_domains
    ]
    allowed_type_lines = [
        f"- {item.get('CODE')}: {item.get('NAME')}"
        for item in domain_types
        if item.get("CODE")
    ]

    system_prompt = (
        "You are helping organize a business domain map.\n\n"
        'A domain is a specific area of knowledge, activity, control, or interest, often representing a field where someone has expertise or authority, such as "the financial domain".\n\n'
        "Your task is to suggest exactly one new child domain under the selected parent domain.\n\n"
        "Return only valid JSON in this exact structure:\n"
        "{\n"
        '  "displayName": "Domain Title here",\n'
        '  "description": "Context text here...",\n'
        '  "domainType": "EXECUTIVE"\n'
        "}\n\n"
        "Rules:\n"
        "- Return only JSON.\n"
        "- Do not wrap the JSON in markdown fences.\n"
        "- Do not include any explanation before or after the JSON.\n"
        '- "displayName" must be a concise business domain title.\n'
        '- "description" must explain what belongs in the domain and what underlying information, activities, responsibilities, or controls it covers.\n'
        '- "domainType" must be exactly one of: EXECUTIVE, CORPORATE, SERVICE.\n'
        "- Do not repeat or closely duplicate an existing child domain.\n"
        "- Make the suggestion fit naturally under the selected parent domain.\n"
        "- Prefer clarity and specificity over broad or generic wording."
    )
    user_prompt = (
        f"Selected parent domain name: {domain.get('DisplayName') or ''}\n"
        f"Selected parent domain code: {domain.get('DomainCode') or ''}\n"
        f"Selected parent domain type: {domain.get('DomainType') or ''}\n"
        f"Parent path: {domain_context.get('parentPath') or '(root)'}\n"
        f"Selected parent domain description:\n{draft_text if draft_text is not None else (domain.get('Description') or '')}\n\n"
        f"Existing child domains:\n{chr(10).join(child_lines) if child_lines else 'None'}\n\n"
        f"Allowed domain types:\n{chr(10).join(allowed_type_lines) if allowed_type_lines else 'None'}\n\n"
        f"User instruction:\n{instruction.strip()}\n"
    )
    return system_prompt, user_prompt


def _build_child_domain_suggestion_prompt(
    domain_context: dict[str, object],
    instruction: str,
    draft_text: str | None,
    domain_types: list[dict[str, object]],
) -> str:
    system_prompt, user_prompt = _build_child_domain_suggestion_prompt_parts(
        domain_context,
        instruction,
        draft_text,
        domain_types,
    )
    return f"{system_prompt}\n\n{user_prompt}"


def _build_control_suggestion_prompt(
    context: dict[str, object],
    control_types: list[dict[str, object]],
    request: ControlSuggestionRequest,
) -> tuple[str, str]:
    root_domain = context.get("rootDomain") or {}
    branch_domains = context.get("branchDomains") or []
    existing_controls = context.get("existingControls") or []
    count = max(1, min(request.count, 10))

    domain_lines = [
        f"- {item.get('DisplayName')} [{item.get('DomainCode')}] ({item.get('DomainType') or 'Unknown type'}): {item.get('Description') or ''}"
        for item in branch_domains
    ]
    control_lines = [
        f"- {item.get('DisplayName')} [{item.get('ControlCode')}] type={item.get('ControlTypeCode')} domain={item.get('DomainCode')}"
        for item in existing_controls
    ]
    allowed_type_lines = [
        f"- {item.get('CODE')}: {item.get('NAME')} - {item.get('DESCRIPTION') or ''}"
        for item in control_types
        if item.get("CODE")
    ]
    requested_type = (request.controlTypeCode or "").strip().upper()
    type_instruction = (
        f'Every suggestion must use this controlTypeCode: "{requested_type}".'
        if requested_type and requested_type != "ANY"
        else "Use the most appropriate controlTypeCode from the allowed list."
    )
    idea_instruction = (
        f"User idea/focus:\n{request.idea.strip()}\n\n"
        if request.idea and request.idea.strip()
        else "User idea/focus:\nNone. Suggest useful options from the branch context.\n\n"
    )

    system_prompt = (
        "You are helping design business controls for an internal control manager.\n\n"
        "Return only valid JSON in this exact structure:\n"
        "{\n"
        '  "suggestions": [\n'
        "    {\n"
        '      "displayName": "Control title",\n'
        '      "controlTypeCode": "PREVENTIVE",\n'
        '      "domainCode": "domain-code",\n'
        '      "description": "Short description",\n'
        '      "controlObjective": "What this control is meant to achieve",\n'
        '      "evidenceExpectation": "Evidence expected to prove operation"\n'
        "    }\n"
        "  ]\n"
        "}\n\n"
        "Rules:\n"
        "- Return only JSON. Do not wrap JSON in markdown fences.\n"
        "- Suggest practical, auditable controls that fit the selected domain.\n"
        "- Do not duplicate existing controls.\n"
        '- domainCode must exactly equal the selected root domain code shown in "Branch root".\n'
        "- controlTypeCode must be one of the allowed control type codes.\n"
        "- Keep displayName concise and business-readable.\n"
        "- Evidence must be concrete enough that a person could inspect it."
    )
    user_prompt = (
        f"Branch root: {root_domain.get('DisplayName') or ''} [{root_domain.get('DomainCode') or ''}]\n"
        f"Mode: {request.mode}\n"
        f"Suggestion count: {count}\n"
        f"{type_instruction}\n\n"
        f"{idea_instruction}"
        f"Allowed control types:\n{chr(10).join(allowed_type_lines) if allowed_type_lines else 'None'}\n\n"
        f"Branch domains:\n{chr(10).join(domain_lines) if domain_lines else 'None'}\n\n"
        f"Existing controls:\n{chr(10).join(control_lines) if control_lines else 'None'}\n\n"
        f"Return exactly {count} suggestions."
    )
    return system_prompt, user_prompt


def _build_control_suggestion_prompt_text(
    context: dict[str, object],
    control_types: list[dict[str, object]],
    request: ControlSuggestionRequest,
) -> str:
    system_prompt, user_prompt = _build_control_suggestion_prompt(context, control_types, request)
    return f"{system_prompt}\n\n{user_prompt}"


def _build_control_insert_preview(
    domain_code: str,
    control_type_code: str,
    control_code: str,
    display_name: str,
    description: str | None,
    control_objective: str | None,
    evidence_expectation: str | None,
) -> str:
    domain_literal = _sql_nvarchar_literal(domain_code)
    type_literal = _sql_nvarchar_literal(control_type_code)
    code_literal = _sql_nvarchar_literal(control_code)
    name_literal = _sql_nvarchar_literal(display_name)
    description_literal = _sql_nvarchar_literal(description)
    objective_literal = _sql_nvarchar_literal(control_objective)
    evidence_literal = _sql_nvarchar_literal(evidence_expectation)

    return (
        f"DECLARE @DomainCode NVARCHAR(100) = {domain_literal};\n"
        f"DECLARE @ControlTypeCode NVARCHAR(50) = {type_literal};\n"
        f"DECLARE @ControlCode NVARCHAR(100) = {code_literal};\n\n"
        "DECLARE @CreatedControl TABLE (ControlId UNIQUEIDENTIFIER);\n\n"
        "INSERT INTO dbo.Controls (\n"
        "    ControlTypeId,\n"
        "    ControlCode,\n"
        "    DisplayName,\n"
        "    Description,\n"
        "    ControlObjective,\n"
        "    EvidenceExpectation,\n"
        "    Status\n"
        ")\n"
        "OUTPUT inserted.ControlId INTO @CreatedControl\n"
        "SELECT\n"
        "    ct.ID,\n"
        f"    @ControlCode,\n"
        f"    {name_literal},\n"
        f"    {description_literal},\n"
        f"    {objective_literal},\n"
        f"    {evidence_literal},\n"
        "    'Active'\n"
        "FROM dbo.ControlTypes ct\n"
        "WHERE ct.CODE = @ControlTypeCode\n"
        "  AND NOT EXISTS (\n"
        "      SELECT 1\n"
        "      FROM dbo.Controls existing\n"
        "      WHERE existing.ControlCode = @ControlCode\n"
        "  );\n\n"
        "INSERT INTO dbo.DomainControls (\n"
        "    DomainId,\n"
        "    ControlId,\n"
        "    RelationshipType,\n"
        "    IsPrimary,\n"
        "    DisplayOrder\n"
        ")\n"
        "SELECT\n"
        "    d.DomainId,\n"
        "    c.ControlId,\n"
        "    'Primary',\n"
        "    1,\n"
        "    COALESCE((\n"
        "        SELECT MAX(existing.DisplayOrder) + 10\n"
        "        FROM dbo.DomainControls existing\n"
        "        WHERE existing.DomainId = d.DomainId\n"
        "    ), 10)\n"
        "FROM dbo.Domains d\n"
        "CROSS JOIN @CreatedControl c\n"
        "WHERE d.DomainCode = @DomainCode;"
    )


def _extract_json_object(raw_text: str) -> dict[str, object]:
    text = (raw_text or "").strip()
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?\s*", "", text, flags=re.IGNORECASE)
        text = re.sub(r"\s*```$", "", text)

    start = text.find("{")
    end = text.rfind("}")
    if start < 0 or end <= start:
        raise ValueError("The model did not return a JSON object.")

    return json.loads(text[start : end + 1])


def _normalize_domain_type_code(domain_type: str) -> str:
    return re.sub(r"[^A-Z0-9]+", "_", (domain_type or "").strip().upper()).strip("_")


def _slugify_code(value: str) -> str:
    code = re.sub(r"[^a-z0-9]+", "-", value.strip().lower()).strip("-")
    if not code:
        raise ValueError("Code must contain at least one letter or digit.")
    return code[:100]


def _sql_nvarchar_literal(value: str | None) -> str:
    if value is None:
        return "NULL"
    escaped = value.replace("'", "''")
    return f"N'{escaped}'"


def _resolve_domain_type(domain_types: list[dict[str, object]], requested_type: str) -> dict[str, object]:
    normalized_requested = _normalize_domain_type_code(requested_type)
    for domain_type in domain_types:
        code = _normalize_domain_type_code(str(domain_type.get("CODE") or ""))
        name = _normalize_domain_type_code(str(domain_type.get("NAME") or ""))
        if normalized_requested and normalized_requested in {code, name}:
            return domain_type
    raise ValueError(f"Domain type '{requested_type}' is not valid for child domain creation.")


def _build_child_domain_insert_preview(
    parent_domain_code: str,
    domain_type_code: str,
    domain_code: str,
    display_name: str,
    description: str | None,
) -> str:
    parent_literal = _sql_nvarchar_literal(parent_domain_code)
    type_literal = _sql_nvarchar_literal(domain_type_code)
    code_literal = _sql_nvarchar_literal(domain_code)
    name_literal = _sql_nvarchar_literal(display_name.strip())
    description_literal = _sql_nvarchar_literal(description.strip() if description else None)

    return (
        f"DECLARE @ParentDomainCode NVARCHAR(100) = {parent_literal};\n"
        f"DECLARE @DomainTypeCode NVARCHAR(50) = {type_literal};\n\n"
        "INSERT INTO dbo.Domains (\n"
        "    DomainParentId,\n"
        "    DomainTypeId,\n"
        "    DomainOrientationId,\n"
        "    DisplayOrder,\n"
        "    DomainCode,\n"
        "    DisplayName,\n"
        "    Description\n"
        ")\n"
        "SELECT\n"
        "    parent.DomainId,\n"
        "    dt.ID,\n"
        "    parent.DomainOrientationId,\n"
        "    COALESCE((\n"
        "        SELECT MAX(sibling.DisplayOrder) + 10\n"
        "        FROM dbo.Domains sibling\n"
        "        WHERE sibling.Status = 'Active' AND sibling.DomainParentId = parent.DomainId\n"
        "    ), 10),\n"
        f"    {code_literal},\n"
        f"    {name_literal},\n"
        f"    {description_literal}\n"
        "FROM dbo.Domains parent\n"
        "JOIN dbo.DomainTypes dt\n"
        "    ON dt.CODE = @DomainTypeCode\n"
        "WHERE parent.DomainCode = @ParentDomainCode AND parent.Status = 'Active';"
    )


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

    @app.get("/domain-types")
    def domain_types() -> list[dict[str, object]]:
        return list_domain_types(settings)

    @app.get("/domain-orientations")
    def domain_orientations() -> list[dict[str, object]]:
        return list_domain_orientations(settings)

    @app.get("/control-types")
    def control_types() -> list[dict[str, object]]:
        return list_control_types(settings)

    @app.get("/controls")
    def controls(branchRootDomainCode: str) -> list[dict[str, object]]:
        try:
            return list_controls_for_branch(settings, branchRootDomainCode)
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/controls/suggest")
    def suggest_controls(request: ControlSuggestionRequest) -> dict[str, object]:
        try:
            context = get_control_suggestion_context(settings, request.branchRootDomainCode)
            control_types = list_control_types(settings)
            prompt = _build_control_suggestion_prompt_text(context, control_types, request)
            payload = _generate_with_ollama(settings, prompt, model=request.model)
            parsed = _extract_json_object(str(payload.get("response") or ""))
            suggestions = parsed.get("suggestions")
            if not isinstance(suggestions, list):
                raise ValueError("The model response did not include a suggestions array.")

            allowed_type_codes = {
                str(item.get("CODE") or "").upper()
                for item in control_types
                if item.get("CODE")
            }
            branch_domain_codes = {
                str(item.get("DomainCode") or "")
                for item in context.get("branchDomains", [])
                if item.get("DomainCode")
            }
            root_domain_code = _slugify_code(str(context.get("rootDomain", {}).get("DomainCode") or request.branchRootDomainCode))
            normalized_suggestions: list[dict[str, object]] = []
            for item in suggestions[: max(1, min(request.count, 10))]:
                if not isinstance(item, dict):
                    continue
                display_name = str(item.get("displayName") or "").strip()
                control_type_code = str(item.get("controlTypeCode") or "").strip().upper()
                if (
                    not display_name
                    or control_type_code not in allowed_type_codes
                    or root_domain_code not in branch_domain_codes
                ):
                    continue
                normalized_suggestions.append(
                    {
                        "displayName": display_name,
                        "controlTypeCode": control_type_code,
                        "domainCode": root_domain_code,
                        "controlCode": _slugify_code(f"{root_domain_code}-{display_name}")[:100],
                        "description": str(item.get("description") or "").strip(),
                        "controlObjective": str(item.get("controlObjective") or "").strip(),
                        "evidenceExpectation": str(item.get("evidenceExpectation") or "").strip(),
                    }
                )

            for suggestion in normalized_suggestions:
                suggestion["sqlPreview"] = _build_control_insert_preview(
                    domain_code=str(suggestion["domainCode"]),
                    control_type_code=str(suggestion["controlTypeCode"]),
                    control_code=str(suggestion["controlCode"]),
                    display_name=str(suggestion["displayName"]),
                    description=str(suggestion["description"]),
                    control_objective=str(suggestion["controlObjective"]),
                    evidence_expectation=str(suggestion["evidenceExpectation"]),
                )

            return {
                "suggestions": normalized_suggestions,
                "metrics": _extract_metrics(payload, request.model),
            }
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/controls/suggest-preview")
    def preview_control_suggestion(request: ControlSuggestionRequest) -> PromptPreviewResponse:
        try:
            context = get_control_suggestion_context(settings, request.branchRootDomainCode)
            control_types = list_control_types(settings)
            system_prompt, user_prompt = _build_control_suggestion_prompt(context, control_types, request)
            return PromptPreviewResponse(
                model=request.model or settings.ollama_chat_model,
                systemPrompt=system_prompt,
                userPrompt=user_prompt,
            )
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/controls/suggest/execute")
    def execute_control_suggestion(request: ExecuteControlSuggestionRequest) -> dict[str, object]:
        try:
            control_code = request.controlCode or _slugify_code(f"{request.domainCode}-{request.displayName}")
            created_control = create_control_from_suggestion(
                settings,
                domain_code=request.domainCode,
                control_type_code=request.controlTypeCode,
                control_code=control_code,
                display_name=request.displayName,
                description=request.description,
                control_objective=request.controlObjective,
                evidence_expectation=request.evidenceExpectation,
            )
            return {
                "createdControl": created_control,
                "sqlPreview": _build_control_insert_preview(
                    domain_code=request.domainCode,
                    control_type_code=request.controlTypeCode,
                    control_code=control_code,
                    display_name=request.displayName,
                    description=request.description,
                    control_objective=request.controlObjective,
                    evidence_expectation=request.evidenceExpectation,
                ),
            }
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/domains")
    def add_domain(request: CreateDomainRequest) -> dict[str, object]:
        return create_domain(
            settings,
            domain_code=request.domainCode,
            domain_type_id=request.domainTypeId,
            domain_orientation_id=request.domainOrientationId,
            display_name=request.displayName,
            description=request.description,
            domain_parent_id=request.domainParentId,
        )

    @app.post("/domains/assist")
    def assist_domain(request: DomainAssistRequest) -> dict[str, object]:
        instruction = request.instruction.strip()
        if not instruction:
            raise HTTPException(status_code=400, detail="Instruction is required.")

        domain_context = get_domain_assist_context(settings, request.domainCode)
        compiled_prompt = _build_domain_assist_prompt(domain_context, instruction, request.draftText)
        answer_payload = _generate_with_ollama(settings, compiled_prompt, model=request.model)
        answer = str(answer_payload.get("response", "")).strip()

        return {
            "answer": answer,
            "systemPromptLabel": "Domain curation assist",
            "metrics": _extract_metrics(answer_payload, model=request.model),
        }

    @app.post("/domains/assist-preview")
    def assist_domain_preview(request: DomainAssistRequest) -> PromptPreviewResponse:
        instruction = request.instruction.strip()
        if not instruction:
            raise HTTPException(status_code=400, detail="Instruction is required.")

        domain_context = get_domain_assist_context(settings, request.domainCode)
        system_prompt, user_prompt = _build_domain_assist_prompt_parts(
            domain_context,
            instruction,
            request.draftText,
        )
        return PromptPreviewResponse(
            model=request.model or settings.ollama_chat_model,
            systemPrompt=system_prompt,
            userPrompt=user_prompt,
        )

    @app.post("/domains/suggest-child")
    def suggest_child_domain(request: DomainChildSuggestionRequest) -> dict[str, object]:
        instruction = request.instruction.strip()
        if not instruction:
            raise HTTPException(status_code=400, detail="Instruction is required.")

        domain_context = get_domain_assist_context(settings, request.parentDomainCode)
        domain_types = list_domain_types(settings)
        compiled_prompt = _build_child_domain_suggestion_prompt(
            domain_context,
            instruction,
            request.draftText,
            domain_types,
        )
        answer_payload = _generate_with_ollama(settings, compiled_prompt, model=request.model)
        raw_answer = str(answer_payload.get("response", "")).strip()

        try:
            parsed = _extract_json_object(raw_answer)
            display_name = str(parsed.get("displayName") or "").strip()
            description = str(parsed.get("description") or "").strip()
            requested_type = str(parsed.get("domainType") or "").strip()
            if not display_name:
                raise ValueError("The model did not return a displayName.")
            if not requested_type:
                raise ValueError("The model did not return a domainType.")

            resolved_domain_type = _resolve_domain_type(domain_types, requested_type)
            domain_type_code = str(resolved_domain_type.get("CODE") or "").strip().upper()
            domain_code = _slugify_code(str(parsed.get("domainCode") or display_name))
            sql_preview = _build_child_domain_insert_preview(
                str(request.parentDomainCode).strip(),
                domain_type_code,
                domain_code,
                display_name,
                description or None,
            )
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=f"Child suggestion parsing failed: {exc}") from exc
        except json.JSONDecodeError as exc:
            raise HTTPException(status_code=400, detail=f"Child suggestion JSON was invalid: {exc}") from exc

        return {
            "suggestion": {
                "displayName": display_name,
                "description": description,
                "domainType": domain_type_code,
                "domainCode": domain_code,
            },
            "sqlPreview": sql_preview,
            "systemPromptLabel": "Child domain suggestion assist",
            "metrics": _extract_metrics(answer_payload, model=request.model),
        }

    @app.post("/domains/suggest-child-preview")
    def suggest_child_domain_preview(request: DomainChildSuggestionRequest) -> PromptPreviewResponse:
        instruction = request.instruction.strip()
        if not instruction:
            raise HTTPException(status_code=400, detail="Instruction is required.")

        domain_context = get_domain_assist_context(settings, request.parentDomainCode)
        domain_types = list_domain_types(settings)
        system_prompt, user_prompt = _build_child_domain_suggestion_prompt_parts(
            domain_context,
            instruction,
            request.draftText,
            domain_types,
        )
        return PromptPreviewResponse(
            model=request.model or settings.ollama_chat_model,
            systemPrompt=system_prompt,
            userPrompt=user_prompt,
        )

    @app.post("/domains/suggest-child/execute")
    def execute_suggested_child_domain(request: ExecuteDomainChildSuggestionRequest) -> dict[str, object]:
        domain_context = get_domain_assist_context(settings, request.parentDomainCode)
        parent_domain = domain_context.get("domain") or {}
        if not parent_domain:
            raise HTTPException(status_code=404, detail="Parent domain not found.")

        domain_types = list_domain_types(settings)
        try:
            resolved_domain_type = _resolve_domain_type(domain_types, request.domainType)
            domain_type_code = str(resolved_domain_type.get("CODE") or "").strip().upper()
            domain_code = _slugify_code(request.domainCode or request.displayName)
            sql_preview = _build_child_domain_insert_preview(
                request.parentDomainCode.strip(),
                domain_type_code,
                domain_code,
                request.displayName,
                request.description,
            )
            created_domain = create_domain(
                settings,
                domain_code=domain_code,
                domain_type_id=int(resolved_domain_type.get("ID")),
                domain_orientation_id=parent_domain.get("DomainOrientationId"),
                display_name=request.displayName,
                description=request.description,
                domain_parent_id=parent_domain.get("DomainId"),
            )
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

        return {
            "createdDomain": created_domain,
            "sqlPreview": sql_preview,
        }

    @app.put("/domains/{domainCode}")
    def edit_domain(domainCode: str, request: UpdateDomainRequest) -> dict[str, object]:
        return update_domain(
            settings,
            domain_code=domainCode,
            display_name=request.displayName,
            description=request.description,
            domain_type_id=request.domainTypeId,
            domain_orientation_id=request.domainOrientationId,
        )

    @app.put("/domain-sibling-order")
    def reorder_domains(request: ReorderDomainSiblingsRequest) -> dict[str, object]:
        return reorder_root_domains(
            settings,
            parent_domain_id=request.parentDomainId,
            orientation_code=request.orientationCode,
            ordered_domain_codes=request.orderedDomainCodes,
        )

    @app.get("/domains/{domainCode}/delete-preview")
    def domain_delete_preview(domainCode: str) -> dict[str, object]:
        return get_domain_delete_preview(settings, domainCode)

    @app.delete("/domains/{domainCode}")
    def remove_domain(domainCode: str) -> dict[str, object]:
        return delete_domain(settings, domainCode)

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

    @app.get("/collections/{collectionCode}/delete-preview")
    def collection_delete_preview(collectionCode: str) -> dict[str, object]:
        return get_collection_delete_preview(settings, collectionCode)

    @app.delete("/collections/{collectionCode}")
    def remove_collection(collectionCode: str) -> dict[str, object]:
        return delete_collection(settings, collectionCode)

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
