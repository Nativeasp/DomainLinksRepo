# PRD -- DomainLinks Brain

**Date:** July 18, 2026

## Overview

DomainLinks Brain is a visual intelligence layer for the DomainLinksAI /
AriaAI platform. Rather than presenting the organization's knowledge
solely through search and chat, it provides an interactive "brain" that
visualizes relationships, knowledge density, domain maturity, and
organizational gaps.

The objective is to transform the RAG knowledge base into a navigable,
living knowledge model that supports governance, architecture, policy
development, and strategic planning.

------------------------------------------------------------------------

# Objectives

-   Visualize semantic relationships between documents, controls,
    policies, procedures, standards, and records.
-   Display the current maturity of each organizational domain.
-   Reveal knowledge gaps and undocumented areas.
-   Support debugging and validation of the vector database.
-   Provide a compelling visual interface for exploring organizational
    knowledge.

------------------------------------------------------------------------

# Core Concept

Every embedded document becomes one or more nodes in a semantic graph.

Relationships between vectors become edges.

Clusters naturally emerge based on similarity.

The visualization behaves like a living brain that grows as additional
knowledge is embedded.

------------------------------------------------------------------------

# Functional Requirements

## Interactive Brain Canvas

-   Zoom and pan infinitely.
-   Thousands of nodes.
-   Force-directed layout.
-   Smooth animation.
-   Search and center on any node.
-   Display metadata when selected.

------------------------------------------------------------------------

## Domain Layer

Each node belongs to one or more domains.

Examples:

-   Governance
-   Human Resources
-   Finance
-   Accounting
-   Information Management
-   IT
-   Community Services
-   Health
-   Education
-   Justice
-   Strategic Planning

Filtering should allow:

-   single domain
-   multiple domains
-   hide/show domains

------------------------------------------------------------------------

## Knowledge Density

Heat map showing:

-   dense knowledge
-   sparse knowledge
-   isolated nodes

Useful for identifying underdeveloped areas.

------------------------------------------------------------------------

## Gap Layer

One of the primary capabilities.

Overlay missing information such as:

-   missing controls
-   missing policies
-   missing procedures
-   missing standards
-   missing records
-   missing evidence
-   missing relationships
-   missing subject experts

The graph should visually expose incomplete organizational capability.

------------------------------------------------------------------------

## Maturity Layer

Each domain receives a maturity score.

Possible levels:

-   Not Started
-   Emerging
-   Developing
-   Established
-   Optimized

Scores derived from available documentation and governance components.

------------------------------------------------------------------------

## Relationship Explorer

Selecting a node highlights:

-   nearest semantic neighbours
-   references
-   citations
-   dependent controls
-   downstream policies
-   linked procedures

------------------------------------------------------------------------

## Timeline Mode

Animate the evolution of the knowledge base.

Show:

-   when documents were added
-   domain growth
-   policy expansion
-   governance maturity over time

------------------------------------------------------------------------

## Agent Activity Layer

Future multi-agent systems can display:

-   ingestion agent
-   policy agent
-   validation agent
-   QA agent
-   governance agent

Each interaction leaves a visual trace.

------------------------------------------------------------------------

## Validation Layer

Highlight:

-   conflicting information
-   duplicate knowledge
-   weak embeddings
-   orphan nodes
-   inconsistent terminology

------------------------------------------------------------------------

# Opportunities

The Brain could evolve into much more than a visualization.

Potential opportunities include:

-   Organizational capability mapping
-   Governance maturity measurement
-   Gap analysis
-   AI-assisted policy planning
-   Risk visualization
-   Dependency analysis
-   Cross-domain impact analysis
-   Knowledge health scoring
-   Duplicate detection
-   Missing documentation recommendations
-   SME (Subject Matter Expert) discovery
-   Organizational onboarding
-   Executive dashboards
-   Change impact forecasting
-   Compliance readiness monitoring
-   Strategic planning support
-   Continuous documentation progress tracking
-   Visual debugging of the RAG system
-   Interactive presentations for leadership
-   Knowledge evolution playback
-   AI-generated recommendations for the next highest-value document to
    create

------------------------------------------------------------------------

# Technical Architecture

Input

-   SQL Server 2025 native vector storage
-   Embeddings
-   Metadata
-   Domain hierarchy
-   Document relationships

Processing

-   Graph construction
-   Semantic clustering
-   Gap analysis
-   Maturity scoring
-   Agent annotations

Presentation

-   Interactive graph
-   Filters
-   Layers
-   Search
-   Analytics

------------------------------------------------------------------------

# Suggested Technology

Backend

-   FastAPI
-   Python
-   SQL Server graph queries and native vector similarity
-   Graph algorithms

Frontend

-   .NET WPF window with WebView2
-   Locally bundled HTML, CSS, and JavaScript graph canvas

------------------------------------------------------------------------

# Future Vision

DomainLinks Brain becomes the visual operating system for organizational
knowledge.

Instead of asking only "What do we know?", users can answer:

-   Where are we strongest?
-   Where are our gaps?
-   Which domains are immature?
-   What documentation should be written next?
-   Which policies influence multiple domains?
-   How healthy is our organizational knowledge?
-   How is our knowledge evolving over time?

The long-term objective is to create a living digital representation of
organizational knowledge that continuously grows, measures itself, and
guides future development.

------------------------------------------------------------------------

# Additional Strategic Concepts

As we develop it further, I'd like to expand the PRD with several
additional concepts:

-   **Knowledge Health Index** -- an overall score for the
    organization's knowledge base.
-   **Domain Maturity Dashboard** -- maturity scores by domain, control
    area, or department.
-   **Knowledge Drift Detection** -- identify documents or policies that
    are becoming disconnected from the rest of the model.
-   **Control Coverage Visualization** -- show which mandates, controls,
    policies, procedures, forms, and evidence are fully linked versus
    incomplete.
-   **Recommendation Engine** -- "The next five documents you should
    create to maximize organizational maturity."
-   **Simulation Mode** -- temporarily add a proposed policy or control
    and visualize how it changes the knowledge graph before publishing.
-   **Executive Mode** -- a simplified, presentation-ready view for
    leadership showing organizational capability and gaps.
-   **Semantic Time Machine** -- replay how the knowledge base evolved
    over months or years.

One idea I find especially compelling is that the Brain should not
simply visualize vectors. It should visualize your organizational
framework---domains, controls, policies, procedures, records, evidence,
risks, and relationships---using the vector database only as the
intelligence behind the scenes. That would make it a governance and
planning tool, not just an AI visualization.
