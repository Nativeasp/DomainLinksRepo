import asyncio
import hashlib
from collections import deque
from datetime import datetime, timezone
import html
from pathlib import Path
from threading import Lock
import time
import uuid

from fastapi import FastAPI, File, HTTPException, UploadFile
import httpx
from pydantic import BaseModel
import re
import json
import base64
from fastapi.responses import HTMLResponse, StreamingResponse

from .config import get_settings
from .db import ping_database
from .document_ingest import extract_pdf_text
from .repositories import (
    archive_document,
    clear_policy_tables,
    create_control_from_suggestion,
    delete_control,
    create_collection,
    create_domain,
    create_domain_type,
    create_text_document,
    delete_collection,
    delete_domain,
    delete_policy,
    delete_content_unit,
    get_collection_delete_preview,
    list_policy_control_explanations,
    get_control_suggestion_context,
    get_default_embedding_profile,
    get_domain_delete_preview,
    get_latest_policy_for_root_domain,
    list_context_units_for_collections,
    get_policy_presentation_data,
    get_retrieval_profile,
    get_recent_context_units,
    get_domain_assist_context,
    has_user_chat_backup_files,
    list_collection_documents,
    list_document_chunks,
    list_embedding_status,
    list_collections,
    list_controls_for_branch,
    list_controls_report_rows,
    list_policies,
    list_control_types,
    list_domains,
    list_domain_orientations,
    list_domain_types,
    reorder_domain_types,
    reorder_root_domains,
    list_retrieval_profiles,
    list_user_chat_backup_files,
    mark_user_chat_backup_files_restored,
    move_domain,
    search_similar_content_units,
    list_unembedded_content_units,
    upsert_content_unit_embeddings,
    upsert_policy_draft,
    upsert_app_user,
    upsert_user_chat_backup_file,
    update_collection,
    update_domain,
    upsert_policy_control_explanation,
)


_llm_trace_lock = Lock()
_llm_traces: deque[dict[str, object]] = deque(maxlen=200)


def _append_llm_trace(
    *,
    trace_type: str,
    model: str,
    prompt: str,
    response_text: str = "",
    success: bool = True,
    error: str | None = None,
    duration_seconds: float | None = None,
    label: str | None = None,
    metadata: dict[str, object] | None = None,
) -> None:
    with _llm_trace_lock:
        trace_record = {
            "traceId": str(uuid.uuid4()),
            "createdAtUtc": datetime.now(timezone.utc).isoformat(),
            "traceType": trace_type,
            "label": (label or "ollama").strip(),
            "model": model,
            "prompt": prompt,
            "responseText": response_text,
            "success": success,
            "error": error or "",
            "durationSeconds": round(duration_seconds or 0.0, 3),
            "promptChars": len(prompt),
            "responseChars": len(response_text),
        }
        if metadata:
            trace_record["metadata"] = metadata
        _llm_traces.appendleft(trace_record)


def _list_llm_traces() -> list[dict[str, object]]:
    with _llm_trace_lock:
        return [dict(item) for item in _llm_traces]


def _build_llm_trace_html(base_url: str, traces: list[dict[str, object]]) -> str:
    def metric_card(label: str, value_html: str, definition: str) -> str:
        escaped_definition = html.escape(definition)
        return (
            f'<div class="metric-card" role="button" tabindex="0" data-help-title="{html.escape(label)}" data-help-definition="{escaped_definition}">'
            f'<strong>{html.escape(label)}</strong>'
            f'<span>{value_html}</span>'
            f"</div>"
        )

    cards: list[str] = []
    for trace in traces:
        metadata = trace.get("metadata") if isinstance(trace.get("metadata"), dict) else {}
        status_label = "Success" if trace.get("success") else "Error"
        status_class = "ok" if trace.get("success") else "error"
        error_html = (
            f"<div class='error-block'><strong>Error</strong><pre>{html.escape(str(trace.get('error') or ''))}</pre></div>"
            if trace.get("error")
            else ""
        )
        context_html = ""
        if metadata:
            used_collection_codes = metadata.get("usedCollectionCodes") or []
            used_collection_text = ", ".join(str(code) for code in used_collection_codes) if isinstance(used_collection_codes, list) else ""
            retrieved_sources = metadata.get("retrievedSourceNames") or []
            retrieved_sources_text = ", ".join(str(name) for name in retrieved_sources) if isinstance(retrieved_sources, list) else ""
            context_html = f"""
              <section>
                <h3>Context</h3>
                <div class="meta-grid">
                  {metric_card("Retrieval mode", html.escape(str(metadata.get("retrievalMode") or "FullContext")), "How the backend assembled context before sending the prompt to the model.")}
                  {metric_card("Profile", html.escape(str(metadata.get("retrievalProfileCode") or "--")), "The retrieval profile that supplied retrieval defaults like top-k and whole-document behavior.")}
                  {metric_card("Context units", html.escape(str(metadata.get("contextUnitCount") or 0)), "How many retrieved chunks or units were inserted into the context block.")}
                  {metric_card("Context size", html.escape(str(metadata.get("contextChars") or 0)) + " chars", "The character length of the retrieved context block only, not the whole compiled prompt.")}
                  {metric_card("User prompt", html.escape(str(metadata.get("userPromptChars") or 0)) + " chars / " + html.escape(str(metadata.get("userPromptTokensEstimated") or 0)) + " est.", "The size of the current user prompt by characters and estimated tokens.")}
                  {metric_card("Context tokens", html.escape(str(metadata.get("contextTokensActual") or 0)) + " actual / " + html.escape(str(metadata.get("contextTokensEstimated") or 0)) + " est.", "Token footprint of the retrieved context block. 'Actual' comes from chunk token counts; 'est.' is a fallback approximation.")}
                </div>
                <div class="meta-grid">
                  {metric_card("Conversation history", html.escape(str(metadata.get("historyMessageCount") or 0)) + " messages / " + html.escape(str(metadata.get("historyChars") or 0)) + " chars / " + html.escape(str(metadata.get("historyTokensEstimated") or 0)) + " est.", "The chat history included in the compiled prompt before the current response was generated.")}
                  {metric_card("Compiled prompt", html.escape(str(trace.get("promptChars") or 0)) + " chars / " + html.escape(str(metadata.get("compiledPromptTokensEstimated") or 0)) + " est.", "The full prompt sent to the model, including instructions, history, user prompt, and retrieved context.")}
                  {metric_card("Collections", html.escape(used_collection_text or "--"), "The short-memory and long-term collection codes that were eligible to contribute context.")}
                  {metric_card("Retrieved sources", html.escape(retrieved_sources_text or "--"), "Distinct source documents that contributed retrieved context to this prompt.")}
                  {metric_card("Fallback", html.escape(str(metadata.get("fallbackReason") or "--")), "Why the backend fell back or issued a warning, such as no vector hits or missing embeddings.")}
                  {metric_card("Measure", "LLM context is primarily measured in tokens.", "The model's hard budget is tokens, not characters or document count.")}
                </div>
              </section>
            """
        response_html = html.escape(str(trace.get("responseText") or ""))
        prompt_html = html.escape(str(trace.get("prompt") or ""))
        cards.append(
            f"""
            <article class="trace-card">
              <header class="trace-header">
                <div>
                  <h2>{html.escape(str(trace.get("label") or "ollama"))}</h2>
                  <div class="meta">{html.escape(str(trace.get("createdAtUtc") or ""))}</div>
                </div>
                <span class="status {status_class}">{status_label}</span>
              </header>
              <div class="meta-grid">
                {metric_card("Model", html.escape(str(trace.get("model") or "")), "The model name reported by the backend or Ollama for this request.")}
                {metric_card("Type", html.escape(str(trace.get("traceType") or "")), "Whether this was a normal one-shot generation or a streaming response.")}
                {metric_card("Duration", html.escape(str(trace.get("durationSeconds") or 0)) + "s", "Elapsed backend time for the model call, measured in seconds.")}
                {metric_card("Sizes", html.escape(str(trace.get("promptChars") or 0)) + " / " + html.escape(str(trace.get("responseChars") or 0)) + " chars", "Character sizes for the compiled prompt and the final model response.")}
              </div>
              {context_html}
              {error_html}
              <section>
                <h3>Prompt</h3>
                <pre>{prompt_html}</pre>
              </section>
              <section>
                <h3>Response</h3>
                <pre>{response_html}</pre>
              </section>
            </article>
            """
        )

    body = "\n".join(cards) if cards else "<p class='empty'>No LLM traces captured yet.</p>"
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>LLM Traces</title>
  <style>
    body {{
      margin: 0;
      background: #f7f7f7;
      color: #111;
      font-family: "Segoe UI", Arial, sans-serif;
    }}
    .page {{
      max-width: 1280px;
      margin: 0 auto;
      padding: 24px;
    }}
    .page-header {{
      display: flex;
      justify-content: space-between;
      align-items: end;
      gap: 16px;
      margin-bottom: 20px;
    }}
    .page-header h1 {{
      margin: 0;
      font-size: 28px;
    }}
    .page-header p {{
      margin: 6px 0 0;
      color: #444;
    }}
    .actions a {{
      color: #111;
      text-decoration: none;
      border: 1px solid #bbb;
      padding: 8px 12px;
      border-radius: 6px;
      background: #fff;
      margin-left: 8px;
    }}
    .trace-list {{
      display: grid;
      gap: 18px;
    }}
    .trace-card {{
      background: #fff;
      border: 1px solid #d4d4d4;
      border-radius: 8px;
      padding: 18px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.04);
    }}
    .trace-header {{
      display: flex;
      justify-content: space-between;
      gap: 12px;
      align-items: start;
      margin-bottom: 12px;
    }}
    .trace-header h2 {{
      margin: 0;
      font-size: 18px;
    }}
    .meta, .meta-grid span {{
      color: #555;
      font-size: 13px;
    }}
    .meta-grid {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 10px;
      margin-bottom: 12px;
    }}
    .meta-grid div {{
      background: #fafafa;
      border: 1px solid #e2e2e2;
      border-radius: 6px;
      padding: 8px 10px;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }}
    .metric-card {{
      cursor: pointer;
      transition: border-color 120ms ease, box-shadow 120ms ease, background 120ms ease;
    }}
    .metric-card:hover,
    .metric-card:focus {{
      border-color: #99adc2;
      box-shadow: 0 0 0 2px rgba(24, 52, 74, 0.08);
      background: #f5f8fb;
      outline: none;
    }}
    .status {{
      display: inline-block;
      padding: 4px 8px;
      border-radius: 999px;
      font-size: 12px;
      border: 1px solid #bbb;
    }}
    .status.ok {{
      background: #f2f2f2;
    }}
    .status.error {{
      background: #fff0f0;
      border-color: #c99;
    }}
    h3 {{
      margin: 14px 0 8px;
      font-size: 15px;
    }}
    pre {{
      margin: 0;
      padding: 12px;
      background: #fafafa;
      border: 1px solid #e0e0e0;
      border-radius: 6px;
      white-space: pre-wrap;
      word-break: break-word;
      font-family: Consolas, "Courier New", monospace;
      font-size: 12px;
      line-height: 1.45;
    }}
    .error-block {{
      margin-bottom: 12px;
    }}
    .empty {{
      background: #fff;
      border: 1px solid #ddd;
      border-radius: 8px;
      padding: 18px;
    }}
    .help-overlay {{
      position: fixed;
      inset: 0;
      display: none;
      align-items: center;
      justify-content: center;
      background: rgba(0, 0, 0, 0.35);
      padding: 20px;
      z-index: 1000;
    }}
    .help-overlay.is-open {{
      display: flex;
    }}
    .help-dialog {{
      width: min(540px, 100%);
      background: #fff;
      border: 1px solid #d6d6d6;
      border-radius: 10px;
      box-shadow: 0 20px 50px rgba(0, 0, 0, 0.18);
      padding: 18px;
    }}
    .help-dialog h2 {{
      margin: 0 0 10px;
      font-size: 20px;
    }}
    .help-dialog p {{
      margin: 0;
      color: #333;
      line-height: 1.5;
    }}
    .help-dialog-actions {{
      margin-top: 16px;
      display: flex;
      justify-content: flex-end;
    }}
    .help-dialog button {{
      height: 34px;
      padding: 0 14px;
      border: 1px solid #bbb;
      border-radius: 6px;
      background: #fff;
      cursor: pointer;
      font: inherit;
    }}
  </style>
</head>
<body>
  <main class="page">
    <header class="page-header">
      <div>
        <h1>LLM Traces</h1>
        <p>Live local backend trace of prompts and responses sent through the Ollama wrapper.</p>
      </div>
      <div class="actions">
        <a href="{html.escape(base_url.rstrip('/') + '/debug/llm-traces.json')}">JSON</a>
        <a href="{html.escape(base_url.rstrip('/') + '/debug/llm-traces')}">Refresh</a>
      </div>
    </header>
    <section class="trace-list">
      {body}
    </section>
  </main>
  <div id="help-overlay" class="help-overlay" aria-hidden="true">
    <div class="help-dialog" role="dialog" aria-modal="true" aria-labelledby="help-title">
      <h2 id="help-title">Metric help</h2>
      <p id="help-body"></p>
      <div class="help-dialog-actions">
        <button id="help-close-button" type="button">Close</button>
      </div>
    </div>
  </div>
  <script>
    (function () {{
      const overlay = document.getElementById('help-overlay');
      const title = document.getElementById('help-title');
      const body = document.getElementById('help-body');
      const closeButton = document.getElementById('help-close-button');
      if (!overlay || !title || !body || !closeButton) {{
        return;
      }}

      function openHelp(helpTitle, helpDefinition) {{
        title.textContent = helpTitle || 'Metric help';
        body.textContent = helpDefinition || '';
        overlay.classList.add('is-open');
        overlay.setAttribute('aria-hidden', 'false');
        closeButton.focus();
      }}

      function closeHelp() {{
        overlay.classList.remove('is-open');
        overlay.setAttribute('aria-hidden', 'true');
      }}

      document.querySelectorAll('.metric-card').forEach((card) => {{
        card.addEventListener('click', () => {{
          openHelp(card.getAttribute('data-help-title'), card.getAttribute('data-help-definition'));
        }});
        card.addEventListener('keydown', (event) => {{
          if (event.key === 'Enter' || event.key === ' ') {{
            event.preventDefault();
            openHelp(card.getAttribute('data-help-title'), card.getAttribute('data-help-definition'));
          }}
        }});
      }});

      overlay.addEventListener('click', (event) => {{
        if (event.target === overlay) {{
          closeHelp();
        }}
      }});
      closeButton.addEventListener('click', closeHelp);
      document.addEventListener('keydown', (event) => {{
        if (event.key === 'Escape' && overlay.classList.contains('is-open')) {{
          closeHelp();
        }}
      }});
    }})();
  </script>
