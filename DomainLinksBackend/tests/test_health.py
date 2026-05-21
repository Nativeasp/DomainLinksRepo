from fastapi.testclient import TestClient

import app.main as main_module
from app.main import app, create_app


def test_health_returns_ok() -> None:
    client = TestClient(app)
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_config_returns_public_settings() -> None:
    client = TestClient(app)
    response = client.get("/config")

    assert response.status_code == 200
    assert response.json()["default_llm_provider"] == "ollama"


def test_domains_endpoint_returns_list() -> None:
    def fake_list_domains(_settings: object) -> list[dict[str, object]]:
        return [{"DomainCode": "demo"}]

    main_module.list_domains = fake_list_domains
    client = TestClient(create_app())
    response = client.get("/domains")

    assert response.status_code == 200
    assert response.json() == [{"DomainCode": "demo"}]


def test_collections_endpoint_returns_list() -> None:
    def fake_list_collections(
        _settings: object,
        domain_code: str | None = None,
    ) -> list[dict[str, object]]:
        return [{"CollectionCode": "notes", "DomainCode": domain_code}]

    main_module.list_collections = fake_list_collections
    client = TestClient(create_app())
    response = client.get("/collections", params={"domainCode": "demo"})

    assert response.status_code == 200
    assert response.json() == [{"CollectionCode": "notes", "DomainCode": "demo"}]


def test_retrieval_profiles_endpoint_returns_list() -> None:
    def fake_list_retrieval_profiles(_settings: object) -> list[dict[str, object]]:
        return [{"ProfileCode": "default"}]

    main_module.list_retrieval_profiles = fake_list_retrieval_profiles
    client = TestClient(create_app())
    response = client.get("/retrieval-profiles")

    assert response.status_code == 200
    assert response.json() == [{"ProfileCode": "default"}]


def test_health_reports_database_error_when_driver_missing() -> None:
    def fake_ping_database(_settings: object) -> dict[str, object]:
        raise RuntimeError("pyodbc is not installed")

    main_module.ping_database = fake_ping_database
    client = TestClient(create_app())
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json()["database"] == {
        "reachable": False,
        "error": "pyodbc is not installed",
    }
