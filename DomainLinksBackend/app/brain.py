from __future__ import annotations

from collections import defaultdict
from typing import Any

from .config import Settings
from .db import fetch_all, fetch_one


DEFAULT_DOMAIN_CODE = "information-management"
MAX_GRAPH_NODES = 2000
SEMANTIC_NEIGHBOURS = 5
MIN_SEMANTIC_SIMILARITY = 0.75
MIN_CONTROL_SEMANTIC_SIMILARITY = 0.55
MIN_GOVERNANCE_SEMANTIC_SIMILARITY = 0.50


def _text(value: object) -> str:
    return "" if value is None else str(value)


def _node_id(kind: str, value: object) -> str:
    return f"{kind}:{_text(value)}"


def _gap(code: str, message: str) -> dict[str, str]:
    return {"code": code, "message": message}


def _resolve_scope(settings: Settings, scope_kind: str, scope_id: str) -> dict[str, str]:
    kind = (scope_kind or "domain").strip().lower()
    identifier = (scope_id or DEFAULT_DOMAIN_CODE).strip()
    queries: dict[str, tuple[str, list[object]]] = {
        "domain": (
            """
            SELECT TOP 1 DomainCode, DomainId, 'domain' AS FocusKind, DomainId AS FocusId
            FROM dbo.Domains
            WHERE Status = 'Active' AND (DomainCode = ? OR CONVERT(nvarchar(36), DomainId) = ?)
            """,
            [identifier, identifier],
        ),
        "collection": (
            """
            SELECT TOP 1 d.DomainCode, d.DomainId, 'collection' AS FocusKind, c.CollectionId AS FocusId
            FROM dbo.Collections c JOIN dbo.Domains d ON d.DomainId = c.DomainId
            WHERE c.Status = 'Active' AND d.Status = 'Active'
              AND (c.CollectionCode = ? OR CONVERT(nvarchar(36), c.CollectionId) = ?)
            """,
            [identifier, identifier],
        ),
        "document": (
            """
            SELECT TOP 1 dm.DomainCode, dm.DomainId, 'document' AS FocusKind, d.DocumentId AS FocusId
            FROM dbo.Documents d
            JOIN dbo.Collections c ON c.CollectionId = d.CollectionId
            JOIN dbo.Domains dm ON dm.DomainId = c.DomainId
            WHERE d.Status = 'Active' AND c.Status = 'Active' AND dm.Status = 'Active'
              AND CONVERT(nvarchar(36), d.DocumentId) = ?
            """,
            [identifier],
        ),
        "policy": (
            """
            SELECT TOP 1 d.DomainCode, d.DomainId, 'policy' AS FocusKind, p.PolicyId AS FocusId
            FROM dbo.Policies p JOIN dbo.Domains d ON d.DomainId = p.RootDomainId
            WHERE d.Status = 'Active' AND CONVERT(nvarchar(36), p.PolicyId) = ?
            """,
            [identifier],
        ),
        "control": (
            """
            SELECT TOP 1 d.DomainCode, d.DomainId, 'control' AS FocusKind, c.ControlId AS FocusId
            FROM dbo.Controls c
            JOIN dbo.DomainControls dc ON dc.ControlId = c.ControlId
            JOIN dbo.Domains d ON d.DomainId = dc.DomainId
            WHERE c.Status = 'Active'
              AND (c.ControlCode = ? OR CONVERT(nvarchar(36), c.ControlId) = ?)
            ORDER BY d.DisplayOrder, d.DisplayName
            """,
            [identifier, identifier],
        ),
    }
    if kind not in queries:
        raise ValueError(f"Unsupported Brain scope kind '{scope_kind}'.")
    query, params = queries[kind]
    row = fetch_one(settings, query, params)
    if row is None:
        raise LookupError(f"Active {kind} scope '{identifier}' was not found.")
    return {
        "domainCode": _text(row["DomainCode"]),
        "domainId": _text(row["DomainId"]),
        "focusKind": _text(row["FocusKind"]),
        "focusId": _text(row["FocusId"]),
    }


