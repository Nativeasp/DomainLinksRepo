from __future__ import annotations

import argparse
import hashlib
import time
from dataclasses import dataclass

import httpx

from .config import Settings, get_settings
from .db import fetch_all, fetch_one, get_connection


@dataclass(frozen=True)
class SourceArtifact:
    artifact_type: str
    source_id: str
    parent_id: str | None
    display_name: str
    canonical_text: str

    @property
    def content_hash(self) -> bytes:
        return hashlib.sha256(self.canonical_text.encode("utf-8")).digest()


SOURCE_QUERIES: tuple[tuple[str, str], ...] = (
    ("Domain", """SELECT DomainId SourceId,DomainParentId ParentId,DisplayName,
        CONCAT('Domain: ',DisplayName,CHAR(10),'Code: ',DomainCode,CHAR(10),'Description: ',COALESCE(Description,'')) CanonicalText
        FROM dbo.Domains WHERE Status='Active'"""),
    ("Control", """SELECT ControlId SourceId,NULL ParentId,DisplayName,
        CONCAT('Control: ',DisplayName,CHAR(10),'Code: ',ControlCode,CHAR(10),'Description: ',COALESCE(Description,''),
        CHAR(10),'Objective: ',COALESCE(ControlObjective,''),CHAR(10),'Evidence: ',COALESCE(EvidenceExpectation,'')) CanonicalText
        FROM dbo.Controls WHERE Status IN ('Draft','Active')"""),
    ("Policy", """SELECT PolicyId SourceId,RootDomainId ParentId,PolicyTitle DisplayName,
        CONCAT('Policy: ',PolicyTitle,CHAR(10),'Code: ',PolicyCode,CHAR(10),'Version: ',COALESCE(VersionText,''),CHAR(10),'Status: ',Status) CanonicalText
        FROM dbo.Policies"""),
    ("PolicyObjective", """SELECT po.PolicyObjectiveId SourceId,ps.PolicyId ParentId,
        CONCAT(p.PolicyTitle,' — Objective') DisplayName,CONCAT('Policy: ',p.PolicyTitle,CHAR(10),'Objective: ',po.StatementText) CanonicalText
        FROM dbo.PolicyObjectives po JOIN dbo.PolicySections ps ON ps.PolicySectionId=po.PolicySectionId JOIN dbo.Policies p ON p.PolicyId=ps.PolicyId"""),
    ("PolicyPrinciple", """SELECT pp.PolicyPrincipleId SourceId,ps.PolicyId ParentId,
        CONCAT(p.PolicyTitle,' — Principle') DisplayName,CONCAT('Policy: ',p.PolicyTitle,CHAR(10),'Principle: ',pp.StatementText) CanonicalText
        FROM dbo.PolicyPrinciples pp JOIN dbo.PolicySections ps ON ps.PolicySectionId=pp.PolicySectionId JOIN dbo.Policies p ON p.PolicyId=ps.PolicyId"""),
    ("PolicyAccountability", """SELECT x.PolicyAccountabilityStatementId SourceId,ps.PolicyId ParentId,
        CONCAT(p.PolicyTitle,' — Accountability') DisplayName,CONCAT('Policy: ',p.PolicyTitle,CHAR(10),'Accountability: ',x.StatementText) CanonicalText
        FROM dbo.PolicyAccountabilityStatements x JOIN dbo.PolicySections ps ON ps.PolicySectionId=x.PolicySectionId JOIN dbo.Policies p ON p.PolicyId=ps.PolicyId"""),
    ("PolicyTransparency", """SELECT x.PolicyTransparencyStatementId SourceId,ps.PolicyId ParentId,
        CONCAT(p.PolicyTitle,' — Transparency') DisplayName,CONCAT('Policy: ',p.PolicyTitle,CHAR(10),'Transparency: ',x.StatementText) CanonicalText
        FROM dbo.PolicyTransparencyStatements x JOIN dbo.PolicySections ps ON ps.PolicySectionId=x.PolicySectionId JOIN dbo.Policies p ON p.PolicyId=ps.PolicyId"""),
    ("PolicyStrategy", """SELECT x.PolicyStrategyStatementId SourceId,ps.PolicyId ParentId,
        CONCAT(p.PolicyTitle,' — Strategy') DisplayName,CONCAT('Policy: ',p.PolicyTitle,CHAR(10),'Strategy: ',x.StatementText) CanonicalText
        FROM dbo.PolicyStrategyStatements x JOIN dbo.PolicySections ps ON ps.PolicySectionId=x.PolicySectionId JOIN dbo.Policies p ON p.PolicyId=ps.PolicyId"""),
    ("PolicyConsequence", """SELECT x.PolicyConsequenceId SourceId,ps.PolicyId ParentId,
        CONCAT(p.PolicyTitle,' — Consequence') DisplayName,CONCAT('Policy: ',p.PolicyTitle,CHAR(10),'Consequence: ',x.StatementText) CanonicalText
        FROM dbo.PolicyConsequences x JOIN dbo.PolicySections ps ON ps.PolicySectionId=x.PolicySectionId JOIN dbo.Policies p ON p.PolicyId=ps.PolicyId"""),
    ("PolicyControlStatement", """SELECT x.PolicyControlStatementId SourceId,x.PolicyId ParentId,
        CONCAT(p.PolicyTitle,' — ',c.DisplayName) DisplayName,
        CONCAT('Policy: ',p.PolicyTitle,CHAR(10),'Control: ',c.DisplayName,CHAR(10),'Statement: ',x.StatementText) CanonicalText
        FROM dbo.PolicyControlStatements x JOIN dbo.Policies p ON p.PolicyId=x.PolicyId JOIN dbo.Controls c ON c.ControlId=x.ControlId"""),
)


