# Chat Storage And DRP Design

## Goal

Keep root/child chats private to the local user while still supporting disaster recovery and seamless restore on a replacement PC.

## Decisions

- Local chat files are the primary working copy.
- Local storage path is `%AppData%\DomainLinks\Chats\`.
- One root collection maps to one local JSON file.
- File names should be human-readable but stable:
  - `{SanitizedRootDisplayName}--{CollectionCode}.json`
- Backup happens after every completed answer.
- Server backup stores encrypted and compressed chat files, not plaintext messages.
- User identity rule:
  - if domain-backed identity is available, prefer `SID`
  - otherwise use `WindowsUserName`
- Restore prompt rule:
  - if there are no local root chat files and backups exist for the user, ask before restoring

## Local File Model

Each file represents one root collection and all of its child threads.

Suggested shape:

```json
{
  "rootCollectionCode": "hiring-policy",
  "rootDisplayName": "Hiring Policy",
  "threads": [
    {
      "threadId": "guid",
      "title": "Vacation policy follow-up",
      "messages": [
        {
          "role": "User",
          "content": "Question text",
          "createdUtc": "2026-05-21T19:33:00Z"
        },
        {
          "role": "Assistant",
          "content": "Answer text",
          "supplementalText": "Sources: HR: handbook.pdf",
          "modelName": "gemma3:1b",
          "createdUtc": "2026-05-21T19:33:05Z",
          "durationSeconds": 5.2,
          "tokensTotal": 412,
          "tokensPerSecond": 79.2
        }
      ]
    }
  ]
}
```

## Metrics

The reference app stored a per-answer metrics line that included:

- model
- tokens
- time
- TPS
- timestamp

Reference examples:

- [Form1.vb](../DomainLinksAI/Form1.vb:354)
- [Form1.vb](../DomainLinksAI/Form1.vb:846)

For DomainLinks Desktop, the message model should store those as structured fields rather than one formatted line, then render a compact summary line in the response UI.

## User Identity

Resolution rule:

1. If a domain-backed SID is available, identify the user by SID.
2. If not, fall back to Windows username.

`dbo.AppUsers` supports both values. The desktop app should:

- try to resolve the current SID
- upsert an `AppUsers` row
- use `WindowsSid` when present for backup lookup
- fall back to `WindowsUserName` otherwise

## Encryption Model

Use a per-user application key protected by the user identity.

Recommended sequence:

1. Serialize local root chat JSON.
2. Compress with gzip.
3. Encrypt with AES-GCM using the per-user application key.
4. Store ciphertext locally and in SQL backup.

Reason:

- replacement-PC restore is not practical with machine-only DPAPI
- a user-scoped application key is more portable while still private

## SQL Server Backup Model

Backups are stored as encrypted blobs in SQL Server.

### 003_app_users.sql

Creates `dbo.AppUsers`.

Purpose:

- app-level user identity
- supports SID-first, username-fallback logic
- keeps user backup ownership explicit

### 004_user_chat_backup_files.sql

Creates `dbo.UserChatBackupFiles`.

Purpose:

- one encrypted backup row per root collection file per user
- content is compressed and encrypted before insert
- plaintext chats are never stored in shared org scope

## Restore Flow

At startup:

1. Resolve current user identity.
2. Check `%AppData%\DomainLinks\Chats\` for local root chat files.
3. If local files exist:
   - load local files
   - do not auto-restore
4. If local files do not exist:
   - check SQL backup rows for this user
   - if backups exist, prompt:
     - `We found backed-up chats for your account but no local chats on this PC. Restore them now?`
5. If user accepts:
   - download encrypted backup rows
   - decrypt
   - decompress
   - recreate local root files
   - load them into the UI

## Backup Flow

Backup occurs after every completed answer.

Recommended sequence:

1. Update in-memory thread.
2. Write the root JSON file locally.
3. Compress and encrypt the root file payload.
4. Upsert `dbo.UserChatBackupFiles` for that root collection.

Also back up on app close as a safety pass.

## Conflict Rule

For the first implementation:

- local files are authoritative if they exist
- restore prompt appears only when local root chat files are absent
- no auto-overwrite of local files

Later menu actions can add:

- restore all backups
- restore one root
- merge missing roots only
- replace local with server backup

## Future UI Hooks

This design assumes future menu or tabbed settings work for:

- Restore chats
- Backup now
- Export root chat
- Import root chat
- Backup status
- Encryption / identity diagnostics

## Next Implementation Steps

1. Move current desktop chat persistence out of `domainlinks-desktop.settings.json`.
2. Add local file repository under `%AppData%\DomainLinks\Chats\`.
3. Extend chat message model with structured stats fields.
4. Add user identity resolution and `AppUsers` upsert.
5. Add SQL backup upsert after every completed answer.
6. Add restore prompt on startup when local chat files are absent.