def build_brain_graph(
    settings: Settings,
    *,
    scope_kind: str = "domain",
    scope_id: str = DEFAULT_DOMAIN_CODE,
    include_descendants: bool = True,
    max_nodes: int = MAX_GRAPH_NODES,
) -> dict[str, Any]:
    resolved = _resolve_scope(settings, scope_kind, scope_id)
    safe_limit = max(50, min(max_nodes, MAX_GRAPH_NODES))
    depth_clause = "" if include_descendants else "AND d.DomainId = tree.DomainId AND 1 = 0"
    domains = fetch_all(
        settings,
        f"""
        ;WITH tree AS (
            SELECT DomainId, DomainParentId, DomainCode, DisplayName, Description, DisplayOrder, 0 AS Depth
            FROM dbo.Domains WHERE DomainCode = ? AND Status = 'Active'
            UNION ALL
            SELECT d.DomainId, d.DomainParentId, d.DomainCode, d.DisplayName, d.Description,
                   d.DisplayOrder, tree.Depth + 1
            FROM dbo.Domains d JOIN tree ON d.DomainParentId = tree.DomainId
            WHERE d.Status = 'Active' {depth_clause}
        )
        SELECT tree.*,
               (SELECT COUNT(*) FROM dbo.Collections c WHERE c.DomainId=tree.DomainId AND c.Status='Active') CollectionCount,
               (SELECT COUNT(*) FROM dbo.Documents doc JOIN dbo.Collections c ON c.CollectionId=doc.CollectionId
                WHERE c.DomainId=tree.DomainId AND c.Status='Active' AND doc.Status='Active') DocumentCount,
               (SELECT COUNT(*) FROM dbo.DomainControls dc WHERE dc.DomainId=tree.DomainId) ControlCount,
               (SELECT COUNT(*) FROM dbo.Policies p WHERE p.RootDomainId=tree.DomainId) PolicyCount
        FROM tree ORDER BY Depth, DisplayOrder, DisplayName
        """,
        [resolved["domainCode"]],
    )
    domain_ids = {_text(row["DomainId"]) for row in domains}
    if not domains:
        raise LookupError(f"Domain branch '{resolved['domainCode']}' was not found.")

    branch_code = resolved["domainCode"]
    collections = fetch_all(
        settings,
        """
        ;WITH tree AS (
            SELECT DomainId FROM dbo.Domains WHERE DomainCode=? AND Status='Active'
            UNION ALL SELECT d.DomainId FROM dbo.Domains d JOIN tree t ON d.DomainParentId=t.DomainId
            WHERE d.Status='Active'
        )
        SELECT c.CollectionId,c.CollectionCode,c.DisplayName,c.Description,c.DomainId,
               (SELECT COUNT(*) FROM dbo.Documents d WHERE d.CollectionId=c.CollectionId AND d.Status='Active') DocumentCount
        FROM dbo.Collections c JOIN tree t ON t.DomainId=c.DomainId
        WHERE c.Status='Active' ORDER BY c.DisplayName
        """,
        [branch_code],
    )
    documents = fetch_all(
        settings,
        """
        ;WITH tree AS (
            SELECT DomainId FROM dbo.Domains WHERE DomainCode=? AND Status='Active'
            UNION ALL SELECT d.DomainId FROM dbo.Domains d JOIN tree t ON d.DomainParentId=t.DomainId
            WHERE d.Status='Active'
        )
        SELECT d.DocumentId,d.SourceName,d.SourceType,d.CreatedAtUtc,d.UpdatedAtUtc,c.CollectionId,
               COUNT(DISTINCT cu.ContentUnitId) ContentUnitCount,
               COUNT(DISTINCT emb.ContentUnitId) EmbeddedContentUnitCount
        FROM dbo.Documents d JOIN dbo.Collections c ON c.CollectionId=d.CollectionId
        JOIN tree t ON t.DomainId=c.DomainId
        LEFT JOIN dbo.ContentUnits cu ON cu.DocumentId=d.DocumentId AND cu.Status='Active'
        LEFT JOIN dbo.ContentUnitEmbeddings768 emb ON emb.ContentUnitId=cu.ContentUnitId
        WHERE d.Status='Active' AND c.Status='Active'
        GROUP BY d.DocumentId,d.SourceName,d.SourceType,d.CreatedAtUtc,d.UpdatedAtUtc,c.CollectionId
        ORDER BY d.UpdatedAtUtc DESC
        """,
        [branch_code],
    )
    controls = fetch_all(
        settings,
        """
        ;WITH tree AS (
            SELECT DomainId FROM dbo.Domains WHERE DomainCode=? AND Status='Active'
            UNION ALL SELECT d.DomainId FROM dbo.Domains d JOIN tree t ON d.DomainParentId=t.DomainId
            WHERE d.Status='Active'
        )
        SELECT DISTINCT c.ControlId,c.ControlCode,c.DisplayName AS ControlName,c.Description AS ControlDescription,dc.DomainId
        FROM dbo.Controls c JOIN dbo.DomainControls dc ON dc.ControlId=c.ControlId
        JOIN tree t ON t.DomainId=dc.DomainId WHERE c.Status='Active'
        """,
        [branch_code],
    )
    policies = fetch_all(
        settings,
        """
        ;WITH tree AS (
            SELECT DomainId FROM dbo.Domains WHERE DomainCode=? AND Status='Active'
            UNION ALL SELECT d.DomainId FROM dbo.Domains d JOIN tree t ON d.DomainParentId=t.DomainId
            WHERE d.Status='Active'
        )
        SELECT p.PolicyId,p.PolicyCode,p.PolicyTitle,p.Status,p.RootDomainId
        FROM dbo.Policies p JOIN tree t ON t.DomainId=p.RootDomainId
        """,
        [branch_code],
    )

    nodes: list[dict[str, Any]] = []
    edges: list[dict[str, Any]] = []
    degree: defaultdict[str, int] = defaultdict(int)

    def add_edge(kind: str, source: str, target: str, **extra: object) -> None:
        edge = {"id": f"{kind}:{source}:{target}", "type": kind, "source": source, "target": target, **extra}
        edges.append(edge)
        degree[source] += 1
        degree[target] += 1

    for row in domains:
        node_id = _node_id("domain", row["DomainId"])
        gaps: list[dict[str, str]] = []
        if int(row["CollectionCount"] or 0) == 0:
            gaps.append(_gap("domain-no-collections", "No active knowledge collections are assigned to this domain."))
        if int(row["DocumentCount"] or 0) == 0:
            gaps.append(_gap("domain-no-documents", "No active documents provide evidence for this domain."))
        if int(row["ControlCount"] or 0) == 0:
            gaps.append(_gap("domain-no-controls", "No controls are assigned to this domain."))
        if int(row["PolicyCount"] or 0) == 0:
            gaps.append(_gap("domain-no-policy", "No policy is rooted in this domain."))
        nodes.append({
            "id": node_id, "type": "domain", "label": _text(row["DisplayName"]),
            "description": _text(row["Description"]), "domainCode": _text(row["DomainCode"]),
            "scopeDepth": int(row["Depth"] or 0), "isScopeDomain": True,
            "gaps": gaps, "expandable": False,
        })
        parent_id = _text(row["DomainParentId"])
        if parent_id in domain_ids:
            add_edge("hierarchy", _node_id("domain", parent_id), node_id)

    for row in collections:
        node_id = _node_id("collection", row["CollectionId"])
        collection_gaps = [] if int(row["DocumentCount"] or 0) > 0 else [
            _gap("collection-no-documents", "This collection contains no active documents.")
        ]
        nodes.append({"id": node_id, "type": "collection", "label": _text(row["DisplayName"]),
                      "description": _text(row["Description"]), "collectionCode": _text(row["CollectionCode"]),
                      "gaps": collection_gaps, "expandable": False})
        add_edge("contains", _node_id("domain", row["DomainId"]), node_id)

    for row in documents:
        count = int(row["ContentUnitCount"] or 0)
        embedded = int(row["EmbeddedContentUnitCount"] or 0)
        gaps = []
        if count == 0:
            gaps.append(_gap("document-no-content", "The document has no active content units."))
        elif embedded < count:
            gaps.append(_gap("document-missing-embeddings", f"{count - embedded} of {count} content units lack embeddings."))
        node_id = _node_id("document", row["DocumentId"])
        nodes.append({"id": node_id, "type": "document", "label": _text(row["SourceName"]),
                      "description": _text(row["SourceType"]), "contentUnitCount": count,
                      "embeddedContentUnitCount": embedded, "gaps": gaps, "expandable": count > 0})
        add_edge("contains", _node_id("collection", row["CollectionId"]), node_id)

    seen_controls: set[str] = set()
    for row in controls:
        node_id = _node_id("control", row["ControlId"])
        if node_id not in seen_controls:
            nodes.append({"id": node_id, "type": "control", "label": _text(row["ControlName"]),
                          "description": _text(row["ControlDescription"]), "controlCode": _text(row["ControlCode"]),
                          "gaps": [], "expandable": False})
            seen_controls.add(node_id)
        add_edge("governs", _node_id("domain", row["DomainId"]), node_id)

    for row in policies:
        node_id = _node_id("policy", row["PolicyId"])
        nodes.append({"id": node_id, "type": "policy", "label": _text(row["PolicyTitle"]),
                      "description": _text(row["Status"]), "policyCode": _text(row["PolicyCode"]),
                      "gaps": [], "expandable": False})
        add_edge("governs", _node_id("domain", row["RootDomainId"]), node_id)

    policy_control_links = fetch_all(
        settings,
        """
        ;WITH tree AS (
            SELECT DomainId FROM dbo.Domains WHERE DomainCode=? AND Status='Active'
            UNION ALL SELECT d.DomainId FROM dbo.Domains d JOIN tree t ON d.DomainParentId=t.DomainId WHERE d.Status='Active'
        )
        SELECT DISTINCT pcs.PolicyId,pcs.ControlId
        FROM dbo.PolicyControlStatements pcs
        JOIN dbo.Policies p ON p.PolicyId=pcs.PolicyId
        JOIN tree t ON t.DomainId=p.RootDomainId
        """,
        [branch_code],
    )
    available_controls = {_node_id("control", row["ControlId"]) for row in controls}
    for row in policy_control_links:
        control_id = _node_id("control", row["ControlId"])
        if control_id in available_controls:
            add_edge("policy-control", _node_id("policy", row["PolicyId"]), control_id)

    semantic_rows = fetch_all(
        settings,
        """
        ;WITH tree AS (
            SELECT DomainId FROM dbo.Domains WHERE DomainCode=? AND Status='Active'
            UNION ALL SELECT d.DomainId FROM dbo.Domains d JOIN tree t ON d.DomainParentId=t.DomainId WHERE d.Status='Active'
        ), pairs AS (
            SELECT a.DocumentId SourceDocumentId,b.DocumentId TargetDocumentId,
                   MIN(CAST(VECTOR_DISTANCE('cosine', ea.EmbeddingVector, eb.EmbeddingVector) AS float)) Distance
            FROM dbo.ContentUnits a JOIN dbo.ContentUnitEmbeddings768 ea ON ea.ContentUnitId=a.ContentUnitId
            JOIN dbo.Documents da ON da.DocumentId=a.DocumentId
            JOIN dbo.Collections ca ON ca.CollectionId=da.CollectionId JOIN tree ta ON ta.DomainId=ca.DomainId
            JOIN dbo.ContentUnits b ON b.DocumentId>a.DocumentId
            JOIN dbo.ContentUnitEmbeddings768 eb ON eb.ContentUnitId=b.ContentUnitId AND eb.EmbeddingProfileId=ea.EmbeddingProfileId
            JOIN dbo.Documents db ON db.DocumentId=b.DocumentId
            JOIN dbo.Collections cb ON cb.CollectionId=db.CollectionId JOIN tree tb ON tb.DomainId=cb.DomainId
            WHERE a.Status='Active' AND b.Status='Active' AND da.Status='Active' AND db.Status='Active'
            GROUP BY a.DocumentId,b.DocumentId
        ), ranked AS (
            SELECT *,ROW_NUMBER() OVER(PARTITION BY SourceDocumentId ORDER BY Distance) RankNumber FROM pairs
            WHERE Distance <= ?
        )
        SELECT SourceDocumentId,TargetDocumentId,Distance FROM ranked WHERE RankNumber<=?
        """,
        [branch_code, 1.0 - MIN_SEMANTIC_SIMILARITY, SEMANTIC_NEIGHBOURS],
    )
    for row in semantic_rows:
        similarity = round(1.0 - float(row["Distance"]), 4)
        add_edge("semantic", _node_id("document", row["SourceDocumentId"]),
                 _node_id("document", row["TargetDocumentId"]), similarity=similarity)

    artifact_rows = fetch_all(
        settings,
        """
        ;WITH tree AS (
            SELECT DomainId FROM dbo.Domains WHERE DomainCode=? AND Status='Active'
            UNION ALL SELECT d.DomainId FROM dbo.Domains d JOIN tree t ON d.DomainParentId=t.DomainId WHERE d.Status='Active'
        ), source_artifacts AS (
            SELECT sa.SemanticArtifactId,sa.SourceRecordId,e.EmbeddingVector,e.EmbeddingProfileId
            FROM dbo.SemanticArtifacts sa
            JOIN dbo.SemanticArtifactEmbeddings768 e ON e.SemanticArtifactId=sa.SemanticArtifactId
            JOIN tree t ON t.DomainId=sa.SourceRecordId
            WHERE sa.ArtifactType='Domain' AND sa.EmbeddingStatus='Embedded'
        ), candidates AS (
            SELECT src.SourceRecordId SourceDomainId,target.ArtifactType,target.SourceRecordId,target.SourceParentId,
                   target.DisplayName,target.CanonicalText,
                   CAST(VECTOR_DISTANCE('cosine',src.EmbeddingVector,te.EmbeddingVector) AS float) Distance
            FROM source_artifacts src
            JOIN dbo.SemanticArtifactEmbeddings768 te ON te.EmbeddingProfileId=src.EmbeddingProfileId
            JOIN dbo.SemanticArtifacts target ON target.SemanticArtifactId=te.SemanticArtifactId
            WHERE target.EmbeddingStatus='Embedded' AND target.SemanticArtifactId<>src.SemanticArtifactId
        ), ranked AS (
            SELECT *,ROW_NUMBER() OVER(PARTITION BY SourceDomainId,ArtifactType ORDER BY Distance) RankNumber
            FROM candidates
            WHERE (ArtifactType='Domain' AND Distance<=?)
               OR (ArtifactType='Control' AND Distance<=?)
               OR (ArtifactType NOT IN ('Domain','Control') AND Distance<=?)
        )
        SELECT * FROM ranked WHERE RankNumber<=?
        """,
        [branch_code, 1.0 - MIN_SEMANTIC_SIMILARITY, 1.0 - MIN_CONTROL_SEMANTIC_SIMILARITY,
         1.0 - MIN_GOVERNANCE_SEMANTIC_SIMILARITY, SEMANTIC_NEIGHBOURS],
    )
    known_node_ids = {node["id"] for node in nodes}
    artifact_kind_map = {"Domain": "domain", "Control": "control", "Policy": "policy"}
    for row in artifact_rows:
        target_kind = artifact_kind_map.get(_text(row["ArtifactType"]), "policy-statement")
        target_id = _node_id(target_kind, row["SourceRecordId"])
        if target_id not in known_node_ids:
            nodes.append({
                "id": target_id,
                "type": target_kind,
                "label": _text(row["DisplayName"]),
                "description": _text(row["CanonicalText"])[:1000],
                "artifactType": _text(row["ArtifactType"]),
                "gaps": [],
                "expandable": False,
                "semanticDiscovery": True,
            })
            known_node_ids.add(target_id)
        source_id = _node_id("domain", row["SourceDomainId"])
        similarity = round(1.0 - float(row["Distance"]), 4)
        edge_key = f"semantic:{source_id}:{target_id}"
        if source_id != target_id and not any(edge["id"] == edge_key for edge in edges):
            add_edge("semantic", source_id, target_id, similarity=similarity)

    total_before_limit = len(nodes)
    nodes = nodes[:safe_limit]
    included = {node["id"] for node in nodes}
    edges = [edge for edge in edges if edge["source"] in included and edge["target"] in included]
    max_degree = max((degree[node_id] for node_id in included), default=0)
    for node in nodes:
        node_degree = degree[node["id"]]
        ratio = node_degree / max_degree if max_degree else 0
        node["density"] = "dense" if ratio >= 0.67 else "normal" if ratio >= 0.34 else "sparse"
        if node_degree == 0:
            node["gaps"].append(_gap("isolated-node", "No visible relationships connect this node in the current scope."))

    focus_id = _node_id(resolved["focusKind"], resolved["focusId"])
    return {
        "scope": {"kind": scope_kind.lower(), "id": scope_id, "domainCode": branch_code},
        "focusNodeId": focus_id if focus_id in included else _node_id("domain", resolved["domainId"]),
        "nodes": nodes, "edges": edges,
        "summary": {"nodeCount": len(nodes), "edgeCount": len(edges), "totalNodeCount": total_before_limit,
                    "isTruncated": total_before_limit > len(nodes)},
        "filters": {"nodeTypes": sorted({node["type"] for node in nodes}),
                    "domains": [{"code": _text(row["DomainCode"]), "label": _text(row["DisplayName"])} for row in domains]},
    }