def discover_source_artifacts(settings: Settings) -> list[SourceArtifact]:
    artifacts: list[SourceArtifact] = []
    for artifact_type, query in SOURCE_QUERIES:
        for row in fetch_all(settings, query):
            text = str(row.get("CanonicalText") or "").strip()
            if not text:
                continue
            artifacts.append(SourceArtifact(
                artifact_type=artifact_type,
                source_id=str(row["SourceId"]),
                parent_id=str(row["ParentId"]) if row.get("ParentId") else None,
                display_name=str(row.get("DisplayName") or artifact_type),
                canonical_text=text,
            ))
    return artifacts


def synchronize_artifacts(settings: Settings) -> dict[str, int]:
    artifacts = discover_source_artifacts(settings)
    active_keys = {(item.artifact_type, item.source_id) for item in artifacts}
    inserted = updated = unchanged = archived = 0
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute("""UPDATE dbo.SemanticArtifacts SET EmbeddingStatus='Pending',ClaimedAtUtc=NULL,
            NextAttemptAtUtc=NULL,UpdatedAtUtc=SYSUTCDATETIME(),LastError='Recovered stale processing claim.'
            WHERE EmbeddingStatus='Processing' AND ClaimedAtUtc<DATEADD(minute,-15,SYSUTCDATETIME())""")
        for item in artifacts:
            cursor.execute("SELECT SemanticArtifactId,ContentHash FROM dbo.SemanticArtifacts WHERE ArtifactType=? AND SourceRecordId=?",
                           [item.artifact_type, item.source_id])
            existing = cursor.fetchone()
            if existing is None:
                cursor.execute("""INSERT dbo.SemanticArtifacts(ArtifactType,SourceRecordId,SourceParentId,DisplayName,CanonicalText,ContentHash)
                    VALUES(?,?,?,?,?,?)""", [item.artifact_type,item.source_id,item.parent_id,item.display_name,item.canonical_text,item.content_hash])
                inserted += 1
            elif bytes(existing.ContentHash) != item.content_hash:
                cursor.execute("""UPDATE dbo.SemanticArtifacts SET SourceParentId=?,DisplayName=?,CanonicalText=?,ContentHash=?,
                    EmbeddingStatus='Pending',RetryCount=0,LastError=NULL,NextAttemptAtUtc=NULL,UpdatedAtUtc=SYSUTCDATETIME()
                    WHERE SemanticArtifactId=?""", [item.parent_id,item.display_name,item.canonical_text,item.content_hash,existing.SemanticArtifactId])
                updated += 1
            else:
                cursor.execute("""UPDATE dbo.SemanticArtifacts SET SourceParentId=?,DisplayName=?,
                    EmbeddingStatus=CASE WHEN EmbeddingStatus='Archived' THEN 'Pending' ELSE EmbeddingStatus END,
                    UpdatedAtUtc=CASE WHEN EmbeddingStatus='Archived' THEN SYSUTCDATETIME() ELSE UpdatedAtUtc END
                    WHERE SemanticArtifactId=?""", [item.parent_id,item.display_name,existing.SemanticArtifactId])
                unchanged += 1
        cursor.execute("SELECT SemanticArtifactId,ArtifactType,CONVERT(nvarchar(36),SourceRecordId) SourceRecordId FROM dbo.SemanticArtifacts WHERE EmbeddingStatus<>'Archived'")
        for row in cursor.fetchall():
            if (str(row.ArtifactType), str(row.SourceRecordId)) not in active_keys:
                cursor.execute("UPDATE dbo.SemanticArtifacts SET EmbeddingStatus='Archived',UpdatedAtUtc=SYSUTCDATETIME() WHERE SemanticArtifactId=?", [row.SemanticArtifactId])
                archived += 1
        conn.commit()
    return {"inserted": inserted, "updated": updated, "unchanged": unchanged, "archived": archived}