</body>
</html>"""


def _resolve_backend_base_url(settings) -> str:
    host = (getattr(settings, "api_host", None) or "127.0.0.1").strip()
    port = int(getattr(settings, "api_port", 5056) or 5056)
    return f"http://{host}:{port}"


def _estimate_token_count(text: str) -> int:
    stripped = (text or "").strip()
    if not stripped:
        return 0
    return max(1, len(stripped) // 4)


def _build_embedding_debug_html(base_url: str, status_payload: dict[str, object]) -> str:
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Embedding Status</title>
  <style>
    body {{
      margin: 0;
      background: #f7f7f7;
      color: #111;
      font-family: "Segoe UI", Arial, sans-serif;
    }}
    .page {{
      max-width: 1100px;
      margin: 0 auto;
      padding: 24px;
    }}
    .summary, .collection-table, .actions {{
      background: #fff;
      border: 1px solid #ddd;
      border-radius: 8px;
      padding: 16px;
      margin-bottom: 18px;
    }}
    .stats {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 10px;
    }}
    .stat {{
      border: 1px solid #e4e4e4;
      background: #fafafa;
      border-radius: 6px;
      padding: 10px;
    }}
    table {{
      width: 100%;
      border-collapse: collapse;
    }}
    th, td {{
      text-align: left;
      padding: 8px;
      border-bottom: 1px solid #ececec;
      font-size: 13px;
    }}
    input, button {{
      height: 32px;
      padding: 4px 10px;
      font: inherit;
    }}
    .actions-row {{
      display: flex;
      gap: 10px;
      align-items: end;
      flex-wrap: wrap;
    }}
    .status {{
      margin-top: 10px;
      color: #444;
      white-space: pre-wrap;
    }}
  </style>
</head>
<body>
  <main class="page">
    <section class="summary">
      <h1>Embedding Status</h1>
      <p>Monitor chunk embeddings and run small manual backfills.</p>
      <div class="stats">
        <div class="stat"><strong>Profile</strong><div>{html.escape(str(status_payload.get("profileCode") or ""))}</div></div>
        <div class="stat"><strong>Model</strong><div>{html.escape(str(status_payload.get("modelName") or ""))}</div></div>
        <div class="stat"><strong>Total chunks</strong><div id="total-count">{html.escape(str(status_payload.get("totalContentUnitCount") or 0))}</div></div>
        <div class="stat"><strong>Embedded</strong><div id="embedded-count">{html.escape(str(status_payload.get("embeddedContentUnitCount") or 0))}</div></div>
        <div class="stat"><strong>Unembedded</strong><div id="unembedded-count">{html.escape(str(status_payload.get("unembeddedContentUnitCount") or 0))}</div></div>
        <div class="stat"><strong>Total tokens</strong><div id="total-tokens">{html.escape(str(status_payload.get("totalTokenCount") or 0))}</div></div>
        <div class="stat"><strong>Embedded tokens</strong><div id="embedded-tokens">{html.escape(str(status_payload.get("embeddedTokenCount") or 0))}</div></div>
        <div class="stat"><strong>Unembedded tokens</strong><div id="unembedded-tokens">{html.escape(str(status_payload.get("unembeddedTokenCount") or 0))}</div></div>
      </div>
    </section>
    <section class="actions">
      <h2>Backfill</h2>
      <div class="actions-row">
        <label>Collection code<br><input id="collection-code" type="text" placeholder="optional"></label>
        <label>Limit<br><input id="backfill-limit" type="number" value="25" min="1" max="2000"></label>
        <button id="backfill-button" type="button">Run Backfill</button>
        <button id="refresh-button" type="button">Refresh</button>
      </div>
      <div id="backfill-status" class="status"></div>
    </section>
    <section class="collection-table">
      <h2>Collections</h2>
      <table>
        <thead>
          <tr>
            <th>Collection</th>
            <th>Total</th>
            <th>Embedded</th>
            <th>Unembedded</th>
            <th>Total tokens</th>
            <th>Embedded tokens</th>
            <th>Unembedded tokens</th>
          </tr>
        </thead>
        <tbody id="collections-body"></tbody>
      </table>
    </section>
  </main>
  <script>
    const baseUrl = {json.dumps(base_url.rstrip('/'))};
    const initialStatus = {json.dumps(status_payload)};

    function renderStatus(payload) {{
      document.getElementById('total-count').textContent = payload.totalContentUnitCount ?? 0;
      document.getElementById('embedded-count').textContent = payload.embeddedContentUnitCount ?? 0;
      document.getElementById('unembedded-count').textContent = payload.unembeddedContentUnitCount ?? 0;
      document.getElementById('total-tokens').textContent = payload.totalTokenCount ?? 0;
      document.getElementById('embedded-tokens').textContent = payload.embeddedTokenCount ?? 0;
      document.getElementById('unembedded-tokens').textContent = payload.unembeddedTokenCount ?? 0;
      const tbody = document.getElementById('collections-body');
      tbody.innerHTML = '';
      for (const collection of payload.collections || []) {{
        const row = document.createElement('tr');
        row.innerHTML = `
          <td>${{collection.CollectionDisplayName || collection.CollectionCode || ''}}<br><small>${{collection.CollectionCode || ''}}</small></td>
          <td>${{collection.TotalContentUnitCount || 0}}</td>
          <td>${{collection.EmbeddedContentUnitCount || 0}}</td>
          <td>${{collection.UnembeddedContentUnitCount || 0}}</td>
          <td>${{collection.TotalTokenCount || 0}}</td>
          <td>${{collection.EmbeddedTokenCount || 0}}</td>
          <td>${{collection.UnembeddedTokenCount || 0}}</td>
        `;
        tbody.appendChild(row);
      }}
    }}

    async function refreshStatus() {{
      const response = await fetch(baseUrl + '/debug/embedding-status');
      const payload = await response.json();
      renderStatus(payload);
      return payload;
    }}

    async function runBackfill() {{
      const collectionCode = document.getElementById('collection-code').value.trim();
      const limit = document.getElementById('backfill-limit').value || '25';
      const status = document.getElementById('backfill-status');
      status.textContent = 'Running backfill...';
      const url = new URL(baseUrl + '/debug/embeddings/backfill');
      url.searchParams.set('limit', limit);
      if (collectionCode) {{
        url.searchParams.set('collectionCode', collectionCode);
      }}
      const response = await fetch(url, {{ method: 'POST' }});
      const payload = await response.json();
      status.textContent = JSON.stringify(payload, null, 2);
      await refreshStatus();
    }}

    document.getElementById('backfill-button').addEventListener('click', () => {{
      runBackfill().catch(error => {{
        document.getElementById('backfill-status').textContent = String(error);
      }});
    }});
    document.getElementById('refresh-button').addEventListener('click', () => {{
      refreshStatus().catch(error => {{
        document.getElementById('backfill-status').textContent = String(error);
      }});
    }});
    renderStatus(initialStatus);
  </script>
</body>
</html>"""


def _normalize_retrieval_mode(value: str | None) -> str:
    normalized = (value or "").strip().lower().replace(" ", "").replace("-", "")
    if normalized in {"vectorrag", "vector"}:
        return "VectorRag"
    if normalized == "hybrid":
        return "Hybrid"
    return "FullContext"


def _vector_to_sql_literal(values: list[float]) -> str:
    return "[" + ",".join(f"{float(value):.7e}" for value in values) + "]"


def _build_embedding_hash(text: str) -> bytes:
    return hashlib.sha256(text.encode("utf-8")).digest()


def _request_ollama_embeddings(settings, inputs: list[str], model: str | None = None) -> list[list[float]]:
    selected_model = (model or settings.ollama_embed_model).strip()
    if not inputs:
        return []

    try:
        response = httpx.post(
            f"{settings.ollama_base_url}/api/embed",
            json={
                "model": selected_model,
                "input": inputs,
            },
            timeout=120,
        )
        response.raise_for_status()
        payload = response.json()
        embeddings = payload.get("embeddings")
        if isinstance(embeddings, list) and len(embeddings) == len(inputs):
            return [[float(value) for value in vector] for vector in embeddings]
    except Exception:
        pass

    vectors: list[list[float]] = []
    for text in inputs:
        response = httpx.post(
            f"{settings.ollama_base_url}/api/embeddings",
            json={
                "model": selected_model,
                "prompt": text,
            },
            timeout=120,
        )
        response.raise_for_status()
        payload = response.json()
        vector = payload.get("embedding")
        if not isinstance(vector, list):
            raise ValueError("Ollama embeddings response did not include an embedding vector.")
        vectors.append([float(value) for value in vector])
    return vectors


def _trim_context_units(
    rows: list[dict[str, object]],
    *,
    max_context_tokens: int | None,
) -> list[dict[str, object]]:
    if not max_context_tokens or max_context_tokens <= 0:
        return rows

    trimmed: list[dict[str, object]] = []
    running_total = 0
    for row in rows:
        token_count = int(row.get("TokenCount") or 0)
        if token_count <= 0:
            token_count = _estimate_token_count(str(row.get("BodyText") or ""))
        if trimmed and running_total + token_count > max_context_tokens:
            break
        trimmed.append(row)
        running_total += token_count
    return trimmed


def _build_source_items(rows: list[dict[str, object]]) -> list[dict[str, object]]:
    return [
        {
            "collectionCode": row.get("CollectionCode"),
            "collectionDisplayName": row.get("CollectionDisplayName") or row.get("CollectionCode"),
            "sourceName": row.get("SourceName") or "unknown",
            "contentUnitId": row.get("ContentUnitId"),
            "tokenCount": int(row.get("TokenCount") or 0),
        }
        for row in rows
    ]


def _build_context_lines(rows: list[dict[str, object]]) -> list[str]:
    lines: list[str] = []
    for row in rows:
        collection_display = row.get("CollectionDisplayName") or row.get("CollectionCode")
        source_name = row.get("SourceName") or "unknown"
        body = row.get("BodyText") or ""
        lines.append(f"[{collection_display} | {source_name}] {body}")
    return lines


def _build_policy_context_lines(policy_data: dict[str, object]) -> list[str]:
    policy = policy_data.get("policy") or {}
    title = str(policy.get("PolicyTitle") or "").strip()
    code = str(policy.get("PolicyCode") or "").strip()
    version = str(policy.get("VersionText") or "").strip()
    status = str(policy.get("Status") or "").strip()
    root_name = str(policy.get("RootDomainName") or "").strip()
    root_code = str(policy.get("RootDomainCode") or "").strip()

    lines = [
        f"[Saved Policy | {title or 'Policy'}] Domain={root_name} ({root_code}); Code={code}; Version={version}; Status={status}"
    ]

    def append_section(label: str, rows: list[dict[str, object]], limit: int = 5) -> None:
        for row in rows[:limit]:
            text = str(row.get("StatementText") or "").strip()
            if text:
                lines.append(f"[Saved Policy | {label}] {text}")

    append_section("Objective", policy_data.get("objectives") or [], limit=4)
    append_section("Principle", policy_data.get("principles") or [], limit=4)
    append_section("Accountability", policy_data.get("accountability") or [], limit=3)
    append_section("Transparency", policy_data.get("transparency") or [], limit=3)
    append_section("Strategy", policy_data.get("strategy") or [], limit=3)
    append_section("Consequence", policy_data.get("consequences") or [], limit=3)

    seen_controls: set[str] = set()
    grouped_rows: dict[str, list[dict[str, object]]] = {}
    for row in policy_data.get("controlStatements") or []:
        control_code = str(row.get("ControlCode") or "").strip()
        if not control_code:
            continue
        grouped_rows.setdefault(control_code, []).append(row)

    for control_code, rows in list(grouped_rows.items())[:8]:
        first = rows[0]
        control_name = str(first.get("ControlName") or control_code).strip()
        if control_code.lower() in seen_controls:
            continue
        seen_controls.add(control_code.lower())
        lines.append(
            f"[Saved Policy | Control] {control_name} ({control_code}) | "
            f"{str(first.get('ControlTypeName') or '').strip()} ({str(first.get('ControlTypeCode') or '').strip()})"
        )
        for row in rows[:3]:
            text = str(row.get("StatementText") or "").strip()
            if text:
                lines.append(f"[Saved Policy | Control Statement] {text}")

    return lines


def _build_domain_context_lines(domain_context: dict[str, object]) -> list[str]:
    domain = domain_context.get("domain") or {}
    display_name = str(domain.get("DisplayName") or domain.get("DomainCode") or "").strip()
    domain_code = str(domain.get("DomainCode") or "").strip()
    description = str(domain.get("Description") or "").strip()
    parent_path = str(domain_context.get("parentPath") or "").strip()

    lines = [
        f"[Domain Context | Domain] {display_name} ({domain_code})"
        + (f" | Path={parent_path}" if parent_path else "")
    ]
    if description:
        lines.append(f"[Domain Context | Summary] {description}")

    for item in (domain_context.get("childDomains") or [])[:8]:
        child_name = str(item.get("displayName") or item.get("domainCode") or "").strip()
        child_code = str(item.get("domainCode") or "").strip()
        child_type = str(item.get("domainType") or "").strip()
        if child_name:
            detail = f"[Domain Context | Child Domain] {child_name} ({child_code})"
            if child_type:
                detail += f" | Type={child_type}"
            lines.append(detail)

    for item in (domain_context.get("collections") or [])[:6]:
        collection_name = str(item.get("DisplayName") or item.get("CollectionCode") or "").strip()
        document_count = int(item.get("DocumentCount") or 0)
        collection_description = str(item.get("Description") or "").strip()
        if collection_name:
            lines.append(
                f"[Domain Context | Collection] {collection_name} | Documents={document_count}"
                + (f" | {collection_description}" if collection_description else "")
            )

    return lines


def _build_controls_context_lines(control_rows: list[dict[str, object]]) -> list[str]:
    lines: list[str] = []
    for row in control_rows[:12]:
        control_name = str(row.get("DisplayName") or row.get("ControlCode") or "").strip()
        control_code = str(row.get("ControlCode") or "").strip()
        control_type = str(row.get("ControlTypeName") or row.get("ControlTypeCode") or "").strip()
        domain_name = str(row.get("DomainDisplayName") or row.get("DomainCode") or "").strip()
        description = str(row.get("Description") or "").strip()
        objective = str(row.get("ControlObjective") or "").strip()
        evidence = str(row.get("EvidenceExpectation") or "").strip()
        if not control_name:
            continue
        lines.append(
            f"[Controls | Control] {control_name} ({control_code}) | Domain={domain_name} | Type={control_type}"
        )
        if description:
            lines.append(f"[Controls | Summary] {description}")
        if objective:
            lines.append(f"[Controls | Objective] {objective}")
        if evidence:
            lines.append(f"[Controls | Evidence] {evidence}")
    return lines


def _build_chat_trace_metadata(
    *,
    retrieval_mode: str,
    collection_codes: list[str],
    context_units: list[dict[str, object]],
    context_text: str,
    prompt: str,
    history_lines: list[str],
    history_text: str,
    compiled_prompt: str,
    retrieval_profile: dict[str, object] | None,
    policy_context_lines: list[str] | None = None,
    fallback_reason: str | None = None,
) -> dict[str, object]:
    actual_context_tokens = sum(
        int(row.get("TokenCount") or 0) if int(row.get("TokenCount") or 0) > 0 else _estimate_token_count(str(row.get("BodyText") or ""))
        for row in context_units
    )
    return {
        "retrievalMode": retrieval_mode,
        "usedCollectionCodes": collection_codes,
        "contextUnitCount": len(context_units),
        "contextChars": len(context_text),
        "contextTokensEstimated": _estimate_token_count(context_text),
        "contextTokensActual": actual_context_tokens,
        "userPromptChars": len(prompt),
        "userPromptTokensEstimated": _estimate_token_count(prompt),
        "historyMessageCount": len(history_lines),
        "historyChars": len(history_text),
        "historyTokensEstimated": _estimate_token_count(history_text),
        "compiledPromptTokensEstimated": _estimate_token_count(compiled_prompt),
        "retrievalProfileCode": str(retrieval_profile.get("ProfileCode") or "") if retrieval_profile else "",
        "retrievedChunkIds": [str(row.get("ContentUnitId") or "") for row in context_units if row.get("ContentUnitId")],
        "retrievedSourceNames": sorted(
            {
                str(row.get("SourceName") or "")
                for row in context_units
                if str(row.get("SourceName") or "").strip()
            }
        ),
        "policyContextLineCount": len(policy_context_lines or []),
        "policyContextChars": len("\n".join(policy_context_lines or [])),
        "fallbackOccurred": bool(fallback_reason),
        "fallbackReason": fallback_reason or "",
    }


def _expand_whole_documents(
    settings,
    rows: list[dict[str, object]],
    *,
    include_chunks: bool,
) -> list[dict[str, object]]:
    if not rows:
        return rows

    doc_metadata = {
        str(row.get("DocumentId") or ""): row
        for row in rows
        if str(row.get("DocumentId") or "").strip()
    }
    expanded_rows: list[dict[str, object]] = rows[:] if include_chunks else []
    seen_ids = {
        str(row.get("ContentUnitId") or "")
        for row in expanded_rows
        if str(row.get("ContentUnitId") or "").strip()
    }

    for document_id, metadata in doc_metadata.items():
        for chunk in list_document_chunks(settings, document_id):
            content_unit_id = str(chunk.get("ContentUnitId") or "")
            if content_unit_id and content_unit_id in seen_ids:
                continue
            expanded_rows.append(
                {
                    "CollectionCode": metadata.get("CollectionCode"),
                    "CollectionDisplayName": metadata.get("CollectionDisplayName"),
                    "DocumentId": document_id,
                    "SourceName": metadata.get("SourceName"),
                    "ContentUnitId": chunk.get("ContentUnitId"),
                    "UnitType": chunk.get("UnitType"),
                    "UnitOrdinal": chunk.get("UnitOrdinal"),
                    "TokenCount": chunk.get("TokenCount"),
                    "BodyText": chunk.get("BodyText"),
                    "Distance": metadata.get("Distance"),
                }
            )
            if content_unit_id:
                seen_ids.add(content_unit_id)
    return expanded_rows


def _dedupe_context_units(rows: list[dict[str, object]]) -> list[dict[str, object]]:
    deduped: list[dict[str, object]] = []
    seen_ids: set[str] = set()
    for row in rows:
        content_unit_id = str(row.get("ContentUnitId") or "")
        if content_unit_id and content_unit_id in seen_ids:
            continue
        deduped.append(row)
        if content_unit_id:
            seen_ids.add(content_unit_id)
    return deduped


def _ensure_embeddings_for_content(
    settings,
    *,
    document_id: str | None = None,
    collection_codes: list[str] | None = None,
    limit: int = 200,
) -> dict[str, object]:
    embedding_profile = get_default_embedding_profile(settings)
    vector_dimension = int(embedding_profile.get("VectorDimension") or 768)
    units = list_unembedded_content_units(
        settings,
        embedding_profile_id=str(embedding_profile["EmbeddingProfileId"]),
        limit=limit,
        collection_codes=collection_codes,
        document_id=document_id,
    )
    if not units:
        return {
            "profileCode": str(embedding_profile.get("ProfileCode") or ""),
            "processedCount": 0,
            "insertedCount": 0,
            "updatedCount": 0,
        }

    inputs = [str(unit.get("BodyText") or "") for unit in units]
    embeddings = _request_ollama_embeddings(settings, inputs, model=str(embedding_profile.get("ModelName") or ""))
    if len(embeddings) != len(units):
        raise ValueError("Embedding generation did not return the expected number of vectors.")

    upsert_result = upsert_content_unit_embeddings(
        settings,
        embedding_profile_id=str(embedding_profile["EmbeddingProfileId"]),
        vector_dimension=vector_dimension,
        embeddings=[
            {
                "contentUnitId": str(unit.get("ContentUnitId") or ""),
                "vectorText": _vector_to_sql_literal(vector),
                "embeddingHash": _build_embedding_hash(str(unit.get("BodyText") or "")),
            }
            for unit, vector in zip(units, embeddings, strict=False)
        ],
    )
    return {
        "profileCode": str(embedding_profile.get("ProfileCode") or ""),
        "processedCount": len(units),
        **upsert_result,
    }


