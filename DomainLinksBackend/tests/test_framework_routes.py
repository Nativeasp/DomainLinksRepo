from fastapi.testclient import TestClient

from app import main as main_module
from app.frameworks import format_framework_context


def test_framework_endpoint_returns_requested_framework(monkeypatch) -> None:
    def fake_get(_settings: object, code: str, version: str | None = None) -> dict[str, object]:
        return {"framework": {"FrameworkCode": code, "VersionText": version or "1.0"}}

    monkeypatch.setattr(main_module, "get_framework", fake_get)
    client = TestClient(main_module.create_app())

    response = client.get("/frameworks/MANDATE-CAPABILITY-STRATEGY")

    assert response.status_code == 200
    assert response.json()["framework"]["FrameworkCode"] == "MANDATE-CAPABILITY-STRATEGY"


def test_framework_context_endpoint_passes_resolution_scope(monkeypatch) -> None:
    captured: dict[str, object] = {}

    def fake_resolve(_settings: object, **kwargs: object) -> dict[str, object]:
        captured.update(kwargs)
        return {"frameworkCode": kwargs["framework_code"], "elements": []}

    monkeypatch.setattr(main_module, "resolve_framework_context", fake_resolve)
    client = TestClient(main_module.create_app())

    response = client.get(
        "/frameworks/MANDATE-CAPABILITY-STRATEGY/context",
        params={"activityType": "PolicyDevelopment", "artifactType": "Policy"},
    )

    assert response.status_code == 200
    assert captured == {
        "framework_code": "MANDATE-CAPABILITY-STRATEGY",
        "activity_type": "PolicyDevelopment",
        "artifact_type": "Policy",
        "source_record_id": None,
        "delivery_mode": "AIContext",
    }


def test_framework_context_format_preserves_authority_and_scope_warning() -> None:
    text = format_framework_context(
        {
            "frameworkName": "Mandate-Capability-Strategy Framework",
            "versionText": "1.0",
            "elements": [
                {
                    "elementName": "Capability",
                    "elementCode": "CAPABILITY",
                    "statementText": None,
                    "definitionText": None,
                    "instructionText": "Assess required capability.",
                    "principles": [
                        {
                            "statementText": "Capability defines readiness.",
                            "shortStatementText": "Capability defines the state of readiness.",
                        }
                    ],
                }
            ],
        }
    )

    assert "Authoritative organizational framework context" in text
    assert "Capability defines readiness." in text
    assert "Do not invent a specific mandate" in text
