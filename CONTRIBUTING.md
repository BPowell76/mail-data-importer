# Contributing to Mail Data Importer

This document is for anyone joining the project — whether you're setting up a
dev environment for the first time, fixing a bug, or adding a new feature.

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.x | [Download from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) |
| Git | Any current | — |
| JetBrains Rider *or* VS Code | Current | Rider is the team IDE; VS Code with the C# Dev Kit extension works too |

You do **not** need direct access to the shared mailbox
or to the `AppDatabase` SQL Server to build the application. The app will fail at
runtime without valid secrets, but it will compile and the config validation
will tell you exactly what is missing.

---

## Getting started

### 1. Clone the repository

```bash
git clone <repo-url>
cd MailDataImporter
```

### 2. Restore dependencies

```bash
dotnet restore
```

### 3. Load secrets

Two secrets are required at runtime and are **never** stored in source control.
Get the current development values from the team password manager, then load
them into [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets):

```bash
dotnet user-secrets set "Graph:ClientSecret" "<value-from-team-vault>"
dotnet user-secrets set "Sql:Password" "<value-from-team-vault>"
```

User Secrets are stored outside the project directory on your machine and are
never committed to git. They are automatically picked up at runtime in
development because `AddUserSecrets<AppConfig>(optional: true)` is wired into
`Program.cs`.

### 4. Build and run

```bash
dotnet build
dotnet run
```

If any required config keys are missing, `AppConfig.Validate()` will throw at
startup with a list of what is blank, so you'll know immediately what to fix.

### 5. Run the tests

Unit tests live in the `Tests/` project and do not require a live database or
Graph API connection. Run them any time with:

```bash
dotnet test
```

---

## Configuration reference

Configuration is layered — later sources override earlier ones:

| Priority | Source | Used for |
|---|---|---|
| 1 (lowest) | `appsettings.json` | Non-secret defaults; committed to source |
| 2 | User Secrets | Dev secrets on developer workstations |
| 3 (highest) | Environment variables (`MDI_` prefix) | Production on the Debian server |

The double-underscore in environment variable names maps to the `:` section
separator (e.g., `MDI_Graph__ClientSecret` → `Graph:ClientSecret`). See
[Microsoft's docs on environment variable configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/#environment-variables).

**Required keys** (must be non-blank at startup):

- `Graph:TenantId`
- `Graph:ClientId`
- `Graph:MailboxUpn`
- `Graph:ImportedTestReportsFolderId`
- `Import:AllowedExtension`
- `Sql:DataSource`
- `Sql:Username`
- `Sql:InitialCatalog`

**Secret keys** (required at runtime but intentionally excluded from `Validate()`):

- `Graph:ClientSecret` — always comes from User Secrets or environment variable
- `Sql:Password` — always comes from User Secrets or environment variable

---

## Project structure

```
MailDataImporter/
├── AppConfig.cs          # Configuration binding and startup validation
├── Program.cs            # Entry point and main processing pipeline
├── appsettings.json      # Non-secret defaults (committed)
├── MailDataImporter.csproj
├── MailDataImporter.sln
├── Classes/
│   ├── AttachmentData.cs # Static class: decode and parse .txt attachment bytes into TestReport objects
│   ├── AttachmentType.cs # Enum: TestReport
│   ├── ExchangeMail.cs   # Instance class: fetch messages and attachments via Graph; move processed messages; send notification emails
│   ├── GraphClient.cs    # Wraps ClientSecretCredential; produces GraphServiceClient
│   └── Query.cs          # Instance class: open SQL connection, execute INSERT, look up correction factors
├── Models/
│   ├── Attachment.cs     # Attachment ID, filename, email subject, and raw byte[] content
│   ├── Email.cs          # Message ID, subject, sender address/name, HasAttachments
│   └── TestReport.cs     # Typed model for one parsed test report row; aligned with AppDatabase schema
├── Tests/
│   ├── Tests.csproj      # NUnit test project; references main project via ProjectReference
│   └── AttachmentDataTests.cs  # Tests for AttachmentData.ProcessTestReports
├── docs/
│   ├── CHANGELOG.md      # Notable changes by version
│   └── mail-data-importer-context-design.webp   # C4 System Context diagram
├── AGENTS.md             # Instructions for AI coding agents working in this repo
├── CLAUDE.md             # Context file for Claude Code and future maintainers
├── CONTRIBUTING.md       # This file
└── README.md
```

---

## Architecture decisions you should know before changing code

**In-memory processing pipeline.** The importer fetches attachment bytes from
the Graph API, decodes them in-memory, validates the expected header format,
and parses tab-delimited rows directly into `TestReport` objects. No files are
written to disk at any point. The idempotency mechanism is the folder move:
messages are not moved out of the Inbox until after a successful database
write, so a failed run can be retried by running the application again.

**The filtering split is intentional.** `ExchangeMail.GetMailAsync` filters
server-side with OData (`$filter=hasAttachments eq true`, explicit `$select`)
and checks the subject keyword client-side with null-safe LINQ. The subject
check stays client-side deliberately: Exchange does not reliably support
`contains()` in mail `$filter`, and because the shared mailbox is dedicated
whose processed messages are moved out of the Inbox, the post-filter result
set is small. Do not move the subject check into `$filter`, and do not
reintroduce the `"Import This"` category filter — it was an artifact of the
legacy Outlook workflow and was dropped from the design in June 2026.

**Parsing failures halt the run on purpose.** `AttachmentData.ProcessTestReports`
throws `InvalidDataException` on empty content, a bad header, or a malformed
data row — before anything is written to the database. Since unprocessed
messages stay in the Inbox, a malformed attachment blocks subsequent runs
until the offending email is removed. This fail-fast behavior is a deliberate
data-integrity decision; do not soften it to skip-and-continue without
talking to the maintainer.

**`.txt`-only filtering is intentional.** Only `.txt` attachments are
processed. Other attachment types on matching messages are silently skipped.
The original importer accepted only fixed-width text reports; this behavior is
preserved.

---

## Making changes

### General guidelines

- Change only what was asked. If the scope of a request is unclear, ask before
  starting.
- Secrets must never appear in committed files — not in `appsettings.json`,
  not in code comments, not in commit messages. This applies even in a private
  repository.
- Use Microsoft Learn as the primary reference for Graph SDK and .NET APIs.
  The Graph SDK moved from v4 to v5 and many third-party tutorials are out of
  date — always check the official docs.

### Graph SDK version note

This project uses **Microsoft Graph SDK v5**. v5 removed the `.Request()`
call that v4 required. If you find third-party examples that use
`.Request().GetAsync()`, they are v4 syntax and will not compile here.
Correct v5 syntax: `graphClient.Users["..."].GetAsync()`.

Reference: [Microsoft Graph .NET SDK changelog](https://learn.microsoft.com/en-us/graph/sdks/sdk-installation)

### Committing

When your change adds a feature, fixes a bug, or changes behavior, add a brief
entry under `[Unreleased]` in [CHANGELOG.md](docs/CHANGELOG.md) before committing.
Use the appropriate category (`Added`, `Changed`, `Fixed`, `Removed`). See the
existing entries for the expected style.

Write commit messages that explain *why* the change was made, not just *what*
changed. The diff shows what; the message should explain the reason.

Example:

```
Strip Exchange legacy DN from attachment filenames

Downstream file consumers expect filenames without the
'/o=ExchangeLabs/...' suffix that Exchange appends in some cases.
Preserves the behavior of the original VBA importer.
```

---

## Contacts

| Area | Contact |
|---|---|
| Project maintainer, application behavior, business rules | Brian Powell |