def _retrieve_context_for_chat(settings, request: AskRequest) -> tuple[list[dict[str, object]], dict[str, object]]:
    checked_domain_collection_codes = [
        code for code in request.longTermCollectionCodes
        if str(code or "").strip()
    ]

    if not request.includeDocuments and not request.includeRag:
        retrieval_mode = "NoDocuments"
        return [], {
            "retrievalMode": retrieval_mode,
            "collectionCodes": checked_domain_collection_codes,
            "retrievalProfile": None,
            "fallbackReason": "Document context is turned off.",
        }

    if request.includeDocuments and not request.includeRag:
        rows = list_context_units_for_collections(settings, checked_domain_collection_codes)
        return rows, {
            "retrievalMode": "DocumentsOnly",
            "collectionCodes": checked_domain_collection_codes,
            "retrievalProfile": None,
            "fallbackReason": None if rows else "No active documents were found under the checked domain collections.",
        }

    collection_codes = checked_domain_collection_codes
    retrieval_mode = "DocumentsAndRag" if request.includeDocuments else "RagOnly"
    fallback_reason: str | None = None
    retrieval_profile: dict[str, object] | None = None

    profile_code = "domain-vector-default"
    retrieval_profile = get_retrieval_profile(settings, profile_code)
    if not retrieval_profile:
        fallback_reason = f"Retrieval profile '{profile_code}' is not configured."
        rows = list_context_units_for_collections(settings, collection_codes) if request.includeDocuments else []
        return rows, {
            "retrievalMode": retrieval_mode,
            "collectionCodes": collection_codes,
            "retrievalProfile": retrieval_profile,
            "fallbackReason": fallback_reason,
        }

    try:
        embedding_profile = get_default_embedding_profile(settings)
        vector_dimension = int(embedding_profile.get("VectorDimension") or 768)
        query_vector = _request_ollama_embeddings(settings, [request.prompt], model=str(embedding_profile.get("ModelName") or ""))[0]
        vector_rows = search_similar_content_units(
            settings,
            embedding_profile_id=str(embedding_profile["EmbeddingProfileId"]),
            vector_dimension=vector_dimension,
            query_vector_text=_vector_to_sql_literal(query_vector),
            collection_codes=collection_codes,
            top_k=int(retrieval_profile.get("TopK") or 8),
        )
    except Exception as exc:
        vector_rows = []
        fallback_reason = str(exc)

    if not vector_rows and not fallback_reason:
        fallback_reason = "Vector retrieval found no embedded chunks within the selected collections."

    max_context_tokens = int(retrieval_profile.get("MaxContextTokens") or 0)
    vector_rows = _trim_context_units(_dedupe_context_units(vector_rows), max_context_tokens=max_context_tokens)

    if request.includeDocuments:
        full_document_rows = list_context_units_for_collections(settings, collection_codes)
        final_rows = _dedupe_context_units([*vector_rows, *full_document_rows])
    else:
        final_rows = vector_rows

    return final_rows, {
        "retrievalMode": retrieval_mode,
        "collectionCodes": collection_codes,
        "retrievalProfile": retrieval_profile,
        "fallbackReason": fallback_reason,
    }


class CreateDomainRequest(BaseModel):
    domainCode: str
    domainTypeId: int | None = None
    domainOrientationId: int | None = None
    domainParentId: str | None = None
    displayName: str
    description: str | None = None


class CreateDomainTypeRequest(BaseModel):
    name: str
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
    parentDomainId: str | None = None


class MoveDomainRequest(BaseModel):
    domainCode: str
    newParentDomainCode: str | None = None
    newDomainTypeId: int | None = None


class ReorderDomainSiblingsRequest(BaseModel):
    parentDomainId: str | None = None
    orientationCode: str | None = None
    orderedDomainCodes: list[str]


class ReorderDomainTypesRequest(BaseModel):
    orderedTypeIds: list[int]


class DomainAssistRequest(BaseModel):
    domainCode: str
    instruction: str
    draftText: str | None = None
    model: str | None = None


class DomainChildSuggestionRequest(BaseModel):
    parentDomainCode: str | None = None
    targetDomainType: str | None = None
    instruction: str
    draftText: str | None = None
    model: str | None = None


class ExecuteDomainChildSuggestionRequest(BaseModel):
    parentDomainCode: str | None = None
    targetDomainType: str | None = None
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
    retrievalMode: str = "FullContext"
    selectedDomainCode: str | None = None
    includeDocuments: bool = True
    includeRag: bool = True
    includePolicies: bool = True
    includeDomainContext: bool = True
    includeControls: bool = True
    model: str | None = None
    history: list[dict[str, str]] = []


class ContextPreviewResponse(BaseModel):
    retrievalMode: str
    retrievalWarning: str = ""
    usedCollectionCodes: list[str] = []
    contextUnitCount: int = 0
    contextTokenCount: int = 0
    contextCharCount: int = 0
    sourceCount: int = 0
    sources: list[dict[str, object]] = []


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


class PolicyDraftContentRequest(BaseModel):
    domainCode: str
    templatePath: str = "Policy/Policy-Template-1.01.md"
    model: str | None = None
    includedControlCodes: list[str] | None = None
    controlGroups: list[dict[str, object]] | None = None


class PolicyDraftLineRetryRequest(BaseModel):
    domainCode: str
    sectionKey: str
    currentText: str
    templatePath: str = "Policy/Policy-Template-1.01.md"
    model: str | None = None
    controlCode: str | None = None
    includedControlCodes: list[str] | None = None
    controlGroups: list[dict[str, object]] | None = None


class ControlGroupingRequest(BaseModel):
    domainCode: str
    model: str | None = None
    controlCodes: list[str] | None = None


class PolicyDraftStatementItem(BaseModel):
    statementText: str
    displayOrder: int = 0
    reviewStatus: str = "Pending"


class PolicyDraftControlStatementItem(BaseModel):
    controlCode: str
    statementText: str
    displayOrder: int = 0
    reviewStatus: str = "Pending"
    groupLabel: str | None = None
    groupDisplayOrder: int = 0
    controlDisplayOrder: int = 0


class SavePolicyDraftRequest(BaseModel):
    rootDomainCode: str
    policyCode: str
    policyTitle: str
    versionText: str
    status: str
    templatePath: str | None = None
    sourceModelName: str | None = None
    objectives: list[PolicyDraftStatementItem] = []
    principles: list[PolicyDraftStatementItem] = []
    accountability: list[PolicyDraftStatementItem] = []
    transparency: list[PolicyDraftStatementItem] = []
    strategy: list[PolicyDraftStatementItem] = []
    consequences: list[PolicyDraftStatementItem] = []
    controlStatements: list[PolicyDraftControlStatementItem] = []


class PolicyControlExplanationRequest(BaseModel):
    model: str | None = None
    force: bool = False


def _build_saved_policy_draft_payload(policy_data: dict[str, object]) -> dict[str, object]:
    policy = policy_data.get("policy") or {}
    explanation_by_code = {
        str(item.get("ControlCode") or ""): str(item.get("ExplanationText") or "")
        for item in (policy_data.get("controlExplanations") or [])
        if str(item.get("ControlCode") or "").strip()
    }
    control_groups: dict[str, dict[str, object]] = {}
    for row in policy_data.get("controlStatements") or []:
        control_code = str(row.get("ControlCode") or "")
        if control_code not in control_groups:
            control_groups[control_code] = {
                "controlCode": control_code,
                "controlName": str(row.get("ControlName") or ""),
                "domainCode": str(policy.get("RootDomainCode") or ""),
                "domainDisplayName": str(policy.get("RootDomainName") or ""),
                "controlTypeCode": str(row.get("ControlTypeCode") or ""),
                "controlTypeName": str(row.get("ControlTypeName") or ""),
                "groupLabel": str(row.get("GroupLabel") or ""),
                "groupDisplayOrder": int(row.get("GroupDisplayOrder") or 0),
                "controlDisplayOrder": int(row.get("ControlDisplayOrder") or 0),
                "controlExplanation": explanation_by_code.get(control_code, ""),
                "policyStatements": [],
            }

        control_groups[control_code]["policyStatements"].append(
            {
                "statementText": str(row.get("StatementText") or ""),
                "displayOrder": int(row.get("DisplayOrder") or 0),
                "reviewStatus": str(row.get("ReviewStatus") or "Pending"),
            }
        )

    def _section_items(rows: list[dict[str, object]]) -> list[dict[str, object]]:
        return [
            {
                "statementText": str(row.get("StatementText") or ""),
                "displayOrder": int(row.get("DisplayOrder") or 0),
                "reviewStatus": str(row.get("ReviewStatus") or "Pending"),
            }
            for row in rows
        ]

    return {
        "policyId": str(policy.get("PolicyId") or ""),
        "policyCode": str(policy.get("PolicyCode") or ""),
        "documentTitle": str(policy.get("PolicyTitle") or ""),
        "versionText": str(policy.get("VersionText") or ""),
        "status": str(policy.get("Status") or ""),
        "rootDomainName": str(policy.get("RootDomainName") or ""),
        "rootDomainCode": str(policy.get("RootDomainCode") or ""),
        "rootBreadcrumb": "",
        "modelName": str(policy.get("SourceModelName") or ""),
        "objectives": _section_items(policy_data.get("objectives") or []),
        "principles": _section_items(policy_data.get("principles") or []),
        "accountability": _section_items(policy_data.get("accountability") or []),
        "transparency": _section_items(policy_data.get("transparency") or []),
        "strategy": _section_items(policy_data.get("strategy") or []),
        "controls": sorted(
            control_groups.values(),
            key=lambda item: (
                int(item.get("groupDisplayOrder") or 0),
                int(item.get("controlDisplayOrder") or 0),
                str(item.get("controlName") or ""),
            ),
        ),
        "consequences": _section_items(policy_data.get("consequences") or []),
    }


def _generate_with_ollama(
    settings,
    prompt: str,
    model: str | None = None,
    trace_label: str | None = None,
    trace_metadata: dict[str, object] | None = None,
) -> dict[str, object]:
    selected_model = model or settings.ollama_chat_model
    started_at = time.perf_counter()
    try:
        response = httpx.post(
            f"{settings.ollama_base_url}/api/generate",
            json={
                "model": selected_model,
                "prompt": prompt,
                "stream": False,
            },
            timeout=120,
        )
        response.raise_for_status()
        payload = response.json()
        _append_llm_trace(
            trace_type="generate",
            model=str(payload.get("model") or selected_model),
            prompt=prompt,
            response_text=str(payload.get("response") or ""),
            success=True,
            duration_seconds=time.perf_counter() - started_at,
            label=trace_label,
            metadata=trace_metadata,
        )
        return payload
    except Exception as exc:
        _append_llm_trace(
            trace_type="generate",
            model=selected_model,
            prompt=prompt,
            response_text="",
            success=False,
            error=str(exc),
            duration_seconds=time.perf_counter() - started_at,
            label=trace_label,
            metadata=trace_metadata,
        )
        raise


async def _stream_with_ollama(
    settings,
    prompt: str,
    model: str | None = None,
    trace_label: str | None = None,
    trace_metadata: dict[str, object] | None = None,
):
    selected_model = model or settings.ollama_chat_model
    started_at = time.perf_counter()
    response_parts: list[str] = []
    final_model = selected_model
    try:
        async with httpx.AsyncClient(timeout=120) as client:
            async with client.stream(
                "POST",
                f"{settings.ollama_base_url}/api/generate",
                json={
                    "model": selected_model,
                    "prompt": prompt,
                    "stream": True,
                },
            ) as response:
                response.raise_for_status()
                async for line in response.aiter_lines():
                    if not line:
                        continue
                    payload = json.loads(line)
                    chunk_text = str(payload.get("response") or "")
                    if chunk_text:
                        response_parts.append(chunk_text)
                    final_model = str(payload.get("model") or final_model)
                    yield payload

        _append_llm_trace(
            trace_type="stream",
            model=final_model,
            prompt=prompt,
            response_text="".join(response_parts),
            success=True,
            duration_seconds=time.perf_counter() - started_at,
            label=trace_label,
            metadata=trace_metadata,
        )
    except Exception as exc:
        _append_llm_trace(
            trace_type="stream",
            model=final_model,
            prompt=prompt,
            response_text="".join(response_parts),
            success=False,
            error=str(exc),
            duration_seconds=time.perf_counter() - started_at,
            label=trace_label,
            metadata=trace_metadata,
        )
        raise


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
        title_payload = _generate_with_ollama(
            settings,
            title_prompt,
            model=settings.ollama_title_model,
            trace_label="chat.title",
        )
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
        title_payload = _generate_with_ollama(
            settings,
            title_prompt,
            model=settings.ollama_title_model,
            trace_label="chat.title",
        )
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
        '- "domainType" must be exactly one of the allowed domain type codes shown below.\n'
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


