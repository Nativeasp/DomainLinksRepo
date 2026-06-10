Removed. Build passed.

Short table list:

- `Domains`
  - top-level memory areas like `projects`, `hr`, `finance`

- `Collections`
  - named containers inside a domain; project roots and durable knowledge sets

- `Documents`
  - one row per uploaded/source document

- `ContentUnits`
  - the chunks/sections extracted from each document

- `ContentUnitEmbeddings768`
  - vector embeddings for chunks

- `EmbeddingProfiles`
  - embedding model/config definitions

- `RetrievalProfiles`
  - retrieval strategy presets

- `DomainClusters`
  - optional grouping of domains

- `DomainClusterMembers`
  - links domains into clusters

- `ProviderSettings`
  - provider config/secrets

- `LegacyChromaMigrationState`
  - tracks migration from the old Chroma setup

- `SchemaMigrations`
  - records which SQL migrations ran

If you want, next I can do the same short map for relationships:
`Domain -> Collection -> Document -> ContentUnit -> Embedding`.