def _claim_batch(settings: Settings, batch_size: int) -> list[dict[str, object]]:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute("""
            ;WITH claim AS (SELECT TOP (?) * FROM dbo.SemanticArtifacts WITH (UPDLOCK,READPAST,ROWLOCK)
                WHERE EmbeddingStatus IN ('Pending','Failed') AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc<=SYSUTCDATETIME())
                ORDER BY CASE EmbeddingStatus WHEN 'Pending' THEN 0 ELSE 1 END,UpdatedAtUtc)
            UPDATE claim SET EmbeddingStatus='Processing',ClaimedAtUtc=SYSUTCDATETIME(),LastError=NULL
            OUTPUT inserted.SemanticArtifactId,inserted.CanonicalText,inserted.ContentHash,inserted.RetryCount;
        """, [batch_size])
        columns = [column[0] for column in cursor.description]
        rows = [dict(zip(columns, row, strict=False)) for row in cursor.fetchall()]
        conn.commit()
        return rows


def _vector_literal(vector: list[float]) -> str:
    return "[" + ",".join(format(float(value), ".9g") for value in vector) + "]"


def _request_embeddings(settings: Settings, texts: list[str], model: str) -> list[list[float]]:
    with httpx.Client(timeout=180.0) as client:
        response = client.post(f"{settings.ollama_base_url}/api/embed", json={"model": model, "input": texts})
        response.raise_for_status()
        vectors = response.json().get("embeddings")
        if not isinstance(vectors, list) or len(vectors) != len(texts):
            raise ValueError("Ollama returned an unexpected embedding batch.")
        return [[float(value) for value in vector] for vector in vectors]


def process_pending_batch(settings: Settings, batch_size: int = 16) -> int:
    profile = fetch_one(settings, """SELECT TOP 1 EmbeddingProfileId,ModelName,VectorDimension FROM dbo.EmbeddingProfiles
        WHERE IsDefault=1 AND Status='Active' ORDER BY UpdatedAtUtc DESC""")
    if profile is None:
        raise RuntimeError("No active default embedding profile is configured.")
    if int(profile["VectorDimension"]) != 768:
        raise RuntimeError("SemanticArtifactEmbeddings768 requires a 768-dimensional embedding profile.")
    items = _claim_batch(settings, batch_size)
    if not items:
        return 0
    try:
        vectors = _request_embeddings(settings, [str(item["CanonicalText"]) for item in items], str(profile["ModelName"]))
        with get_connection(settings) as conn:
            cursor = conn.cursor()
            for item, vector in zip(items, vectors, strict=True):
                cursor.execute("""
                    UPDATE dbo.SemanticArtifactEmbeddings768 SET EmbeddingVector=CAST(CONVERT(nvarchar(max),?) AS VECTOR(768)),EmbeddingHash=?,UpdatedAtUtc=SYSUTCDATETIME()
                    WHERE SemanticArtifactId=? AND EmbeddingProfileId=?;
                    IF @@ROWCOUNT=0 INSERT dbo.SemanticArtifactEmbeddings768(SemanticArtifactId,EmbeddingProfileId,EmbeddingVector,EmbeddingHash)
                    VALUES(?,?,CAST(CONVERT(nvarchar(max),?) AS VECTOR(768)),?);
                    UPDATE dbo.SemanticArtifacts SET EmbeddingStatus='Embedded',RetryCount=0,LastError=NULL,NextAttemptAtUtc=NULL,
                        ClaimedAtUtc=NULL,UpdatedAtUtc=SYSUTCDATETIME() WHERE SemanticArtifactId=?;
                """, [_vector_literal(vector),item["ContentHash"],item["SemanticArtifactId"],profile["EmbeddingProfileId"],
                       item["SemanticArtifactId"],profile["EmbeddingProfileId"],_vector_literal(vector),item["ContentHash"],item["SemanticArtifactId"]])
            conn.commit()
    except Exception as exc:
        with get_connection(settings) as conn:
            cursor = conn.cursor()
            for item in items:
                retry = int(item["RetryCount"] or 0) + 1
                cursor.execute("""UPDATE dbo.SemanticArtifacts SET EmbeddingStatus='Failed',RetryCount=?,LastError=?,
                    NextAttemptAtUtc=DATEADD(second,?,SYSUTCDATETIME()),ClaimedAtUtc=NULL,UpdatedAtUtc=SYSUTCDATETIME()
                    WHERE SemanticArtifactId=?""", [retry,str(exc)[:2000],min(3600,30*(2**min(retry,7))),item["SemanticArtifactId"]])
            conn.commit()
        raise
    return len(items)