def _build_root_domain_suggestion_prompt_parts(
    target_domain_type: str,
    instruction: str,
    draft_text: str | None,
    domain_types: list[dict[str, object]],
    all_domains: list[dict[str, object]],
) -> tuple[str, str]:
    resolved_domain_type = _resolve_domain_type(domain_types, target_domain_type)
    domain_type_code = str(resolved_domain_type.get("CODE") or "").strip().upper()
    domain_type_name = str(resolved_domain_type.get("NAME") or domain_type_code).strip()
    existing_roots = [
        f"- {item.get('DisplayName')} ({item.get('DomainType') or 'Unknown type'})"
        for item in all_domains
        if not item.get("DomainParentId")
        and str(item.get("DomainType") or "").strip().upper() == domain_type_name.upper()
    ]
    allowed_type_lines = [
        f"- {item.get('CODE')}: {item.get('NAME')}"
        for item in domain_types
        if item.get("CODE")
    ]

    system_prompt = (
        "You are helping organize a business domain map.\n\n"
        'A domain is a specific area of knowledge, activity, control, or interest, often representing a field where someone has expertise or authority, such as "the financial domain".\n\n'
        "Your task is to suggest exactly one new top-level domain under the selected domain type.\n\n"
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
        '- "domainType" must be exactly one of the allowed domain type codes shown below.\n'
        "- Do not repeat or closely duplicate an existing top-level domain.\n"
        f"- The domainType must resolve to {domain_type_code}.\n"
        "- Prefer clarity and specificity over broad or generic wording."
    )
    user_prompt = (
        f"Selected domain type name: {domain_type_name}\n"
        f"Selected domain type code: {domain_type_code}\n"
        f"Draft text:\n{draft_text or ''}\n\n"
        f"Existing top-level domains in this type:\n{chr(10).join(existing_roots) if existing_roots else 'None'}\n\n"
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
        "Use the selected domain or subdomain as the starting point for control creation. "
        "Assume the mandate establishes the department's purpose, authority, responsibilities, "
        "and expected outcomes. Create controls that define what must be managed, assigned, "
        "documented, evidenced, and reviewed through policy, procedure, and role requirements.\n\n"
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


def _build_ai_control_grouping_prompt(
    *,
    root_domain: dict[str, object],
    branch_controls: list[dict[str, object]],
) -> str:
    control_lines = [
        (
            f"- code={item.get('ControlCode') or ''}; "
            f"name={item.get('DisplayName') or ''}; "
            f"type={item.get('ControlTypeName') or ''}; "
            f"domain={item.get('DomainDisplayName') or ''}; "
            f"description={item.get('Description') or ''}"
        )
        for item in branch_controls
    ]
    return (
        "You are organizing policy controls into practical working groups for a human editor.\n\n"
        "Return only JSON in this exact structure:\n"
        "{\n"
        '  "groups": [\n'
        "    {\n"
        '      "label": "Short Group Name",\n'
        '      "controlCodes": ["code-1", "code-2"]\n'
        "    }\n"
        "  ]\n"
        "}\n\n"
        "Rules:\n"
        "- Return only JSON.\n"
        "- Do not include markdown fences.\n"
        "- Every control code must appear in exactly one group.\n"
        "- Use 2 to 6 groups when possible.\n"
        "- Group by real working similarity, not alphabetically.\n"
        "- Group labels must be short, clear, and business-friendly.\n"
        "- Do not invent control codes.\n"
        "- Do not omit controls.\n\n"
        f"Root domain: {root_domain.get('DisplayName') or ''} [{root_domain.get('DomainCode') or ''}]\n\n"
        f"Controls:\n{chr(10).join(control_lines) if control_lines else 'None'}\n"
    )


def _normalize_policy_control_groups(raw_groups: list[dict[str, object]] | None) -> list[dict[str, object]]:
    normalized: list[dict[str, object]] = []
    for item in raw_groups or []:
        if not isinstance(item, dict):
            continue
        group_label = str(item.get("groupLabel") or "").strip() or "Ungrouped Controls"
        control_codes = [
            str(code).strip().upper()
            for code in (item.get("controlCodes") or [])
            if str(code).strip()
        ]
        if not control_codes:
            continue
        normalized.append(
            {
                "groupLabel": group_label,
                "controlCodes": list(dict.fromkeys(control_codes)),
            }
        )
    return normalized


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


def _build_controls_report_html(report_rows: list[dict[str, object]]) -> str:
    domain_groups: dict[str, dict[str, object]] = {}
    directive_count = 0
    preventive_count = 0

    for row in report_rows:
        domain_id = str(row.get("DomainId") or "")
        group = domain_groups.setdefault(
            domain_id,
            {
                "domainDisplayName": row.get("DomainDisplayName") or "",
                "domainCode": row.get("DomainCode") or "",
                "domainDescription": row.get("DomainDescription") or "",
                "domainId": domain_id,
                "breadcrumb": " / ".join(
                    [
                        part
                        for part in [
                            row.get("GrandparentDisplayName"),
                            row.get("ParentDisplayName"),
                            row.get("DomainDisplayName"),
                        ]
                        if part
                    ]
                ) or str(row.get("DomainDisplayName") or ""),
                "controls": [],
            },
        )
        group["controls"].append(row)

        control_type = str(row.get("ControlTypeName") or "")
        if control_type == "Directive Control":
            directive_count += 1
        elif control_type == "Preventive Control":
            preventive_count += 1

    ordered_groups = sorted(
        domain_groups.values(),
        key=lambda item: (
            str(item.get("breadcrumb") or ""),
            str(item.get("domainDisplayName") or ""),
        ),
    )

    def esc(value: object) -> str:
        return html.escape("" if value is None else str(value))

    domain_sections: list[str] = []
    for group in ordered_groups:
        control_cards: list[str] = []
        for row in group["controls"]:
            control_cards.append(
                f"""
      <div class="control-card">
        <div class="control-top">
          <div>
            <h4>{esc(row.get("DomainControlDisplayOrder"))}. {esc(row.get("DisplayName"))}</h4>
            <div class="control-meta">
              <span class="pill">{esc(row.get("ControlTypeName"))}</span>
              <span class="pill">{esc(row.get("RelationshipType"))}</span>
              <span class="pill">{esc(row.get("Status"))}</span>
            </div>
            <p class="mono">{esc(row.get("ControlCode"))}</p>
          </div>
        </div>
        <p><span class="label">Description:</span> {esc(row.get("Description"))}</p>
        <div class="grid-2">
          <div><span class="label">Objective:</span> {esc(row.get("ControlObjective"))}</div>
          <div><span class="label">Evidence:</span> {esc(row.get("EvidenceExpectation"))}</div>
        </div>
      </div>"""
            )

        domain_sections.append(
            f"""
  <div class="domain-card">
    <div class="domain-header">
      <div>
        <h3>{esc(group.get("domainDisplayName"))}</h3>
        <div class="breadcrumb">{esc(group.get("breadcrumb"))}</div>
        <p class="muted">{esc(group.get("domainDescription"))}</p>
        <p><span class="label">Domain Code:</span> <span class="mono">{esc(group.get("domainCode"))}</span></p>
        <p><span class="label">Domain ID:</span> <span class="mono">{esc(group.get("domainId"))}</span></p>
      </div>
      <div class="pill">{len(group["controls"])} linked controls</div>
    </div>

    <div class="control-list">
      {''.join(control_cards)}
    </div>
  </div>"""
        )

    return f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Smart Controls Report</title>
  <style>
    :root {{
      --text: #111;
      --muted: #5a5a5a;
      --line: #d7d7d7;
      --panel: #f7f7f7;
      --panel-2: #fcfcfc;
    }}
    * {{ box-sizing: border-box; }}
    body {{
      margin: 24px;
      font-family: Arial, Helvetica, sans-serif;
      color: var(--text);
      background: #fff;
      line-height: 1.4;
    }}
    h1, h2, h3, h4, p {{ margin: 0 0 10px; }}
    h1 {{ font-size: 28px; }}
    h2 {{ font-size: 20px; margin-top: 28px; }}
    h3 {{ font-size: 17px; margin-top: 20px; }}
    h4 {{ font-size: 14px; margin-top: 14px; }}
    .meta, .muted {{ color: var(--muted); }}
    .summary {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 12px;
      margin: 18px 0 28px;
    }}
    .summary-card, .domain-card, .control-card {{
      border: 1px solid var(--line);
      background: var(--panel-2);
    }}
    .summary-card {{
      padding: 12px 14px;
      min-height: 92px;
    }}
    .summary-number {{
      display: block;
      font-size: 24px;
      font-weight: bold;
      margin-bottom: 4px;
    }}
    .domain-card {{
      padding: 16px;
      margin: 0 0 22px;
      background: var(--panel);
    }}
    .domain-header {{
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: flex-start;
      margin-bottom: 12px;
    }}
    .pill {{
      display: inline-block;
      border: 1px solid #bdbdbd;
      padding: 2px 8px;
      font-size: 12px;
      background: #fff;
      white-space: nowrap;
    }}
    .breadcrumb {{
      font-size: 12px;
      color: var(--muted);
      margin-top: -4px;
      margin-bottom: 8px;
    }}
    .control-list {{
      display: grid;
      gap: 12px;
      margin-top: 12px;
    }}
    .control-card {{
      padding: 14px;
      background: #fff;
    }}
    .control-top {{
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: flex-start;
      margin-bottom: 8px;
    }}
    .control-meta {{
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin: 8px 0 10px;
    }}
    .label {{
      font-weight: bold;
    }}
    .mono {{
      font-family: Consolas, "Courier New", monospace;
      font-size: 12px;
    }}
    .grid-2 {{
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin-top: 10px;
    }}
    @media (max-width: 900px) {{
      .grid-2 {{ grid-template-columns: 1fr; }}
      .domain-header, .control-top {{ display: block; }}
    }}
  </style>
</head>
<body>
  <h1>Smart Controls Report</h1>
  <p class="meta">Live view from <span class="mono">DomainLinks</span>, grouped by domain and ordered by domain control display order.</p>

  <div class="summary">
    <div class="summary-card">
      <span class="summary-number">{len(ordered_groups)}</span>
      <div>Domains with controls</div>
    </div>
    <div class="summary-card">
      <span class="summary-number">{len(report_rows)}</span>
      <div>Total controls linked to domains</div>
    </div>
    <div class="summary-card">
      <span class="summary-number">{directive_count}</span>
      <div>Directive controls</div>
    </div>
    <div class="summary-card">
      <span class="summary-number">{preventive_count}</span>
      <div>Preventive controls</div>
    </div>
  </div>

  <h2>Domain Groups</h2>
  <p class="meta">Each domain shows its hierarchy as a breadcrumb and then its underlying controls.</p>
  {''.join(domain_sections)}