def expand_document(settings: Settings, document_id: str) -> dict[str, Any]:
    document = fetch_one(settings, "SELECT DocumentId,SourceName FROM dbo.Documents WHERE Status='Active' AND CONVERT(nvarchar(36),DocumentId)=?", [document_id])
    if document is None:
        raise LookupError(f"Active document '{document_id}' was not found.")
    rows = fetch_all(
        settings,
        """
        SELECT cu.ContentUnitId,cu.UnitType,cu.UnitOrdinal,cu.Heading,cu.BodyText,cu.TokenCount,
               CASE WHEN emb.ContentUnitId IS NULL THEN 0 ELSE 1 END IsEmbedded
        FROM dbo.ContentUnits cu
        LEFT JOIN dbo.ContentUnitEmbeddings768 emb ON emb.ContentUnitId=cu.ContentUnitId
        WHERE cu.DocumentId=? AND cu.Status='Active' ORDER BY cu.UnitOrdinal
        """,
        [document_id],
    )
    nodes = []
    edges = []
    for row in rows:
        node_id = _node_id("content-unit", row["ContentUnitId"])
        embedded = bool(row["IsEmbedded"])
        nodes.append({"id": node_id, "type": "content-unit",
                      "label": _text(row["Heading"]) or f"{_text(row['UnitType'])} {row['UnitOrdinal']}",
                      "description": _text(row["BodyText"])[:1000], "tokenCount": row["TokenCount"],
                      "gaps": [] if embedded else [_gap("content-unit-no-embedding", "This content unit has no embedding.")],
                      "density": "sparse", "expandable": False})
        edges.append({"id": f"contains:document:{document_id}:{node_id}", "type": "contains",
                      "source": _node_id("document", document_id), "target": node_id})
    return {"documentId": document_id, "nodes": nodes, "edges": edges}
