# Integer Identity Key Conversion

## Decision

DomainLinks uses `INT IDENTITY(1,1)` primary keys and `INT` foreign keys for application entities. Natural codes remain unique business identifiers. Database IDs continue to cross the HTTP/JSON boundary as strings so the desktop contract remains stable.

## Scope

- Convert 33 GUID primary keys and every related GUID foreign key.
- Convert polymorphic `SemanticArtifacts.SourceRecordId` and `SourceParentId` values.
- Keep existing integer lookup keys unchanged.
- Preserve data, vectors, hashes, defaults, checks, unique constraints, indexes, foreign keys, and triggers.
- Add missing foreign keys from `PolicyControlExplanations` to `Policies` and `Controls`.
- Keep no `uniqueidentifier` columns in the active `dbo` application schema.

## Execution controls

1. Back up `DomainLinks` to `C:\SQLDatabases\Backups` and run `RESTORE VERIFYONLY`.
2. Restore a validation clone.
3. Run `scripts/convert_guid_ids_to_int.py` against the clone.
4. Export the GUID-to-integer mapping CSV.
5. Validate row counts, schema objects, constraints, and `DBCC CHECKDB`.
6. Run backend tests and desktop/API smoke checks against the clone.
7. Stop application writes, take a final backup, convert a fresh cutover clone, and swap database names.
8. Keep the former GUID database read-only as `DomainLinks_guid_archive_<timestamp>`.
9. Back up the converted production database with a database-and-timestamp filename.

The utility refuses the production database name unless `--allow-production` is explicitly supplied. A failed pre-commit conversion is rolled back.

## Data exception

Six derived `PolicyControlExplanations` cache rows referenced policies and controls that had already been deleted. The converter exports these invalid rows to CSV and removes them before adding the missing referential constraints. The pre-conversion backup and archived database also retain them.

## Rollback

Rollback is a database-name swap to the read-only GUID archive or a restore of the pre-conversion backup. An in-place integer-to-GUID reversal is intentionally unsupported.

## Execution result — 2026-07-21

- Production `DomainLinks` converted successfully: 33 integer identity PKs, zero user `uniqueidentifier` columns, and 46 validated foreign keys.
- Former production retained read-only as `DomainLinks_guid_archive_20260721_151121`.
- Final pre-cutover backup: `C:\SQLDatabases\Backups\DomainLinks_pre_integer_cutover_20260721_151121.bak`.
- Post-conversion backup: `C:\SQLDatabases\Backups\DomainLinks_post_integer_conversion_20260721_151341.bak`.
- Audit map: `C:\SQLDatabases\Backups\DomainLinks_guid_to_int_20260721_151121.csv`.
- Orphan cache export: `C:\SQLDatabases\Backups\DomainLinks_guid_to_int_20260721_151121_orphan_policy_control_explanations.csv`.
- `DBCC CHECKCONSTRAINTS`, `DBCC CHECKDB`, live API reads, semantic synchronization, backend tests, and desktop Release build passed.