</body>
</html>"""


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _resolve_repo_relative_path(repo_relative_path: str) -> Path:
    candidate = (_repo_root() / repo_relative_path).resolve()
    repo_root = _repo_root().resolve()
    if repo_root not in candidate.parents and candidate != repo_root:
        raise ValueError("Template path must stay within the repository.")
    if not candidate.is_file():
        raise ValueError(f"Template file was not found: {repo_relative_path}")
    return candidate


def _domain_breadcrumb(domain: dict[str, object], all_domains: list[dict[str, object]]) -> str:
    lookup = {
        str(item.get("DomainId") or ""): item
        for item in all_domains
        if item.get("DomainId")
    }
    parts = [str(domain.get("DisplayName") or domain.get("DomainCode") or "")]
    parent_id = str(domain.get("DomainParentId") or "").strip()
    while parent_id and parent_id in lookup:
        parent = lookup[parent_id]
        parts.insert(0, str(parent.get("DisplayName") or parent.get("DomainCode") or ""))
        parent_id = str(parent.get("DomainParentId") or "").strip()
    return " / ".join(part for part in parts if part)


def _build_policy_generation_prompt(
    template_text: str,
    root_domain: dict[str, object],
    branch_domains: list[dict[str, object]],
    existing_controls: list[dict[str, object]],
    all_domains: list[dict[str, object]],
) -> tuple[str, str]:
    root_breadcrumb = _domain_breadcrumb(root_domain, all_domains)
    branch_lines = [
        f"- {_domain_breadcrumb(domain, all_domains)} | code={domain.get('DomainCode')} | type={domain.get('DomainType') or 'Unknown'} | description={domain.get('Description') or ''}"
        for domain in branch_domains
    ]
    control_lines = [
        f"- domain={item.get('DomainDisplayName')} | type={item.get('ControlTypeName')} ({item.get('ControlTypeCode')}) | name={item.get('DisplayName')} | code={item.get('ControlCode')} | description={item.get('Description') or ''} | objective={item.get('ControlObjective') or ''} | evidence={item.get('EvidenceExpectation') or ''}"
        for item in existing_controls
    ]

    system_prompt = (
        "You are drafting an internal policy document from structured local organizational data.\n\n"
        "Rules:\n"
        "- Use only the supplied domain, child-domain, and control data.\n"
        "- Do not invent external laws, standards, frameworks, departments, committees, or references.\n"
        "- Use clear professional language for workplace readers rather than legal drafting style.\n"
        "- Prefer direct sentence structure and practical wording that informed staff can grasp quickly.\n"
        "- Avoid legal jargon, archaic wording, and unnecessarily dense phrasing unless a precise term is required.\n"
        "- Keep the draft sound, accurate, and defensible while making it easier for non-lawyers to follow.\n"
        "- The output is a completed policy draft, not a template.\n"
        "- Preserve the template structure and headings exactly as given.\n"
        "- Replace the template title with a final policy title based on the selected root domain.\n"
        "- Never leave the words 'Template' or 'Project Management' in the document title unless the selected root domain actually requires them.\n"
        "- Use the title '<Root Domain Display Name> Policy'.\n"
        "- Replace template instructions and placeholders with policy-ready drafting.\n"
        "- If a factual detail is missing, keep a clear placeholder instead of inventing it.\n"
        "- Derive principles, policy statements, consequences, definitions, forms, and references from the supplied branch data only.\n"
        "- Write 1 to 2 objective statements.\n"
        "- Write 3 to 4 principles.\n"
        "- A principle is a guiding belief, value, or decision standard. A principle should describe what guides choices or behavior, not what someone is required to do.\n"
        "- Do not write principles as commands, enforcement statements, or wording such as 'shall', 'must', or 'will'.\n"
        "- Write exactly 1 statement for Accountability and 1 statement for Transparency.\n"
        "- Write 1 to 2 strategy statements.\n"
        "- Under section 3.0 Policy, create a control-specific subsection for every control item if needed to fit full coverage cleanly.\n"
        "- Use the control display name as the subsection heading under 3.0 Policy.\n"
        "- Write 1 to 3 policy statements per control item.\n"
        "- Never leave out a control.\n"
        "- Keep controls in display order.\n"
        "- Write 1 to 2 consequence statements.\n"
        "- Use markdown only.\n"
        "- Return only the completed policy draft."
    )
    user_prompt = (
        f"Template:\n{template_text}\n\n"
        f"Root domain:\n"
        f"- name={root_domain.get('DisplayName')}\n"
        f"- code={root_domain.get('DomainCode')}\n"
        f"- breadcrumb={root_breadcrumb}\n"
        f"- description={root_domain.get('Description') or ''}\n\n"
        f"Branch domains:\n{chr(10).join(branch_lines) if branch_lines else 'None'}\n\n"
        f"Branch controls:\n{chr(10).join(control_lines) if control_lines else 'None'}\n\n"
        "Draft a complete policy using this material.\n"
        "Coverage requirements:\n"
        "- Every listed control must appear in the policy section.\n"
        "- Section 3.0 must include one control subsection per listed control when the template does not provide enough direct numbered policy lines.\n"
        "- For a branch with 10 controls, the policy output must clearly cover all 10 controls.\n"
        "- Keep objectives, principles, accountability/transparency, strategy, and consequences within the required ranges.\n"
        "- If the template numbering offers more lines than the required ranges, consolidate the drafting while preserving the template headings."
    )
    return system_prompt, user_prompt


def _build_policy_content_prompt(
    template_text: str,
    root_domain: dict[str, object],
    branch_domains: list[dict[str, object]],
    branch_controls: list[dict[str, object]],
    all_domains: list[dict[str, object]],
    control_groups: list[dict[str, object]] | None = None,
) -> tuple[str, str]:
    root_breadcrumb = _domain_breadcrumb(root_domain, all_domains)
    branch_lines = [
        f"- {_domain_breadcrumb(domain, all_domains)} | code={domain.get('DomainCode')} | type={domain.get('DomainType') or 'Unknown'} | description={domain.get('Description') or ''}"
        for domain in branch_domains
    ]
    control_lines = [
        f"- controlCode={item.get('ControlCode')} | controlName={item.get('DisplayName')} | domain={item.get('DomainDisplayName')} ({item.get('DomainCode')}) | type={item.get('ControlTypeName')} ({item.get('ControlTypeCode')}) | description={item.get('Description') or ''} | objective={item.get('ControlObjective') or ''} | evidence={item.get('EvidenceExpectation') or ''}"
        for item in branch_controls
    ]
    grouped_control_lines = []
    control_lookup = {
        str(item.get("ControlCode") or "").strip().upper(): item
        for item in branch_controls
        if str(item.get("ControlCode") or "").strip()
    }
    for group in control_groups or []:
        codes = [
            str(code).strip().upper()
            for code in (group.get("controlCodes") or [])
            if str(code).strip()
        ]
        grouped_controls = [
            control_lookup[code]
            for code in codes
            if code in control_lookup
        ]
        if not grouped_controls:
            continue
        grouped_control_lines.append(
            f"- {group.get('groupLabel') or 'Ungrouped Controls'}:\n"
            + "\n".join(
                "  "
                + f"* controlCode={item.get('ControlCode')} | controlName={item.get('DisplayName')} | type={item.get('ControlTypeName')} ({item.get('ControlTypeCode')}) | domain={item.get('DomainDisplayName')} | objective={item.get('ControlObjective') or ''}"
                for item in grouped_controls
            )
        )

    system_prompt = (
        "You are drafting the content sections for an internal policy from structured local organizational data.\n\n"
        "Return only valid JSON. Do not wrap it in markdown fences.\n"
        "Use only the supplied domain, branch, control, and template data.\n"
        "Do not invent external laws, standards, frameworks, departments, committees, or references.\n"
        "Treat the template as structural guidance only.\n"
        "Write concise, policy-ready sentences.\n"
        "Use clear professional language for workplace readers rather than legal drafting style.\n"
        "Prefer direct sentence structure and practical wording that informed staff can grasp quickly.\n"
        "Avoid legal jargon, archaic wording, and unnecessarily dense phrasing unless a precise term is required.\n"
        "Keep the content sound, accurate, and defensible while making it easier for non-lawyers to follow.\n"
        "Section definitions:\n"
        "- Objectives: short outcome statements describing what this policy is meant to achieve.\n"
        "- Principles: enduring beliefs, values, or decision standards that guide judgment and behavior. Principles are not requirements, directives, or enforcement statements.\n"
        "- Accountability: a policy statement that assigns ownership or responsibility.\n"
        "- Transparency: a policy statement that expresses visibility, disclosure, traceability, or openness expectations.\n"
        "- Strategy: a policy statement describing the organization's practical approach for carrying out this policy area.\n"
        "- Control policy statements: policy requirements aligned to each control's purpose.\n"
        "- Consequences: policy statements describing outcomes of noncompliance or failure.\n"
        "Principles must read like beliefs or standards, not commands.\n"
        "Do not use directive wording such as 'shall', 'must', 'will', 'required', or 'responsible for' in principles.\n"
        "Do not write principles as obligations, prohibitions, procedures, or tasks.\n"
        "A principle should sound like something that guides choices, such as a value, orientation, or standard of judgment.\n"
        "Accountability and transparency are policy statements, so directive wording is allowed there.\n"
        "Strategy should reinforce how the organization approaches the domain in practical terms.\n"
        "Every control must appear exactly once in the controls array.\n"
        "Keep controls in the supplied order.\n"
        "Use the supplied control groups as drafting context so related controls read cohesively.\n"
        "Group labels help shape tone and consistency, but every control still needs its own policy statements.\n"
        "Write 1 to 2 objective statements.\n"
        "Write 3 to 4 principles.\n"
        "Write exactly 1 accountability statement.\n"
        "Write exactly 1 transparency statement.\n"
        "Write 1 to 2 strategy statements.\n"
        "Write 1 to 3 policy statements per control.\n"
        "Write 1 to 2 consequence statements.\n"
        "Use this exact JSON shape:\n"
        "{\n"
        '  "documentTitle": "Root Domain Policy",\n'
        '  "objectives": ["..."],\n'
        '  "principles": ["..."],\n'
        '  "accountability": ["..."],\n'
        '  "transparency": ["..."],\n'
        '  "strategy": ["..."],\n'
        '  "controls": [\n'
        "    {\n"
        '      "controlCode": "control-code",\n'
        '      "policyStatements": ["..."]\n'
        "    }\n"
        "  ],\n"
        '  "consequences": ["..."]\n'
        "}\n"
    )
    user_prompt = (
        f"Template:\n{template_text}\n\n"
        f"Root domain:\n"
        f"- name={root_domain.get('DisplayName')}\n"
        f"- code={root_domain.get('DomainCode')}\n"
        f"- breadcrumb={root_breadcrumb}\n"
        f"- description={root_domain.get('Description') or ''}\n\n"
        f"Branch domains:\n{chr(10).join(branch_lines) if branch_lines else 'None'}\n\n"
        f"Branch controls in required order:\n{chr(10).join(control_lines) if control_lines else 'None'}\n\n"
        f"Control groups:\n{chr(10).join(grouped_control_lines) if grouped_control_lines else 'None'}\n\n"
        "Draft only the policy content sections in the requested JSON format."
    )
    return system_prompt, user_prompt


def _build_policy_line_retry_prompt(
    *,
    template_text: str,
    root_domain: dict[str, object],
    branch_domains: list[dict[str, object]],
    branch_controls: list[dict[str, object]],
    all_domains: list[dict[str, object]],
    control_groups: list[dict[str, object]] | None,
    section_key: str,
    current_text: str,
    control_code: str | None,
) -> str:
    root_breadcrumb = _domain_breadcrumb(root_domain, all_domains)
    target_control = next(
        (
            item
            for item in branch_controls
            if str(item.get("ControlCode") or "").strip().lower() == str(control_code or "").strip().lower()
        ),
        None,
    )

    section_rules = {
        "objective": "Write one concise objective statement for the policy. Keep it outcome-oriented.",
        "principle": "Write one concise principle. A principle is a guiding belief, value, or decision standard. It is not a requirement or instruction. Do not use 'shall', 'must', 'will', 'required', or 'responsible for'.",
        "accountability": "Write one concise accountability statement. It should clearly express ownership or responsibility.",
        "transparency": "Write one concise transparency statement. It should clearly express visibility, disclosure, or traceability expectations.",
        "strategy": "Write one concise strategy statement. It should reinforce the organizational approach for this domain.",
        "consequence": "Write one concise consequence statement tied to noncompliance or failure to follow the policy.",
        "control-policy": "Write one concise policy statement for the specified control. It should align to the control's purpose and sound like policy language.",
    }
    rule_text = section_rules.get(section_key, "Write one concise replacement policy line.")

    branch_lines = [
        f"- {_domain_breadcrumb(domain, all_domains)} | code={domain.get('DomainCode')} | type={domain.get('DomainType') or 'Unknown'}"
        for domain in branch_domains
    ]
    target_group_label = "Ungrouped Controls"
    if control_code:
        target_control_code = str(control_code).strip().upper()
        for group in control_groups or []:
            group_codes = {
                str(code).strip().upper()
                for code in (group.get("controlCodes") or [])
                if str(code).strip()
            }
            if target_control_code in group_codes:
                target_group_label = str(group.get("groupLabel") or "").strip() or target_group_label
                break
    control_context = "None"
    if target_control:
        control_context = (
            f"controlCode={target_control.get('ControlCode')} | "
            f"controlName={target_control.get('DisplayName')} | "
            f"domain={target_control.get('DomainDisplayName')} ({target_control.get('DomainCode')}) | "
            f"type={target_control.get('ControlTypeName')} ({target_control.get('ControlTypeCode')}) | "
            f"description={target_control.get('Description') or ''} | "
            f"objective={target_control.get('ControlObjective') or ''} | "
            f"evidence={target_control.get('EvidenceExpectation') or ''} | "
            f"group={target_group_label}"
        )

    return (
        "You are revising one line in an internal policy draft.\n\n"
        "Return only the replacement line as plain text.\n"
        "Do not number it. Do not add bullets. Do not add quotation marks.\n"
        "Use only the supplied local domain, branch, control, and template context.\n"
        "Do not invent external laws, standards, frameworks, departments, or references.\n\n"
        "Use clear professional language for workplace readers rather than legal drafting style.\n"
        "Prefer direct sentence structure and practical wording that informed staff can grasp quickly.\n"
        "Avoid legal jargon, archaic wording, and unnecessarily dense phrasing unless a precise term is required.\n"
        "Keep the line sound, accurate, and defensible while making it easier for non-lawyers to follow.\n\n"
        "Section definitions:\n"
        "- Objectives: short outcome statements describing what this policy is meant to achieve.\n"
        "- Principles: enduring beliefs, values, or decision standards that guide judgment and behavior. Principles are not requirements, directives, or enforcement statements.\n"
        "- Accountability: a policy statement that assigns ownership or responsibility.\n"
        "- Transparency: a policy statement that expresses visibility, disclosure, traceability, or openness expectations.\n"
        "- Strategy: a policy statement describing the organization's practical approach for carrying out this policy area.\n"
        "- Control policy statements: policy requirements aligned to each control's purpose.\n"
        "- Consequences: policy statements describing outcomes of noncompliance or failure.\n"
        "Principles must read like beliefs or standards, not commands.\n"
        "Do not use directive wording such as 'shall', 'must', 'will', 'required', or 'responsible for' in principles.\n"
        "Do not write principles as obligations, prohibitions, procedures, or tasks.\n"
        "A principle should sound like something that guides choices, such as a value, orientation, or standard of judgment.\n\n"
        f"Rule for this line:\n{rule_text}\n\n"
        f"Template reference:\n{template_text}\n\n"
        f"Root domain:\n"
        f"- name={root_domain.get('DisplayName')}\n"
        f"- code={root_domain.get('DomainCode')}\n"
        f"- breadcrumb={root_breadcrumb}\n"
        f"- description={root_domain.get('Description') or ''}\n\n"
        f"Branch domains:\n{chr(10).join(branch_lines) if branch_lines else 'None'}\n\n"
        f"Target control context:\n{control_context}\n\n"
        f"Current line to replace:\n{current_text.strip()}\n\n"
        "Write a fresh replacement line now."
    )


def _coerce_text_list(value: object, *, minimum: int = 0, maximum: int | None = None) -> list[str]:
    items: list[str] = []
    if isinstance(value, list):
        for item in value:
            text = str(item or "").strip()
            if text:
                items.append(text)
    if maximum is not None:
        items = items[:maximum]
    if minimum > 0 and len(items) < minimum:
        items.extend(["[Draft needed]"] * (minimum - len(items)))
    return items


def _normalize_policy_content_draft(
    parsed: dict[str, object],
    root_domain: dict[str, object],
    branch_controls: list[dict[str, object]],
    model_name: str,
    all_domains: list[dict[str, object]],
    control_groups: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    controls_by_code = {
        str(item.get("ControlCode") or "").strip().lower(): item
        for item in branch_controls
        if item.get("ControlCode")
    }
    parsed_controls = parsed.get("controls")
    parsed_control_lookup: dict[str, dict[str, object]] = {}
    if isinstance(parsed_controls, list):
        for item in parsed_controls:
            if not isinstance(item, dict):
                continue
            control_code = str(item.get("controlCode") or "").strip().lower()
            if control_code:
                parsed_control_lookup[control_code] = item

    group_label_by_control_code: dict[str, str] = {}
    for group in control_groups or []:
        group_label = str(group.get("groupLabel") or "").strip() or "Ungrouped Controls"
        for code in group.get("controlCodes") or []:
            normalized_code = str(code).strip().lower()
            if normalized_code:
                group_label_by_control_code[normalized_code] = group_label

    ordered_controls: list[dict[str, object]] = []
    if control_groups:
        seen_codes: set[str] = set()
        for group_index, group in enumerate(control_groups, start=1):
            for control_index, code in enumerate(group.get("controlCodes") or [], start=1):
                lookup_key = str(code).strip().lower()
                control = controls_by_code.get(lookup_key)
                if control is None or lookup_key in seen_codes:
                    continue
                seen_codes.add(lookup_key)
                ordered_controls.append(
                    {
                        "control": control,
                        "groupDisplayOrder": group_index * 10,
                        "controlDisplayOrder": control_index * 10,
                    }
                )

        for control in branch_controls:
            control_code_key = str(control.get("ControlCode") or "").strip().lower()
            if control_code_key and control_code_key not in seen_codes:
                ordered_controls.append(
                    {
                        "control": control,
                        "groupDisplayOrder": 9990,
                        "controlDisplayOrder": (len(ordered_controls) + 1) * 10,
                    }
                )
    else:
        for control_index, control in enumerate(branch_controls, start=1):
            ordered_controls.append(
                {
                    "control": control,
                    "groupDisplayOrder": 0,
                    "controlDisplayOrder": control_index * 10,
                }
            )

    normalized_controls: list[dict[str, object]] = []
    for ordered_control in ordered_controls:
        control = ordered_control["control"]
        control_code = str(control.get("ControlCode") or "").strip()
        parsed_item = parsed_control_lookup.get(control_code.lower(), {})
        normalized_controls.append(
            {
                "controlCode": control_code,
                "controlName": str(control.get("DisplayName") or ""),
                "domainCode": str(control.get("DomainCode") or ""),
                "domainDisplayName": str(control.get("DomainDisplayName") or ""),
                "controlTypeCode": str(control.get("ControlTypeCode") or ""),
                "controlTypeName": str(control.get("ControlTypeName") or ""),
                "groupLabel": group_label_by_control_code.get(control_code.lower(), ""),
                "groupDisplayOrder": int(ordered_control["groupDisplayOrder"] or 0),
                "controlDisplayOrder": int(ordered_control["controlDisplayOrder"] or 0),
                "policyStatements": _coerce_text_list(parsed_item.get("policyStatements"), minimum=1, maximum=3),
            }
        )

    root_domain_name = str(root_domain.get("DisplayName") or root_domain.get("DomainCode") or "Policy")
    return {
        "documentTitle": str(parsed.get("documentTitle") or f"{root_domain_name} Policy").strip() or f"{root_domain_name} Policy",
        "rootDomainName": root_domain_name,
        "rootDomainCode": str(root_domain.get("DomainCode") or ""),
        "rootBreadcrumb": _domain_breadcrumb(root_domain, all_domains),
        "modelName": model_name,
        "objectives": _coerce_text_list(parsed.get("objectives"), minimum=1, maximum=2),
        "principles": _coerce_text_list(parsed.get("principles"), minimum=3, maximum=4),
        "accountability": _coerce_text_list(parsed.get("accountability"), minimum=1, maximum=1),
        "transparency": _coerce_text_list(parsed.get("transparency"), minimum=1, maximum=1),
        "strategy": _coerce_text_list(parsed.get("strategy"), minimum=1, maximum=2),
        "controls": normalized_controls,
        "consequences": _coerce_text_list(parsed.get("consequences"), minimum=1, maximum=2),
    }


def _finalize_policy_markdown(root_domain_name: str, markdown_text: str) -> str:
    final_title = f"# {root_domain_name} Policy"
    lines = markdown_text.splitlines()
    if not lines:
        return final_title

    for index, line in enumerate(lines):
        if line.strip().startswith("# "):
            lines[index] = final_title
            return "\n".join(lines)

    return final_title + "\n\n" + markdown_text


def _render_simple_markdown_to_html(markdown_text: str) -> str:
    lines = markdown_text.splitlines()
    html_parts: list[str] = []
    in_list = False

    def close_list() -> None:
        nonlocal in_list
        if in_list:
            html_parts.append("</ul>")
            in_list = False

    def inline_format(text: str) -> str:
        escaped = html.escape(text)
        escaped = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", escaped)
        return escaped

    for raw_line in lines:
        line = raw_line.rstrip()
        stripped = line.strip()
        if not stripped:
            close_list()
            continue
        if stripped.startswith("# "):
            close_list()
            html_parts.append(f"<h1>{inline_format(stripped[2:])}</h1>")
            continue
        if stripped.startswith("## "):
            close_list()
            html_parts.append(f"<h2>{inline_format(stripped[3:])}</h2>")
            continue
        if stripped.startswith("### "):
            close_list()
            html_parts.append(f"<h3>{inline_format(stripped[4:])}</h3>")
            continue
        if stripped.startswith("- "):
            if not in_list:
                html_parts.append("<ul>")
                in_list = True
            html_parts.append(f"<li>{inline_format(stripped[2:])}</li>")
            continue
        close_list()
        html_parts.append(f"<p>{inline_format(stripped)}</p>")

    close_list()
    return "\n".join(html_parts)


def _build_policy_browser_html(
    *,
    domain_code: str,
    template_path: str,
    model_name: str,
    body_html: str,
    root_domain_name: str,
    domain_options: list[dict[str, str]],
) -> str:
    domain_options_html = "\n".join(
        f'<option value="{html.escape(item["value"])}"{" selected" if item["selected"] else ""}>{html.escape(item["label"])}</option>'
        for item in domain_options
    )
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Policy Draft Preview</title>
  <style>
    body {{ font-family: Arial, Helvetica, sans-serif; margin: 24px; color: #111; background: #fff; }}
    .meta {{ color: #5a5a5a; margin-bottom: 18px; }}
    .shell {{ max-width: 1100px; margin: 0 auto; }}
    .toolbar {{ border: 1px solid #d7d7d7; background: #f7f7f7; padding: 14px; margin-bottom: 20px; }}
    label {{ display: block; font-size: 12px; font-weight: bold; margin-bottom: 4px; }}
    input {{ width: 100%; padding: 8px; border: 1px solid #c8c8c8; }}
    .row {{ display: grid; grid-template-columns: 1fr 1fr 1fr auto; gap: 12px; align-items: end; }}
    button {{ padding: 9px 14px; border: 1px solid #18344a; background: #18344a; color: #fff; cursor: pointer; }}
    .doc {{ border: 1px solid #ddd; background: #fff; padding: 32px; }}
    h1 {{ font-size: 28px; }}
    h2 {{ font-size: 20px; margin-top: 24px; }}
    h3 {{ font-size: 16px; margin-top: 18px; }}
    p, li {{ line-height: 1.5; }}
    ul {{ margin-top: 0; }}
  </style>
</head>
<body>
  <div class="shell">
    <h1>Policy Draft Preview</h1>
    <p class="meta">Generated from the selected root domain, its branch domains, and its controls using the local model.</p>
    <form class="toolbar" method="get" action="/reports/policy-draft">
      <div class="row">
        <div>
          <label for="domainCode">Domain Code</label>
          <select id="domainCode" name="domainCode" style="width: 100%; padding: 8px; border: 1px solid #c8c8c8;">
            {domain_options_html}
          </select>
        </div>
        <div>
          <label for="templatePath">Template Path</label>
          <input id="templatePath" name="templatePath" value="{html.escape(template_path)}" />
        </div>
        <div>
          <label for="model">Model</label>
          <input id="model" name="model" value="{html.escape(model_name)}" />
        </div>
        <div>
          <button type="submit">Generate</button>
        </div>
      </div>
    </form>
    <div class="meta">Direct draft URL: <code>/reports/policy-draft?domainCode={html.escape(domain_code)}</code></div>
    <div class="meta">Root domain: <strong>{html.escape(root_domain_name)}</strong></div>
    <div class="meta">Model used: <strong>{html.escape(model_name)}</strong></div>
    <div class="doc">
      {body_html}
    </div>
  </div>
</body>
</html>"""


def _render_policy_line_items(rows: list[dict[str, object]]) -> str:
    if not rows:
        return "<p class=\"empty\">No content saved for this section.</p>"

    items: list[str] = []
    for row in rows:
        statement_text = html.escape(str(row.get("StatementText") or ""))
        review_status = str(row.get("ReviewStatus") or "").strip()
        meta_html = f"<div class=\"line-meta\">{html.escape(review_status)}</div>" if review_status else ""
        items.append(f"<li>{meta_html}<div>{statement_text}</div></li>")
    return f"<ol>{''.join(items)}</ol>"


