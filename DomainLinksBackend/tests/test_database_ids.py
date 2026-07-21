from app.db import normalize_database_record


def test_entity_ids_are_strings_at_api_boundary() -> None:
    result = normalize_database_record(
        {
            "DomainId": 42,
            "DomainParentId": None,
            "DomainTypeId": 3,
            "DisplayOrder": 7,
        }
    )

    assert result == {
        "DomainId": "42",
        "DomainParentId": None,
        "DomainTypeId": 3,
        "DisplayOrder": 7,
    }