def get_semantic_embedding_status(settings: Settings) -> dict[str, object]:
    rows = fetch_all(settings, "SELECT EmbeddingStatus,COUNT(*) Count FROM dbo.SemanticArtifacts GROUP BY EmbeddingStatus")
    by_status = {str(row["EmbeddingStatus"]).lower(): int(row["Count"]) for row in rows}
    types = fetch_all(settings, "SELECT ArtifactType,COUNT(*) Total,SUM(CASE WHEN EmbeddingStatus='Embedded' THEN 1 ELSE 0 END) Embedded FROM dbo.SemanticArtifacts WHERE EmbeddingStatus<>'Archived' GROUP BY ArtifactType ORDER BY ArtifactType")
    return {"total": sum(by_status.values()), "byStatus": by_status, "artifactTypes": types}


def queue_semantic_embeddings(settings: Settings, mode: str) -> dict[str, object]:
    normalized = mode.strip().lower()
    if normalized not in {"pending", "retry", "rebuild"}:
        raise ValueError("Mode must be pending, retry, or rebuild.")
    sync = synchronize_artifacts(settings)
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        if normalized == "retry":
            cursor.execute("UPDATE dbo.SemanticArtifacts SET EmbeddingStatus='Pending',NextAttemptAtUtc=NULL,LastError=NULL WHERE EmbeddingStatus='Failed'")
        elif normalized == "rebuild":
            cursor.execute("UPDATE dbo.SemanticArtifacts SET EmbeddingStatus='Pending',RetryCount=0,NextAttemptAtUtc=NULL,LastError=NULL WHERE EmbeddingStatus<>'Archived'")
        queued = cursor.rowcount if normalized != "pending" else 0
        conn.commit()
    return {"mode": normalized, "queued": max(0, queued), "synchronized": sync, "status": get_semantic_embedding_status(settings)}


def run_worker(*, once: bool = False, poll_seconds: int = 15, batch_size: int = 16) -> int:
    settings = get_settings()
    lock_connection = get_connection(settings)
    lock_cursor = lock_connection.cursor()
    lock_cursor.execute("DECLARE @r int; EXEC @r=sp_getapplock @Resource='DomainLinksSemanticEmbeddingWorker',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @r")
    if int(lock_cursor.fetchone()[0]) < 0:
        print("Another semantic embedding worker is already running.")
        lock_connection.close()
        return 0
    try:
        while True:
            synchronize_artifacts(settings)
            while True:
                try:
                    processed = process_pending_batch(settings, batch_size)
                except Exception as exc:
                    print(f"Embedding batch failed: {exc}")
                    processed = 0
                if processed == 0:
                    break
            if once:
                return 0
            time.sleep(max(5, poll_seconds))
    finally:
        lock_connection.close()


def main() -> int:
    parser = argparse.ArgumentParser(description="Synchronize and embed DomainLinks semantic artifacts.")
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--poll-seconds", type=int, default=15)
    parser.add_argument("--batch-size", type=int, default=16)
    args = parser.parse_args()
    return run_worker(once=args.once, poll_seconds=args.poll_seconds, batch_size=args.batch_size)


if __name__ == "__main__":
    raise SystemExit(main())
