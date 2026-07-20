from fastapi.testclient import TestClient

from app import main as main_module


def test_brain_graph_uses_information_management_defaults(monkeypatch) -> None:
    captured: dict[str, object] = {}

    def fake_build(_settings: object, **kwargs: object) -> dict[str, object]:
        captured.update(kwargs)
        return {"nodes": [], "edges": [], "summary": {"nodeCount": 0}}

    monkeypatch.setattr(main_module, "build_brain_graph", fake_build)
    client = TestClient(main_module.create_app())

    response = client.get("/brain/graph")

    assert response.status_code == 200
    assert captured == {
        "scope_kind": "domain",
        "scope_id": "information-management",
        "include_descendants": True,
        "max_nodes": 2000,
    }


def test_brain_graph_returns_not_found(monkeypatch) -> None:
    def fake_build(_settings: object, **_kwargs: object) -> dict[str, object]:
        raise LookupError("Active document scope was not found.")

    monkeypatch.setattr(main_module, "build_brain_graph", fake_build)
    client = TestClient(main_module.create_app())

    response = client.get("/brain/graph?scopeKind=document&scopeId=missing")

    assert response.status_code == 404
    assert response.json()["detail"] == "Active document scope was not found."


def test_brain_document_expansion_returns_not_found(monkeypatch) -> None:
    def fake_expand(_settings: object, _document_id: str) -> dict[str, object]:
        raise LookupError("Active document was not found.")

    monkeypatch.setattr(main_module, "expand_document", fake_expand)
    client = TestClient(main_module.create_app())

    response = client.get("/brain/documents/missing/content-units")

    assert response.status_code == 404