def _build_saved_policy_html(policy_data: dict[str, object]) -> str:
    policy = policy_data.get("policy") or {}
    policy_title = html.escape(str(policy.get("PolicyTitle") or "Policy"))
    root_domain_name = html.escape(str(policy.get("RootDomainName") or ""))
    root_domain_code = html.escape(str(policy.get("RootDomainCode") or ""))
    policy_code = html.escape(str(policy.get("PolicyCode") or ""))
    version_text = html.escape(str(policy.get("VersionText") or ""))
    status = html.escape(str(policy.get("Status") or ""))
    template_name = html.escape(str(policy.get("TemplateName") or "") or "None")
    model_name = html.escape(str(policy.get("SourceModelName") or "") or "Unknown")
    updated_at = html.escape(str(policy.get("UpdatedAtUtc") or policy.get("CreatedAtUtc") or ""))
    policy_id = html.escape(str(policy.get("PolicyId") or ""))
    explanation_by_code = {
        str(item.get("ControlCode") or ""): str(item.get("ExplanationText") or "")
        for item in (policy_data.get("controlExplanations") or [])
        if str(item.get("ControlCode") or "").strip()
    }

    control_rows = policy_data.get("controlStatements") or []
    grouped_controls: dict[str, dict[str, object]] = {}
    for row in control_rows:
        group_label = str(row.get("GroupLabel") or "").strip() or "Ungrouped Controls"
        control_code = str(row.get("ControlCode") or "")
        group_key = f"{int(row.get('GroupDisplayOrder') or 0):08d}|{group_label.lower()}"
        control_key = f"{group_key}|{int(row.get('ControlDisplayOrder') or 0):08d}|{control_code.lower()}"
        if control_key not in grouped_controls:
            grouped_controls[control_key] = {
                "GroupLabel": group_label,
                "GroupDisplayOrder": int(row.get("GroupDisplayOrder") or 0),
                "ControlCode": control_code,
                "ControlName": str(row.get("ControlName") or ""),
                "ControlTypeName": str(row.get("ControlTypeName") or ""),
                "ControlTypeCode": str(row.get("ControlTypeCode") or ""),
                "ControlDisplayOrder": int(row.get("ControlDisplayOrder") or 0),
                "Rows": [],
            }
        grouped_controls[control_key]["Rows"].append(row)

    control_html_parts: list[str] = []
    if grouped_controls:
        controls_by_group: dict[str, list[dict[str, object]]] = {}
        for control in grouped_controls.values():
            group_key = f"{int(control.get('GroupDisplayOrder') or 0):08d}|{str(control.get('GroupLabel') or '').lower()}"
            controls_by_group.setdefault(group_key, []).append(control)

        for controls in controls_by_group.values():
            if not controls:
                continue

            group_label = html.escape(str(controls[0].get("GroupLabel") or "Ungrouped Controls"))
            control_cards: list[str] = []
            for group in sorted(
                controls,
                key=lambda item: (
                    int(item.get("ControlDisplayOrder") or 0),
                    str(item.get("ControlName") or ""),
                ),
            ):
                header = html.escape(str(group.get("ControlName") or "Control"))
                control_code = str(group.get("ControlCode") or "")
                detail = html.escape(
                    f"{group.get('ControlTypeName') or ''} ({group.get('ControlTypeCode') or ''}) | {control_code}"
                )
                explanation = html.escape(explanation_by_code.get(control_code, ""))
                has_explanation = bool(explanation.strip())
                control_cards.append(
                    f"""
                    <section class="control-card">
                      <div class="control-title-row">
                        <h3>{header}</h3>
                        <button class="info-button {'has-info' if has_explanation else 'no-info'}"
                                type="button"
                                data-policy-id="{policy_id}"
                                data-control-code="{html.escape(control_code)}"
                                onclick="toggleControlExplanation(this)"
                                oncontextmenu="showExplanationMenu(event, this)">{'i' if has_explanation else '+'}</button>
                      </div>
                      <div class="control-meta">{detail}</div>
                      <div class="control-explanation"
                           data-control-code="{html.escape(control_code)}">{explanation}</div>
                      {_render_policy_line_items(group.get("Rows") or [])}
                    </section>
                    """
                )

            control_html_parts.append(
                f"""
                <section class="control-group-card">
                  <h3 class="control-group-title">{group_label}</h3>
                  {''.join(control_cards)}
                </section>
                """
            )
    else:
        control_html_parts.append("<p class=\"empty\">No policy statements by control were saved.</p>")

    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{policy_title}</title>
  <style>
    body {{
      margin: 0;
      background: #f3f1ec;
      color: #263746;
      font-family: Segoe UI, Arial, sans-serif;
    }}
    .page {{
      max-width: 1080px;
      margin: 0 auto;
      padding: 18px;
    }}
    .hero {{
      background: #18344A;
      color: #f7f3ea;
      border-radius: 8px;
      padding: 18px 20px;
    }}
    .hero h1 {{
      margin: 0;
      font-size: 28px;
    }}
    .hero .sub {{
      margin-top: 6px;
      font-size: 13px;
      color: #d7e2ea;
    }}
    .meta-grid {{
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 10px;
      margin-top: 14px;
    }}
    .meta-card, .section-card, .control-card, .control-group-card {{
      background: #ffffff;
      border: 1px solid #d6d0c4;
      border-radius: 6px;
    }}
    .meta-card {{
      padding: 12px;
    }}
    .meta-label {{
      font-size: 11px;
      color: #5b6770;
      margin-bottom: 4px;
    }}
    .meta-value {{
      font-size: 14px;
      font-weight: 600;
    }}
    .section-card, .control-card, .control-group-card {{
      padding: 14px 16px;
      margin-top: 14px;
    }}
    h2 {{
      margin: 0 0 10px;
      font-size: 20px;
    }}
    h3 {{
      margin: 0;
      font-size: 17px;
    }}
    .control-group-card {{
      background: #fbf9f4;
      border-color: #ddd4c6;
    }}
    .control-group-title {{
      margin: 0 0 10px;
      font-size: 14px;
      color: #7a5c8e;
    }}
    .control-group-card .control-card {{
      margin-top: 10px;
      background: #ffffff;
    }}
    .control-title-row {{
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 10px;
    }}
    .control-meta {{
      margin-top: 4px;
      margin-bottom: 10px;
      color: #5b6770;
      font-size: 12px;
    }}
    .info-button {{
      width: 26px;
      height: 26px;
      border-radius: 999px;
      border: 1px solid #d6d0c4;
      background: #f8f6f1;
      color: #315b73;
      font-weight: 700;
      cursor: pointer;
    }}
    .info-button.has-info {{
      background: #e9f3f8;
      border-color: #9fb8c7;
      color: #204b62;
    }}
    .info-button.no-info {{
      background: #f8f6f1;
      border-style: dashed;
      color: #7b8790;
    }}
    .control-explanation {{
      display: none;
    }}
    .control-card {{
      position: relative;
    }}
    .control-title-row {{
      position: relative;
    }}
    .info-bubble {{
      position: absolute;
      z-index: 1100;
      display: none;
      width: min(320px, calc(100vw - 64px));
      background: #e2f0f8;
      border: 2px solid #24465c;
      border-radius: 18px;
      box-shadow: 0 16px 28px rgba(24, 52, 74, 0.18);
      overflow: visible;
    }}
    .info-bubble.is-visible {{
      display: block;
    }}
    .info-bubble::after {{
      content: "";
      position: absolute;
      right: 26px;
      top: -9px;
      width: 16px;
      height: 16px;
      background: #e2f0f8;
      border-top: 2px solid #24465c;
      border-left: 2px solid #24465c;
      transform: rotate(45deg);
    }}
    .info-bubble.above::after {{
      top: auto;
      bottom: -9px;
      border-top: 0;
      border-left: 0;
      border-right: 2px solid #24465c;
      border-bottom: 2px solid #24465c;
    }}
    .info-bubble-header {{
      display: flex;
      justify-content: flex-end;
      padding: 8px 10px 0;
    }}
    .info-bubble-close {{
      border: 0;
      background: transparent;
      color: #446175;
      font-size: 22px;
      line-height: 1;
      cursor: pointer;
      width: 24px;
      height: 24px;
      padding: 0;
    }}
    .info-bubble-body {{
      padding: 0 16px 16px;
      color: #263746;
      font-size: 13px;
      line-height: 1.5;
      white-space: pre-wrap;
    }}
    .info-bubble-status {{
      display: none;
      align-items: center;
      gap: 8px;
      padding: 0 16px 10px;
      color: #446175;
      font-size: 12px;
      font-weight: 600;
    }}
    .info-bubble-status.is-visible {{
      display: flex;
    }}
    .info-bubble-spinner {{
      width: 14px;
      height: 14px;
      border: 2px solid rgba(68, 97, 117, 0.25);
      border-top-color: #446175;
      border-radius: 999px;
      animation: info-bubble-spin 0.85s linear infinite;
    }}
    .info-button.is-loading {{
      background: #dbeaf3;
      color: #446175;
      cursor: wait;
      opacity: 0.9;
    }}
    @keyframes info-bubble-spin {{
      from {{ transform: rotate(0deg); }}
      to {{ transform: rotate(360deg); }}
    }}
    .explanation-menu {{
      position: fixed;
      z-index: 1000;
      display: none;
      min-width: 180px;
      background: #ffffff;
      border: 1px solid #d6d0c4;
      border-radius: 6px;
      box-shadow: 0 10px 24px rgba(24, 52, 74, 0.16);
      overflow: hidden;
    }}
    .explanation-menu button {{
      width: 100%;
      border: 0;
      background: #ffffff;
      text-align: left;
      padding: 10px 12px;
      cursor: pointer;
      color: #263746;
    }}
    .explanation-menu button:hover {{
      background: #f3f1ec;
    }}
    ol {{
      margin: 0;
      padding-left: 22px;
    }}
    li {{
      margin: 0 0 10px;
    }}
    .line-meta {{
      color: #5b6770;
      font-size: 11px;
      margin-bottom: 3px;
    }}
    .empty {{
      margin: 0;
      color: #5b6770;
    }}
    @media (max-width: 900px) {{
      .meta-grid {{
        grid-template-columns: 1fr;
      }}
    }}
  </style>
</head>
<body>
  <div id="explanationMenu" class="explanation-menu">
    <button type="button" onclick="regenerateCurrentExplanation()">Regenerate explanation</button>
  </div>
  <div id="infoBubble" class="info-bubble" role="dialog" aria-modal="false" aria-label="Control explanation">
    <div class="info-bubble-header">
      <button type="button" class="info-bubble-close" onclick="hideInfoBubble()" aria-label="Close explanation">x</button>
    </div>
    <div id="infoBubbleStatus" class="info-bubble-status">
      <span class="info-bubble-spinner" aria-hidden="true"></span>
      <span id="infoBubbleStatusText">Loading explanation...</span>
    </div>
    <div id="infoBubbleBody" class="info-bubble-body"></div>
  </div>
  <div class="page">
    <div class="hero">
      <h1>{policy_title}</h1>
      <div class="sub">{root_domain_name} ({root_domain_code})</div>
    </div>

    <div class="meta-grid">
      <div class="meta-card">
        <div class="meta-label">Policy Code</div>
        <div class="meta-value">{policy_code}</div>
      </div>
      <div class="meta-card">
        <div class="meta-label">Version / Status</div>
        <div class="meta-value">{version_text} / {status}</div>
      </div>
      <div class="meta-card">
        <div class="meta-label">Template / Model</div>
        <div class="meta-value">{template_name} / {model_name}</div>
      </div>
    </div>

    <div class="meta-grid">
      <div class="meta-card">
        <div class="meta-label">Root Domain</div>
        <div class="meta-value">{root_domain_name}</div>
      </div>
      <div class="meta-card">
        <div class="meta-label">Domain Code</div>
        <div class="meta-value">{root_domain_code}</div>
      </div>
      <div class="meta-card">
        <div class="meta-label">Last Saved</div>
        <div class="meta-value">{updated_at}</div>
      </div>
    </div>

    <section class="section-card">
      <h2>Objectives</h2>
      {_render_policy_line_items(policy_data.get("objectives") or [])}
    </section>

    <section class="section-card">
      <h2>Principles</h2>
      {_render_policy_line_items(policy_data.get("principles") or [])}
    </section>

    <section class="section-card">
      <h2>Accountability</h2>
      {_render_policy_line_items(policy_data.get("accountability") or [])}
    </section>

    <section class="section-card">
      <h2>Transparency</h2>
      {_render_policy_line_items(policy_data.get("transparency") or [])}
    </section>

    <section class="section-card">
      <h2>Strategy</h2>
      {_render_policy_line_items(policy_data.get("strategy") or [])}
    </section>

    <section class="section-card">
      <h2>Policy Statements By Control</h2>
      {''.join(control_html_parts)}
    </section>

    <section class="section-card">
      <h2>Consequences</h2>
      {_render_policy_line_items(policy_data.get("consequences") or [])}
    </section>
  </div>
  <script>
    let currentExplanationButton = null;
    let currentBubbleButton = null;

    function hideExplanationMenu() {{
      const menu = document.getElementById('explanationMenu');
      menu.style.display = 'none';
      currentExplanationButton = null;
    }}

    function hideInfoBubble() {{
      const bubble = document.getElementById('infoBubble');
      bubble.classList.remove('is-visible');
      setInfoBubbleBusy(false);
      document.body.appendChild(bubble);
      currentBubbleButton = null;
    }}

    function setInfoBubbleBusy(isBusy, text) {{
      const status = document.getElementById('infoBubbleStatus');
      const statusText = document.getElementById('infoBubbleStatusText');
      if (statusText && text) {{
        statusText.textContent = text;
      }}
      if (status) {{
        status.classList.toggle('is-visible', !!isBusy);
      }}
      if (currentBubbleButton) {{
        currentBubbleButton.classList.toggle('is-loading', !!isBusy);
        currentBubbleButton.disabled = !!isBusy;
        if (isBusy) {{
          currentBubbleButton.textContent = '...';
        }} else {{
          currentBubbleButton.textContent = currentBubbleButton.classList.contains('has-info') ? 'i' : '+';
        }}
      }}
    }}

    function positionInfoBubble(button) {{
      const bubble = document.getElementById('infoBubble');
      const card = button.closest('.control-card');
      if (!card) {{
        return;
      }}
      card.appendChild(bubble);
      const buttonRect = button.getBoundingClientRect();
      const cardRect = card.getBoundingClientRect();
      const bubbleWidth = Math.min(320, Math.max(220, cardRect.width - 24));
      const bubbleHeight = bubble.offsetHeight || 160;
      const localButtonRight = buttonRect.right - cardRect.left;
      const localButtonTop = buttonRect.top - cardRect.top;
      const localButtonBottom = buttonRect.bottom - cardRect.top;
      let left = localButtonRight - bubbleWidth + 12;
      left = Math.max(12, Math.min(left, Math.max(12, cardRect.width - bubbleWidth - 12)));

      const viewportMargin = 16;
      const spaceBelowViewport = window.innerHeight - buttonRect.bottom - viewportMargin;
      const spaceAboveViewport = buttonRect.top - viewportMargin;
      const showAbove = spaceBelowViewport < bubbleHeight + 24 && spaceAboveViewport > spaceBelowViewport;

      bubble.classList.toggle('above', showAbove);
      bubble.style.width = `${{bubbleWidth}}px`;
      bubble.style.left = `${{left}}px`;
      bubble.style.top = showAbove
        ? `${{Math.max(12, localButtonTop - bubbleHeight - 16)}}px`
        : `${{localButtonBottom + 14}}px`;
    }}

    async function loadControlExplanation(button, forceRefresh) {{
      const policyId = button.dataset.policyId;
      const controlCode = button.dataset.controlCode;
      const explanation = document.querySelector(`.control-explanation[data-control-code="${{CSS.escape(controlCode)}}"]`);
      const bubble = document.getElementById('infoBubble');
      const bubbleBody = document.getElementById('infoBubbleBody');
      if (!explanation) {{
        return;
      }}

      if (!forceRefresh && explanation.dataset.loaded === 'true') {{
        if (currentBubbleButton === button && bubble.classList.contains('is-visible')) {{
          hideInfoBubble();
          return;
        }}
        bubbleBody.textContent = explanation.textContent || 'No explanation available.';
        bubble.classList.add('is-visible');
        currentBubbleButton = button;
        setInfoBubbleBusy(false);
        positionInfoBubble(button);
        return;
      }}

      bubbleBody.textContent = '';
      bubble.classList.add('is-visible');
      currentBubbleButton = button;
      setInfoBubbleBusy(true, forceRefresh ? 'Refreshing explanation...' : 'Loading explanation...');
      positionInfoBubble(button);

      const response = await fetch(`/policies/${{encodeURIComponent(policyId)}}/controls/${{encodeURIComponent(controlCode)}}/explanation`, {{
        method: 'POST',
        headers: {{ 'Content-Type': 'application/json' }},
        body: JSON.stringify({{ force: !!forceRefresh }})
      }});
      if (!response.ok) {{
        let errorText = 'Explanation unavailable.';
        try {{
          const payload = await response.json();
          errorText = payload.detail || payload.error || errorText;
        }} catch {{
        }}
        setInfoBubbleBusy(false);
        bubbleBody.textContent = errorText;
        return;
      }}

      const payload = await response.json();
      explanation.textContent = payload.ExplanationText || payload.explanationText || 'No explanation available.';
      explanation.dataset.loaded = 'true';
      setInfoBubbleBusy(false);
      bubbleBody.textContent = explanation.textContent;
      positionInfoBubble(button);
      button.textContent = 'i';
      button.classList.remove('no-info');
      button.classList.add('has-info');
    }}

    function toggleControlExplanation(button) {{
      hideExplanationMenu();
      loadControlExplanation(button, false);
    }}

    function showExplanationMenu(event, button) {{
      event.preventDefault();
      currentExplanationButton = button;
      const menu = document.getElementById('explanationMenu');
      menu.style.left = `${{event.clientX}}px`;
      menu.style.top = `${{event.clientY}}px`;
      menu.style.display = 'block';
    }}

    function regenerateCurrentExplanation() {{
      if (!currentExplanationButton) {{
        hideExplanationMenu();
        return;
      }}
      loadControlExplanation(currentExplanationButton, true);
      const menu = document.getElementById('explanationMenu');
      menu.style.display = 'none';
    }}

    window.addEventListener('resize', () => {{
      if (currentBubbleButton) {{
        positionInfoBubble(currentBubbleButton);
      }}
    }});

    document.addEventListener('click', (event) => {{
      const menu = document.getElementById('explanationMenu');
      const bubble = document.getElementById('infoBubble');
      if (!menu.contains(event.target)) {{
        hideExplanationMenu();
      }}
      if (bubble.classList.contains('is-visible')
          && !bubble.contains(event.target)
          && !menu.contains(event.target)
          && !event.target.closest('.info-button')) {{
        hideInfoBubble();
      }}
    }});
  </script>
