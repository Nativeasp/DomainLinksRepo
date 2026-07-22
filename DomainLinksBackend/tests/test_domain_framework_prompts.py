from app import main as main_module


DOMAIN_CONTEXT = {
    "domain": {
        "DomainId": 86,
        "DomainCode": "community-infrastructure",
        "DisplayName": "Community Infrastructure",
        "DomainType": "Service",
        "Description": "Community infrastructure services.",
    },
    "parentPath": "Services",
    "childDomains": [],
    "collections": [],
    "documents": [],
    "controls": [],
    "policies": [],
}


DOMAIN_TYPES = [{"CODE": "SERVICE", "NAME": "Service"}]


def test_domain_assist_prompt_makes_description_downstream_scope() -> None:
    system_prompt, user_prompt = main_module._build_domain_assist_prompt_parts(
        DOMAIN_CONTEXT,
        "Rewrite the description.",
        None,
    )

    assert "child domains, collections, controls, policies" in system_prompt
    assert "organizational mandates" in system_prompt
    assert "Never use meta-commentary" in system_prompt
    assert "Style example only" in system_prompt
    assert "directing those capabilities toward Community Infrastructure priorities" in system_prompt
    assert "Child domains:\nNone" in user_prompt
    assert "Collections in this domain:\nNone" in user_prompt
    assert "Controls in this domain:\nNone" in user_prompt
    assert "Policies rooted in this domain:\nNone" in user_prompt


def test_child_domain_prompt_requires_substantive_framework_application() -> None:
    system_prompt, _ = main_module._build_child_domain_suggestion_prompt_parts(
        DOMAIN_CONTEXT,
        "Suggest an environmental service.",
        None,
        DOMAIN_TYPES,
    )

    assert "durable business scope" in system_prompt
    assert "Apply Mandate, Capability, and Strategy through the substance" in system_prompt
    assert "Content addresses" in system_prompt
    assert "Style example only" in system_prompt


def test_root_domain_prompt_requires_substantive_framework_application() -> None:
    system_prompt, _ = main_module._build_root_domain_suggestion_prompt_parts(
        "SERVICE",
        "Suggest a service domain.",
        None,
        DOMAIN_TYPES,
        [],
    )

    assert "durable business scope" in system_prompt
    assert "Apply Mandate, Capability, and Strategy through the substance" in system_prompt
    assert "Style example only" in system_prompt