</body>
</html>"""


def _build_policy_control_explanation_prompt(policy_data: dict[str, object], control_code: str) -> str:
    policy = policy_data.get("policy") or {}
    statements = [
        row for row in (policy_data.get("controlStatements") or [])
        if str(row.get("ControlCode") or "").strip().lower() == str(control_code or "").strip().lower()
    ]
    if not statements:
        raise ValueError(f"No policy control statements were found for control '{control_code}'.")

    first_row = statements[0]
    statement_lines = "\n".join(
        f"- {str(row.get('StatementText') or '').strip()}"
        for row in statements
        if str(row.get("StatementText") or "").strip()
    )

    return (
        "Write a very brief explanation for a policy control. "
        "Keep it to 2 or 3 sentences. Explain what the control is trying to do and why it matters. "
        "Use clear professional language for workplace readers rather than legal drafting style. "
        "Prefer direct sentence structure and practical wording that informed staff can grasp quickly. "
        "Avoid legal jargon, archaic wording, and unnecessarily dense phrasing unless a precise term is required. "
        "Keep the explanation accurate and defensible for non-lawyers. "
        "Do not use bullets, headings, legal wording, or the phrase 'plain-language'.\n\n"
        f"Policy title: {str(policy.get('PolicyTitle') or '')}\n"
        f"Root domain: {str(policy.get('RootDomainName') or '')} ({str(policy.get('RootDomainCode') or '')})\n"
        f"Control name: {str(first_row.get('ControlName') or '')}\n"
        f"Control code: {str(first_row.get('ControlCode') or '')}\n"
        f"Control type: {str(first_row.get('ControlTypeName') or '')} ({str(first_row.get('ControlTypeCode') or '')})\n"
        f"Group: {str(first_row.get('GroupLabel') or '')}\n\n"
        f"Policy statements for this control:\n{statement_lines}"
    )


def _clean_policy_control_explanation(text: str) -> str:
    cleaned = (text or "").strip()
    if cleaned.startswith("```"):
        cleaned = re.sub(r"^```(?:text)?\s*", "", cleaned, flags=re.IGNORECASE)
        cleaned = re.sub(r"\s*```$", "", cleaned)

    cleaned = " ".join(cleaned.split())
    cleaned = re.sub(
        r"^\s*(?:here(?:'s| is)\s+(?:a|an)\s+(?:brief\s+)?(?:simple\s+)?(?:plain-language\s+)?explanation(?:\s+for\s+the)?[^:]*:\s*)",
        "",
        cleaned,
        flags=re.IGNORECASE,
    )
    cleaned = re.sub(r"\bplain-language\b", "", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\s{2,}", " ", cleaned).strip(" :.-")
    return cleaned


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


def _build_root_domain_insert_preview(
    domain_type_code: str,
    domain_code: str,
    display_name: str,
    description: str | None,
) -> str:
    type_literal = _sql_nvarchar_literal(domain_type_code)
    code_literal = _sql_nvarchar_literal(domain_code)
    name_literal = _sql_nvarchar_literal(display_name.strip())
    description_literal = _sql_nvarchar_literal(description.strip() if description else None)

    return (
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
        "    NULL,\n"
        "    dt.ID,\n"
        "    NULL,\n"
        "    COALESCE((\n"
        "        SELECT MAX(sibling.DisplayOrder) + 10\n"
        "        FROM dbo.Domains sibling\n"
        "        WHERE sibling.Status = 'Active' AND sibling.DomainParentId IS NULL\n"
        "    ), 10),\n"
        f"    {code_literal},\n"
        f"    {name_literal},\n"
        f"    {description_literal}\n"
        "FROM dbo.DomainTypes dt\n"
        "WHERE dt.CODE = @DomainTypeCode;"
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

    @app.post("/domain-types")
    def add_domain_type(request: CreateDomainTypeRequest) -> dict[str, object]:
        return create_domain_type(
            settings,
            name=request.name,
            description=request.description,
        )

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

    @app.get("/controls/report-rows")
    def controls_report_rows() -> list[dict[str, object]]:
        return list_controls_report_rows(settings)

    @app.get("/reports/controls-smart", response_class=HTMLResponse)
    def controls_smart_report() -> HTMLResponse:
        rows = list_controls_report_rows(settings)
        return HTMLResponse(_build_controls_report_html(rows))

    @app.get("/reports/policy-draft", response_class=HTMLResponse)
    def policy_draft_report(
        domainCode: str | None = None,
        templatePath: str = "Policy/Policy-Template-1.01.md",
        model: str | None = None,
    ) -> HTMLResponse:
        try:
            template_file = _resolve_repo_relative_path(templatePath)
            template_text = template_file.read_text(encoding="utf-8")
            all_domains = list_domains(settings)
            selected_model = (model or settings.ollama_chat_model).strip()
            report_rows = list_controls_report_rows(settings)
            domain_codes_with_controls = {
                str(row.get("DomainCode") or "").strip()
                for row in report_rows
                if row.get("DomainCode")
            }
            available_domains = [
                domain
                for domain in all_domains
                if str(domain.get("DomainCode") or "").strip() in domain_codes_with_controls
            ]
            selected_domain_code = (domainCode or "").strip()
            domain_options = [
                {
                    "value": "",
                    "label": "Select a domain...",
                    "selected": not selected_domain_code,
                },
                *[
                    {
                        "value": str(domain.get("DomainCode") or ""),
                        "label": _domain_breadcrumb(domain, all_domains),
                        "selected": str(domain.get("DomainCode") or "") == selected_domain_code,
                    }
                    for domain in available_domains
                ],
            ]

            root_domain_name = "No domain selected"
            draft_html = (
                "<p>Select a domain, keep or change the template path and model if you want, then click <strong>Generate</strong>.</p>"
            )

            if selected_domain_code:
                context = get_control_suggestion_context(settings, selected_domain_code)
                root_domain = context.get("rootDomain") or {}
                branch_domains = context.get("branchDomains") or []
                existing_controls = context.get("existingControls") or []
                system_prompt, user_prompt = _build_policy_generation_prompt(
                    template_text,
                    root_domain,
                    branch_domains,
                    existing_controls,
                    all_domains,
                )
                payload = _generate_with_ollama(
                    settings,
                    f"{system_prompt}\n\n{user_prompt}",
                    model=selected_model,
                    trace_label="policy.browser-draft",
                )
                draft_markdown = _finalize_policy_markdown(
                    str(root_domain.get("DisplayName") or selected_domain_code),
                    str(payload.get("response", "")).strip(),
                )
                draft_html = _render_simple_markdown_to_html(draft_markdown)
                root_domain_name = str(root_domain.get("DisplayName") or selected_domain_code)

            return HTMLResponse(
                _build_policy_browser_html(
                    domain_code=selected_domain_code,
                    template_path=templatePath,
                    model_name=selected_model,
                    body_html=draft_html,
                    root_domain_name=root_domain_name,
                    domain_options=domain_options,
                )
            )
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/policy-drafts/content")
    def policy_draft_content(request: PolicyDraftContentRequest) -> dict[str, object]:
        try:
            template_file = _resolve_repo_relative_path(request.templatePath)
            template_text = template_file.read_text(encoding="utf-8")
            all_domains = list_domains(settings)
            context = get_control_suggestion_context(settings, request.domainCode)
            branch_controls = list_controls_for_branch(settings, request.domainCode)
            control_groups = _normalize_policy_control_groups(request.controlGroups)
            included_control_codes = None if request.includedControlCodes is None else {
                str(code).strip().upper()
                for code in request.includedControlCodes
                if str(code).strip()
            }
            if included_control_codes is not None:
                branch_controls = [
                    control
                    for control in branch_controls
                    if str(control.get("ControlCode") or "").strip().upper() in included_control_codes
                ]
            root_domain = context.get("rootDomain") or {}
            branch_domains = context.get("branchDomains") or []
            selected_model = (request.model or settings.ollama_chat_model).strip()
            system_prompt, user_prompt = _build_policy_content_prompt(
                template_text,
                root_domain,
                branch_domains,
                branch_controls,
                all_domains,
                control_groups,
            )
            payload = _generate_with_ollama(
                settings,
                f"{system_prompt}\n\n{user_prompt}",
                model=selected_model,
                trace_label="policy.content-draft",
            )
            parsed = _extract_json_object(str(payload.get("response", "")).strip())
            normalized = _normalize_policy_content_draft(
                parsed,
                root_domain,
                branch_controls,
                str(payload.get("model") or selected_model),
                all_domains,
                control_groups,
            )
            normalized["metrics"] = _extract_metrics(payload, model=selected_model)
            return normalized
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/policy-drafts/redraft-line")
    def policy_draft_redraft_line(request: PolicyDraftLineRetryRequest) -> dict[str, object]:
        try:
            template_file = _resolve_repo_relative_path(request.templatePath)
            template_text = template_file.read_text(encoding="utf-8")
            all_domains = list_domains(settings)
            context = get_control_suggestion_context(settings, request.domainCode)
            branch_controls = list_controls_for_branch(settings, request.domainCode)
            control_groups = _normalize_policy_control_groups(request.controlGroups)
            included_control_codes = None if request.includedControlCodes is None else {
                str(code).strip().upper()
                for code in request.includedControlCodes
                if str(code).strip()
            }
            if included_control_codes is not None:
                branch_controls = [
                    control
                    for control in branch_controls
                    if str(control.get("ControlCode") or "").strip().upper() in included_control_codes
                ]
            root_domain = context.get("rootDomain") or {}
            branch_domains = context.get("branchDomains") or []
            selected_model = (request.model or settings.ollama_chat_model).strip()
            prompt = _build_policy_line_retry_prompt(
                template_text=template_text,
                root_domain=root_domain,
                branch_domains=branch_domains,
                branch_controls=branch_controls,
                all_domains=all_domains,
                control_groups=control_groups,
                section_key=request.sectionKey,
                current_text=request.currentText,
                control_code=request.controlCode,
            )
            payload = _generate_with_ollama(
                settings,
                prompt,
                model=selected_model,
                trace_label="policy.line-redraft",
            )
            replacement_text = str(payload.get("response", "")).strip()
            if replacement_text.startswith("```"):
                replacement_text = re.sub(r"^```(?:text)?\s*", "", replacement_text, flags=re.IGNORECASE)
                replacement_text = re.sub(r"\s*```$", "", replacement_text)
            replacement_text = " ".join(replacement_text.split())
            if not replacement_text:
                raise ValueError("The model did not return replacement text.")

            return {
                "text": replacement_text,
                "sectionKey": request.sectionKey,
                "controlCode": request.controlCode,
                "modelName": str(payload.get("model") or selected_model),
                "metrics": _extract_metrics(payload, model=selected_model),
            }
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/policies/save-draft")
    def save_policy_draft(request: SavePolicyDraftRequest) -> dict[str, object]:
        try:
            template_body = None
            if request.templatePath:
                template_file = _resolve_repo_relative_path(request.templatePath)
                template_body = template_file.read_text(encoding="utf-8")

            return upsert_policy_draft(
                settings,
                root_domain_code=request.rootDomainCode,
                policy_code=request.policyCode,
                policy_title=request.policyTitle,
                version_text=request.versionText,
                status=request.status,
                template_path=request.templatePath,
                template_body=template_body,
                source_model_name=request.sourceModelName,
                objectives=[item.model_dump() for item in request.objectives],
                principles=[item.model_dump() for item in request.principles],
                accountability=[item.model_dump() for item in request.accountability],
                transparency=[item.model_dump() for item in request.transparency],
                strategy=[item.model_dump() for item in request.strategy],
                consequences=[item.model_dump() for item in request.consequences],
                control_statements=[item.model_dump() for item in request.controlStatements],
            )
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.get("/policies/by-root-domain/{domainCode}")
    def load_policy_draft_for_domain(domainCode: str) -> dict[str, object]:
        try:
            policy_data = get_latest_policy_for_root_domain(settings, domainCode)
            if not policy_data:
                raise HTTPException(status_code=404, detail="No saved policy exists for this domain.")
            return _build_saved_policy_draft_payload(policy_data)
        except HTTPException:
            raise
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.get("/policies/{policyId}/draft")
    def load_policy_draft_by_id(policyId: str) -> dict[str, object]:
        try:
            policy_data = get_policy_presentation_data(settings, policyId)
            return _build_saved_policy_draft_payload(policy_data)
        except Exception as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc

    @app.post("/policies/{policyId}/controls/{controlCode}/explanation")
    def generate_policy_control_explanation(
        policyId: str,
        controlCode: str,
        request: PolicyControlExplanationRequest,
    ) -> dict[str, object]:
        try:
            policy_data = get_policy_presentation_data(settings, policyId)
            existing = {
                str(item.get("ControlCode") or "").strip().lower(): item
                for item in list_policy_control_explanations(settings, policyId)
            }.get(str(controlCode or "").strip().lower())
            if existing and not request.force:
                return existing

            selected_model = request.model or settings.ollama_chat_model
            prompt = _build_policy_control_explanation_prompt(policy_data, controlCode)
            payload = _generate_with_ollama(
                settings,
                prompt,
                model=selected_model,
                trace_label="policy.control-explanation",
            )
            explanation_text = _clean_policy_control_explanation(str(payload.get("response") or ""))
            if not explanation_text:
                raise ValueError("The model did not return an explanation.")

            saved = upsert_policy_control_explanation(
                settings,
                policy_id=policyId,
                control_code=controlCode,
                explanation_text=explanation_text,
                source_model_name=selected_model,
            )
            return saved
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.delete("/policies/{policyId}")
    def remove_policy(policyId: str) -> dict[str, object]:
        try:
            return delete_policy(settings, policyId)
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/policies/testing/clear-all")
    def clear_all_policy_test_data() -> dict[str, object]:
        try:
            return clear_policy_tables(settings)
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc

    @app.post("/controls/suggest")
    def suggest_controls(request: ControlSuggestionRequest) -> dict[str, object]:
        try:
            context = get_control_suggestion_context(settings, request.branchRootDomainCode)
            control_types = list_control_types(settings)
            prompt = _build_control_suggestion_prompt_text(context, control_types, request)
            payload = _generate_with_ollama(
                settings,
                prompt,
                model=request.model,
                trace_label="controls.suggest",
            )
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

    @app.post("/controls/grouping/ai")
    def ai_group_controls(request: ControlGroupingRequest) -> dict[str, object]:
        try:
            context = get_control_suggestion_context(settings, request.domainCode)
            branch_controls = list_controls_for_branch(settings, request.domainCode)
            if request.controlCodes is not None:
                allowed_codes = {
                    str(code).strip().upper()
                    for code in request.controlCodes
                    if str(code).strip()
                }
                branch_controls = [
                    item
                    for item in branch_controls
                    if str(item.get("ControlCode") or "").strip().upper() in allowed_codes
                ]

            if not branch_controls:
                return {"groups": [], "assignments": [], "metrics": _extract_metrics({}, request.model)}

            prompt = _build_ai_control_grouping_prompt(
                root_domain=context.get("rootDomain") or {},
                branch_controls=branch_controls,
            )
            selected_model = request.model or settings.ollama_chat_model
            payload = _generate_with_ollama(
                settings,
                prompt,
                model=selected_model,
                trace_label="controls.grouping.ai",
            )
            parsed = _extract_json_object(str(payload.get("response") or "").strip())
            raw_groups = parsed.get("groups")
            if not isinstance(raw_groups, list):
                raise ValueError("The model response did not include a groups array.")

            valid_control_codes = {
                str(item.get("ControlCode") or "").strip().upper(): str(item.get("ControlCode") or "").strip()
                for item in branch_controls
                if item.get("ControlCode")
            }
            grouped_assignments: dict[str, str] = {}
            normalized_groups: list[dict[str, object]] = []
            for raw_group in raw_groups:
                if not isinstance(raw_group, dict):
                    continue
                label = str(raw_group.get("label") or "").strip()
                if not label:
                    continue
                raw_codes = raw_group.get("controlCodes")
                if not isinstance(raw_codes, list):
                    continue
                normalized_codes: list[str] = []
                for raw_code in raw_codes:
                    code_key = str(raw_code or "").strip().upper()
                    if not code_key or code_key not in valid_control_codes or code_key in grouped_assignments:
                        continue
                    canonical_code = valid_control_codes[code_key]
                    grouped_assignments[code_key] = label
                    normalized_codes.append(canonical_code)
                if normalized_codes:
                    normalized_groups.append(
                        {
                            "groupLabel": label,
                            "controlCodes": normalized_codes,
                        }
                    )

            unassigned_codes = [
                canonical_code
                for code_key, canonical_code in valid_control_codes.items()
                if code_key not in grouped_assignments
            ]
            if unassigned_codes:
                fallback_label = "Other Controls"
                normalized_groups.append(
                    {
                        "groupLabel": fallback_label,
                        "controlCodes": unassigned_codes,
                    }
                )
                for code in unassigned_codes:
                    grouped_assignments[code.strip().upper()] = fallback_label

            assignments = [
                {
                    "controlCode": str(item.get("ControlCode") or ""),
                    "groupLabel": grouped_assignments.get(str(item.get("ControlCode") or "").strip().upper(), "Other Controls"),
                }
                for item in branch_controls
            ]

            return {
                "groups": normalized_groups,
                "assignments": assignments,
                "metrics": _extract_metrics(payload, selected_model),
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

    @app.delete("/controls/{controlId}")
    def remove_control(controlId: str) -> dict[str, object]:
        try:
            return delete_control(settings, controlId)
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
        answer_payload = _generate_with_ollama(
            settings,
            compiled_prompt,
            model=request.model,
            trace_label="domains.assist",
        )
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

        domain_types = list_domain_types(settings)
        if request.parentDomainCode:
            domain_context = get_domain_assist_context(settings, request.parentDomainCode)
            compiled_prompt = _build_child_domain_suggestion_prompt(
                domain_context,
                instruction,
                request.draftText,
                domain_types,
            )
            root_mode = False
        else:
            if not request.targetDomainType:
                raise HTTPException(status_code=400, detail="A parent domain code or target domain type is required.")
            compiled_prompt = _build_root_domain_suggestion_prompt_parts(
                request.targetDomainType,
                instruction,
                request.draftText,
                domain_types,
                list_domains(settings),
            )
            compiled_prompt = f"{compiled_prompt[0]}\n\n{compiled_prompt[1]}"
            root_mode = True
        answer_payload = _generate_with_ollama(
            settings,
            compiled_prompt,
            model=request.model,
            trace_label="domains.child-suggest",
        )
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
            sql_preview = (
                _build_root_domain_insert_preview(
                    domain_type_code,
                    domain_code,
                    display_name,
                    description or None,
                )
                if root_mode
                else _build_child_domain_insert_preview(
                    str(request.parentDomainCode).strip(),
                    domain_type_code,
                    domain_code,
                    display_name,
                    description or None,
                )
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

        domain_types = list_domain_types(settings)
        if request.parentDomainCode:
            domain_context = get_domain_assist_context(settings, request.parentDomainCode)
            system_prompt, user_prompt = _build_child_domain_suggestion_prompt_parts(
                domain_context,
                instruction,
                request.draftText,
                domain_types,
            )
        else:
            if not request.targetDomainType:
                raise HTTPException(status_code=400, detail="A parent domain code or target domain type is required.")
            system_prompt, user_prompt = _build_root_domain_suggestion_prompt_parts(
                request.targetDomainType,
                instruction,
                request.draftText,
                domain_types,
                list_domains(settings),
            )
        return PromptPreviewResponse(
            model=request.model or settings.ollama_chat_model,
            systemPrompt=system_prompt,
            userPrompt=user_prompt,
        )

    @app.post("/domains/suggest-child/execute")
    def execute_suggested_child_domain(request: ExecuteDomainChildSuggestionRequest) -> dict[str, object]:
        domain_types = list_domain_types(settings)
        try:
            resolved_domain_type = _resolve_domain_type(domain_types, request.domainType)
            domain_type_code = str(resolved_domain_type.get("CODE") or "").strip().upper()
            domain_code = _slugify_code(request.domainCode or request.displayName)
            if request.parentDomainCode:
                domain_context = get_domain_assist_context(settings, request.parentDomainCode)
                parent_domain = domain_context.get("domain") or {}
                if not parent_domain:
                    raise HTTPException(status_code=404, detail="Parent domain not found.")

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
            else:
                if not request.targetDomainType:
                    raise HTTPException(status_code=400, detail="A parent domain code or target domain type is required.")
                sql_preview = _build_root_domain_insert_preview(
                    domain_type_code,
                    domain_code,
                    request.displayName,
                    request.description,
                )
                created_domain = create_domain(
                    settings,
                    domain_code=domain_code,
                    domain_type_id=int(resolved_domain_type.get("ID")),
                    domain_orientation_id=None,
                    display_name=request.displayName,
                    description=request.description,
                    domain_parent_id=None,
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
            parent_domain_id=request.parentDomainId,
        )

    @app.post("/domains/move")
    def move_domain_node(request: MoveDomainRequest) -> dict[str, object]:
        return move_domain(
            settings,
            domain_code=request.domainCode,
            new_parent_domain_code=request.newParentDomainCode,
            new_domain_type_id=request.newDomainTypeId,
        )

    @app.put("/domain-sibling-order")
    def reorder_domains(request: ReorderDomainSiblingsRequest) -> dict[str, object]:
        return reorder_root_domains(
            settings,
            parent_domain_id=request.parentDomainId,
            orientation_code=request.orientationCode,
            ordered_domain_codes=request.orderedDomainCodes,
        )

    @app.put("/domain-type-order")
    def reorder_types(request: ReorderDomainTypesRequest) -> dict[str, object]:
        return reorder_domain_types(
            settings,
            ordered_type_ids=request.orderedTypeIds,
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

    @app.get("/policies")
    def policies() -> list[dict[str, object]]:
        return list_policies(settings)

    @app.get("/policies/{policyId}/presentation", response_class=HTMLResponse)
    def policy_presentation(policyId: str) -> HTMLResponse:
        try:
            policy_data = get_policy_presentation_data(settings, policyId)
            return HTMLResponse(_build_saved_policy_html(policy_data))
        except Exception as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc

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
        result = create_text_document(
            settings,
            collection_code=request.collectionCode,
            source_name=request.sourceName,
            body_text=request.bodyText,
            source_type=request.sourceType,
        )
        try:
            result["EmbeddingResult"] = _ensure_embeddings_for_content(
                settings,
                document_id=str(result.get("DocumentId") or ""),
            )
        except Exception as exc:
            result["EmbeddingError"] = str(exc)
        return result

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
        try:
            result["EmbeddingResult"] = _ensure_embeddings_for_content(
                settings,
                document_id=str(result.get("DocumentId") or ""),
            )
        except Exception as exc:
            result["EmbeddingError"] = str(exc)
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

    @app.get("/debug/embedding-status")
    def embedding_status() -> dict[str, object]:
        profile = get_default_embedding_profile(settings)
        status = list_embedding_status(
            settings,
            embedding_profile_id=str(profile["EmbeddingProfileId"]),
        )
        return {
            "profileCode": str(profile.get("ProfileCode") or ""),
            "modelName": str(profile.get("ModelName") or ""),
            "vectorDimension": int(profile.get("VectorDimension") or 768),
            **status,
        }

    @app.get("/debug/embeddings", response_class=HTMLResponse)
    def embedding_status_html() -> HTMLResponse:
        status = embedding_status()
        base_url = _resolve_backend_base_url(settings)
        return HTMLResponse(_build_embedding_debug_html(base_url, status))

    @app.post("/debug/embeddings/backfill")
    def embedding_backfill(collectionCode: str | None = None, limit: int = 200) -> dict[str, object]:
        collection_codes = [collectionCode] if collectionCode and collectionCode.strip() else None
        result = _ensure_embeddings_for_content(
            settings,
            collection_codes=collection_codes,
            limit=max(1, min(limit, 2000)),
        )
        status = embedding_status()
        return {
            "status": "ok",
            "backfill": result,
            "embeddingStatus": status,
        }

    @app.get("/debug/llm-traces.json")
    def llm_traces_json() -> dict[str, object]:
        traces = _list_llm_traces()
        return {
            "count": len(traces),
            "traces": traces,
        }

    @app.get("/debug/llm-traces")
    def llm_traces_html() -> HTMLResponse:
        traces = _list_llm_traces()
        base_url = _resolve_backend_base_url(settings)
        return HTMLResponse(_build_llm_trace_html(base_url, traces))

    @app.post("/ask/context-preview")
    def ask_context_preview(request: AskRequest) -> ContextPreviewResponse:
        context_units, retrieval_info = _retrieve_context_for_chat(settings, request)
        context_lines = _build_context_lines(context_units)
        policy_context_lines: list[str] = []
        domain_context_lines: list[str] = []
        controls_context_lines: list[str] = []
        selected_domain_code = str(request.selectedDomainCode or "").strip()
        if selected_domain_code and request.includePolicies:
            try:
                policy_data = get_latest_policy_for_root_domain(settings, selected_domain_code)
                if policy_data:
                    policy_context_lines = _build_policy_context_lines(policy_data)
            except Exception:
                policy_context_lines = []
        if selected_domain_code and request.includeDomainContext:
            try:
                domain_context = get_domain_assist_context(settings, selected_domain_code)
                if domain_context:
                    domain_context_lines = _build_domain_context_lines(domain_context)
            except Exception:
                domain_context_lines = []
        if selected_domain_code and request.includeControls:
            try:
                control_rows = list_controls_for_branch(settings, selected_domain_code)
                if control_rows:
                    controls_context_lines = _build_controls_context_lines(control_rows)
            except Exception:
                controls_context_lines = []

        all_context_lines = [*context_lines, *policy_context_lines, *domain_context_lines, *controls_context_lines]
        context_text = chr(10).join(all_context_lines) if all_context_lines else ""
        context_token_count = sum(
            int(row.get("TokenCount") or 0) if int(row.get("TokenCount") or 0) > 0 else _estimate_token_count(str(row.get("BodyText") or ""))
            for row in context_units
        ) + _estimate_token_count(chr(10).join([*policy_context_lines, *domain_context_lines, *controls_context_lines]))
        sources = _build_source_items(context_units)
        return ContextPreviewResponse(
            retrievalMode=str(retrieval_info.get("retrievalMode") or "FullContext"),
            retrievalWarning=str(retrieval_info.get("fallbackReason") or ""),
            usedCollectionCodes=list(retrieval_info.get("collectionCodes") or []),
            contextUnitCount=len(context_units),
            contextTokenCount=context_token_count,
            contextCharCount=len(context_text),
            sourceCount=len({
                f"{source.get('collectionDisplayName')}::{source.get('sourceName')}"
                for source in sources
            }),
            sources=sources,
        )

    @app.post("/ask")
    def ask(request: AskRequest) -> dict[str, object]:
        prompt = request.prompt.strip()
        if not prompt:
            return {"error": "Prompt is required."}

        context_units, retrieval_info = _retrieve_context_for_chat(settings, request)
        collection_codes = list(retrieval_info.get("collectionCodes") or [])
        context_lines = _build_context_lines(context_units)
        policy_context_lines: list[str] = []
        domain_context_lines: list[str] = []
        controls_context_lines: list[str] = []
        selected_domain_code = str(request.selectedDomainCode or "").strip()
        if selected_domain_code and request.includePolicies:
            try:
                policy_data = get_latest_policy_for_root_domain(settings, selected_domain_code)
                if policy_data:
                    policy_context_lines = _build_policy_context_lines(policy_data)
            except Exception:
                policy_context_lines = []
        if selected_domain_code and request.includeDomainContext:
            try:
                domain_context = get_domain_assist_context(settings, selected_domain_code)
                if domain_context:
                    domain_context_lines = _build_domain_context_lines(domain_context)
            except Exception:
                domain_context_lines = []
        if selected_domain_code and request.includeControls:
            try:
                control_rows = list_controls_for_branch(settings, selected_domain_code)
                if control_rows:
                    controls_context_lines = _build_controls_context_lines(control_rows)
            except Exception:
                controls_context_lines = []
        combined_context_lines = [*context_lines, *policy_context_lines, *domain_context_lines, *controls_context_lines]
        sources = _build_source_items(context_units)

        history_lines = []
        for item in request.history:
            role = (item.get("role") or "").strip().lower()
            content = (item.get("content") or "").strip()
            if role and content:
                history_lines.append(f"{role.title()}: {content}")

        context_text = chr(10).join(combined_context_lines) if combined_context_lines else "No stored context was found."
        history_text = chr(10).join(history_lines) if history_lines else "No previous conversation."

        compiled_prompt = (
            "Answer the user using the provided short-memory and durable-domain context when relevant. "
            "If the context is thin or missing, say so clearly.\n\n"
            f"Conversation so far:\n{history_text}\n\n"
            f"User prompt:\n{prompt}\n\n"
            f"Context:\n{context_text}\n"
        )
        trace_metadata = {
            **_build_chat_trace_metadata(
                retrieval_mode=str(retrieval_info.get("retrievalMode") or "FullContext"),
                collection_codes=collection_codes,
                context_units=context_units,
                context_text=context_text,
                prompt=prompt,
                history_lines=history_lines,
                history_text=history_text,
                compiled_prompt=compiled_prompt,
                retrieval_profile=retrieval_info.get("retrievalProfile") if isinstance(retrieval_info.get("retrievalProfile"), dict) else None,
                policy_context_lines=[*policy_context_lines, *domain_context_lines, *controls_context_lines],
                fallback_reason=str(retrieval_info.get("fallbackReason") or "") or None,
            ),
        }

        answer_payload = _generate_with_ollama(
            settings,
            compiled_prompt,
            model=request.model,
            trace_label="chat.ask",
            trace_metadata=trace_metadata,
        )
        answer = str(answer_payload.get("response", "")).strip()
        title = _generate_title(settings, prompt, answer)
        return {
            "answer": answer,
            "title": title,
            "sources": sources,
            "usedCollectionCodes": collection_codes,
            "retrievalMode": retrieval_info.get("retrievalMode"),
            "retrievalWarning": retrieval_info.get("fallbackReason") or "",
            "metrics": _extract_metrics(answer_payload, model=request.model),
        }

    @app.post("/ask/stream")
    async def ask_stream(request: AskRequest):
        prompt = request.prompt.strip()
        if not prompt:
            raise HTTPException(status_code=400, detail="Prompt is required.")

        context_units, retrieval_info = _retrieve_context_for_chat(settings, request)
        collection_codes = list(retrieval_info.get("collectionCodes") or [])
        context_lines = _build_context_lines(context_units)
        policy_context_lines: list[str] = []
        domain_context_lines: list[str] = []
        controls_context_lines: list[str] = []
        selected_domain_code = str(request.selectedDomainCode or "").strip()
        if selected_domain_code and request.includePolicies:
            try:
                policy_data = get_latest_policy_for_root_domain(settings, selected_domain_code)
                if policy_data:
                    policy_context_lines = _build_policy_context_lines(policy_data)
            except Exception:
                policy_context_lines = []
        if selected_domain_code and request.includeDomainContext:
            try:
                domain_context = get_domain_assist_context(settings, selected_domain_code)
                if domain_context:
                    domain_context_lines = _build_domain_context_lines(domain_context)
            except Exception:
                domain_context_lines = []
        if selected_domain_code and request.includeControls:
            try:
                control_rows = list_controls_for_branch(settings, selected_domain_code)
                if control_rows:
                    controls_context_lines = _build_controls_context_lines(control_rows)
            except Exception:
                controls_context_lines = []
        combined_context_lines = [*context_lines, *policy_context_lines, *domain_context_lines, *controls_context_lines]
        sources = _build_source_items(context_units)

        history_lines = []
        for item in request.history:
            role = (item.get("role") or "").strip().lower()
            content = (item.get("content") or "").strip()
            if role and content:
                history_lines.append(f"{role.title()}: {content}")

        context_text = chr(10).join(combined_context_lines) if combined_context_lines else "No stored context was found."
        history_text = chr(10).join(history_lines) if history_lines else "No previous conversation."

        compiled_prompt = (
            "Answer the user using the provided short-memory and durable-domain context when relevant. "
            "If the context is thin or missing, say so clearly.\n\n"
            f"Conversation so far:\n{history_text}\n\n"
            f"User prompt:\n{prompt}\n\n"
            f"Context:\n{context_text}\n"
        )
        trace_metadata = {
            **_build_chat_trace_metadata(
                retrieval_mode=str(retrieval_info.get("retrievalMode") or "FullContext"),
                collection_codes=collection_codes,
                context_units=context_units,
                context_text=context_text,
                prompt=prompt,
                history_lines=history_lines,
                history_text=history_text,
                compiled_prompt=compiled_prompt,
                retrieval_profile=retrieval_info.get("retrievalProfile") if isinstance(retrieval_info.get("retrievalProfile"), dict) else None,
                policy_context_lines=[*policy_context_lines, *domain_context_lines, *controls_context_lines],
                fallback_reason=str(retrieval_info.get("fallbackReason") or "") or None,
            ),
        }

        async def event_stream():
            answer_parts: list[str] = []
            provisional_title = _fallback_title(prompt)
            yield json.dumps({"type": "title", "title": provisional_title}) + "\n"
            title_task = asyncio.create_task(asyncio.to_thread(_generate_prompt_title, settings, prompt))
            emitted_generated_title = False
            try:
                async for payload in _stream_with_ollama(
                    settings,
                    compiled_prompt,
                    model=request.model,
                    trace_label="chat.ask.stream",
                    trace_metadata=trace_metadata,
                ):
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
                                "retrievalMode": retrieval_info.get("retrievalMode"),
                                "retrievalWarning": retrieval_info.get("fallbackReason") or "",
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
