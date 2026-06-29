# Mail Data Importer

## What this is

A .NET 10 console application that automates the import of test report
emails sent to `[redacted]`. It replaces a legacy Access
VBA macro that ran against classic Outlook via COM automation — which
will no longer work as Microsoft transitions users to the New Outlook
for Windows.

The application:

1. Connects to the `[redacted]` shared mailbox via the
   Microsoft Graph API using app-only (client credentials) authentication.
2. Pulls messages from the Inbox where the subject contains
   `"Test Report"` and the message has attachments.
3. Fetches `.txt` attachment bytes and decodes them in-memory, parsing
   tab-delimited rows into typed `TestReport` objects.
4. Writes parsed `TestReport` data to the `AppDatabase` SQL Server database.
5. Moves processed messages to the `Imported Test Reports` mailbox folder
   after a successful database write, so they are not picked up on the
   next run.
6. Sends a summary email to Importing Personnel when processing errors occur.

## Current status

- [x] Architecture decided
- [x] Configuration infrastructure (`AppConfig.cs`, `appsettings.json`, layered config wiring in `Program.cs`)
- [x] Entra ID app registration configured
- [x] Graph client setup (`Classes/GraphClient.cs`)
- [x] Mail retrieval — filter inbox by subject keyword, collect messages with attachments (`Classes/ExchangeMail.cs`)
- [x] Attachment retrieval — fetch `.txt` attachments by ID, return raw `byte[]` content (`Classes/ExchangeMail.cs`)
- [x] Attachment model defined (`Models/Attachment.cs`)
- [x] Test report model defined, aligned with `AppDatabase` table schema (`Models/TestReport.cs`)
- [x] In-memory attachment parsing — decode `byte[]` to UTF-8 string, validate 4-line header format, parse tab-delimited rows into `TestReport` objects, null-safe field mapping, supplier name derived from email subject keywords (`Classes/AttachmentData.cs`)
- [x] SQL INSERT statement generation and execution — builds and executes a multi-row `INSERT INTO tbl_import_records`; magnetic saturation correction factor queried from `config_schema.tbl_grade_tolerances` per grade and supplier (`Classes/Query.cs`)
- [x] Mail movement — processed messages moved to the configured imported folder after a successful database write (`Classes/ExchangeMail.cs`)
- [x] Unit test project configured — NUnit 4.3.2 in `Tests/`, initial coverage of `AttachmentData.ProcessTestReports` (supplier name detection, invalid header format validation)
- [x] Server-side mail filtering — `$filter=hasAttachments eq true` with explicit `$select`; subject keyword check stays client-side by design (`Classes/ExchangeMail.cs` — see note below)
- [x] Notification email — `SendMailGenericAsync` implemented in `ExchangeMail.cs`
- [x] LINQ aggregation in `AttachmentData.ProcessTestReports` — deduplicates rows by key fields before database insert
- [ ] Deployment to Debian server with cron

## Tech stack

- **.NET 10** (LTS, supported through November 2028)
- **Microsoft Graph SDK v5** (`Microsoft.Graph` NuGet package)
- **Azure.Identity** for `ClientSecretCredential`
- **Microsoft.Data.SqlClient** for SQL Server connectivity
- **Microsoft.Extensions.Configuration** for layered configuration
- **Microsoft.Extensions.Logging** for structured logging; uses `AddSystemdConsole` on Linux and `AddConsole` on Windows
- **NUnit 4.3.2** with **NUnit3TestAdapter 5.0** — unit test framework for the `Tests/` project

⚠️ The Graph SDK changed significantly between v4 and v5. v4 syntax
(`graphClient.Users[...].Request().GetAsync()`) will not compile against
v5. v5 syntax: `graphClient.Users[...].GetAsync()` — no `.Request()`
call. Many third-party tutorials are still on v4; the official
Microsoft Learn docs are current.

## Architecture overview

See `/docs/mail-data-importer-context-design.webp` for the C4 System Context view.

**In-memory processing pattern.** The importer fetches attachment bytes
from the Graph API, decodes them in-memory from `byte[]` to UTF-8 string,
validates the expected 4-line header format, and parses tab-delimited rows
directly into `TestReport` objects. No files are written to disk at any
point. After a successful database write, messages are moved to the
`Imported Test Reports` folder. This folder move is the idempotency
mechanism — messages remain in the Inbox until successfully processed, so
a failed run can be retried by simply running the application again.

### Key classes and models

- **`Classes/GraphClient.cs`** — wraps `ClientSecretCredential` and produces a
  `GraphServiceClient`. Reads tenant ID, client ID, and client secret from
  `AppConfig`. Instantiated once in `Program.cs` and passed through.
- **`Classes/ExchangeMail.cs`** — instance class. Constructor accepts
  `GraphServiceClient` and `AppConfig`, storing them for use by all methods.
  `GetMailAsync` retrieves inbox messages using a server-side `$filter`
  (`hasAttachments eq true`) and an explicit `$select` (`id, sender, subject`);
  the subject keyword check is null-safe client-side LINQ
  (`Subject?.Contains(...) == true` — the `?.`/`== true` pair is load-bearing:
  it skips null-subject messages instead of throwing). `GetTestReportAsync`
  fetches `.txt` attachment content for a list of emails by calling the
  single-attachment Graph endpoint (list endpoint does not return
  `ContentBytes`). `GetAttachmentContentAsync` is private and handles the
  individual fetch with `$select=id,name,contentBytes`, casting the base
  `Attachment` type to `FileAttachment` to access `ContentBytes`.
  `MoveProcessedEmails` moves each processed message to the destination folder
  configured in `AppConfig`; Graph API errors per message are caught as
  `ODataError` and logged without aborting the rest of the batch.
  `SendMailGenericAsync` sends a notification email to the addresses in
  `Graph:AlertMailboxes`. `GetMailboxFolderIdsAsync` is a debug helper that
  prints all folder IDs for the mailbox to the console.
- **`Classes/AttachmentData.cs`** — static class. `ProcessTestReports` accepts
  a list of `Attachment` objects, rejects null or empty content up front
  (logs an error and throws `InvalidDataException`), decodes each `byte[]`
  as UTF-8, validates
  the expected 4-line header, splits rows on `\t`, maps fields to
  `TestReport` objects, and derives the supplier name from keywords in the
  email subject (G1, G2, GET/GT, ZWG, Zigong, etc.), defaulting to GESAC.
  Private helpers `NullIfEmptyString`, `NullIfEmptyInt`, and `NullIfEmptyFloat`
  return `null` for empty tab-delimited fields rather than converting empty strings.
  After parsing, a LINQ `GroupBy`/`Select` query deduplicates the list: rows
  sharing the same `Grade`, `LotNumber`, `BlankLotNumber`, `RtpLotNumber`,
  `PartNumber`, `ShipDate`, `ShipMethod`, and `Supplier` are collapsed to one row;
  `Quantity` is summed and all measurement fields are averaged.
- **`Classes/AttachmentType.cs`** — enum with a single value `TestReport`. Used
  to select the subject keyword in `GetMailAsync` and the destination folder in
  `MoveProcessedEmails`. The `ShippingList` value was removed in June 2026;
  shipping list importing is out of scope for this application.
- **`Classes/Query.cs`** — instance class (not static). Constructor accepts
  `AppConfig`, builds a connection string from `SqlSettings`, and opens a
  `SqlConnection`. Implements `IDisposable`; `Dispose()` closes the connection.
  Wrap in a `using` block at the call site. `InsertTestReportData`
  builds and executes a multi-row `INSERT INTO AppDatabase.import_schema.tbl_import_records`
  and returns the number of rows affected. `GetMagSatCorrectionFactor` is a
  private method that queries `AppDatabase.config_schema.tbl_grade_tolerances` to retrieve
  a supplier- and grade-specific correction factor for the magnetic saturation
  value; special handling applies for grade `[GradeCode]` and suppliers ZWG/Zigong.
  `TransverseRuptureStrength` is converted from MPa to PSI using the `145.038`
  factor before insertion.
- **`Models/Email.cs`** — immutable record of a message: ID, subject, sender
  address, sender name, and `HasAttachments` flag.
- **`Models/Attachment.cs`** — immutable wrapper for a fetched attachment:
  Graph attachment ID, filename, email subject (passed through for supplier
  detection in `AttachmentData`), and raw `byte[]` content.
- **`Models/TestReport.cs`** — typed model for a single parsed test report row.
  Property types are intentionally aligned with the corresponding `AppDatabase`
  SQL Server column definitions. `float?` for `TransverseRuptureStrength`
  matches the `float`-nullable column in the database. Includes a `Supplier`
  property populated by `AttachmentData.ProcessTestReports` from email subject
  keyword matching.
- **`Tests/AttachmentDataTests.cs`** — NUnit test class covering
  `AttachmentData.ProcessTestReports`. Uses `BuildValidContent` to construct
  minimal valid 4-line header + 1 data row attachment bytes in-memory, and
  `BuildValidMultilineDuplicateContent` to construct a two-row attachment where
  both rows share the same key fields (used for aggregation tests). Current
  tests: invalid first header line throws `InvalidDataException`; subject
  containing `"G2 Unit5"` sets `Supplier` to `"G2 - Unit 5"`; subject containing
  `"ZWG"` sets `Supplier` to `"ZWG"`; two duplicate rows produce a single
  aggregated result row; `Quantity` is summed across duplicate rows. Note:
  `String.Trim()` is called on each data line before splitting, so trailing tabs
  are stripped — test data rows must not rely on a trailing tab to produce the
  last field.

**Error handling pattern.** Two deliberate philosophies coexist:

- **Parsing is fail-fast.** `ProcessTestReports` throws
  `InvalidDataException` on empty content, a bad header, or a malformed
  row, which halts the entire run before anything is written to the
  database. Because unprocessed messages stay in the Inbox, a malformed
  attachment will block *every subsequent run* until the offending email
  is manually removed. This is a conscious data-integrity choice — never
  import around bad data — not an oversight. ⚠️ The private helper
  `RaiseTestReportError` that fires the notification email and throws is
  currently `async void`, which means the throw does not propagate to the
  call site synchronously.
- **Mail movement is per-message.** Failures in `MoveProcessedEmails` are
  caught and logged individually so that one bad message does not abort
  the rest of the batch (the DB write already succeeded at that point).

Notification emails for processing errors are sent via
`ExchangeMail.SendMailGenericAsync`.

**Server-side filtering.** The original design filtered on an
`"Import This"` category, but that was an artifact of the legacy Outlook
workflow and was dropped from the design in June 2026. Selection criteria
are now: subject contains `"Test Report"`, message has attachments,
attachment is `.txt`.

The intended split is:

- **Server-side (`$filter`):** `hasAttachments eq true`
- **Client-side (LINQ):** subject-contains-keyword check

The subject check stays client-side deliberately. OData `$filter` on
Outlook mail does not reliably support `contains(subject, ...)` — the
Exchange backend commonly rejects substring filters even though
`contains` is a general OData function (per the
[`$filter` docs](https://learn.microsoft.com/graph/filter-query-parameter),
operator support varies by resource; only `eq`, `startswith`, date
ranges, etc. are dependable for mail). `$search` supports substring
subject matching but cannot be combined with `$filter` on messages and
queries the search index rather than doing a deterministic comparison.
Because the shared mailbox is dedicated and processed messages are
moved out of the Inbox, the post-filter result set is small and the
client-side subject check is cheap.

This split is implemented in `ExchangeMail.GetMailAsync` as of June 2026.
Both Graph calls also use explicit `$select` projections that name every
property the code consumes (including `id`) rather than relying on
service defaults.

## Configuration & secrets

Layered configuration via `Microsoft.Extensions.Configuration`. Sources
in priority order (later overrides earlier):

1. `appsettings.json` — non-secret defaults, committed to source
2. User Secrets — development only, for secrets on developer workstations
   (loaded via `AddUserSecrets<AppConfig>(optional: true)`)
3. Environment variables prefixed `MDI_` — used in production on the
   Debian server (e.g., `MDI_Graph__ClientSecret`; note the double
   underscore maps to the `:` config-section separator)

Configuration is bound to `AppConfig` (defined in `AppConfig.cs`), which
has three nested settings classes:

- `GraphSettings` — `TenantId`, `ClientId`, `ClientSecret`, `MailboxUpn`,
  `ImportedTestReportsFolderId`, `AlertMailboxes`
- `ImportSettings` — `AllowedExtension`
- `SqlSettings` — `DataSource`, `Username`, `Password`, `InitialCatalog`

`AppConfig.Validate()` is called at startup and throws
`InvalidOperationException` listing any required keys that are blank.
Currently required: `Graph:TenantId`, `Graph:ClientId`, `Graph:MailboxUpn`,
`Graph:ImportedTestReportsFolderId`, `Import:AllowedExtension`,
`Sql:DataSource`, `Sql:Username`, and `Sql:InitialCatalog`. `Graph:ClientSecret` and `Sql:Password` are
intentionally not validated here — both come from secrets, not
`appsettings.json`.

**What is and isn't a secret.** `TenantId`, `ClientId`, folder IDs, server
name, SQL username, and `AllowedExtension` are non-sensitive and are committed
in `appsettings.json`. Only `Graph:ClientSecret` and `Sql:Password` must be
kept out of source control.

**Secrets never go in source control.** Not in `appsettings.json`, not
in any committed file, not in code comments, not in commit messages.
Private repo or not, this is a hard rule.

The shared dev secrets live in the team password manager. Each
developer loads them into their own User Secrets after cloning:

```bash
dotnet user-secrets set "Graph:ClientSecret" "<value-from-team-vault>"
dotnet user-secrets set "Sql:Password" "<value-from-team-vault>"
```

On the Debian server, secrets are supplied via environment variables in
the cron environment or via systemd `LoadCredential=`.

## External dependencies

- **Microsoft Entra ID app registration** — provides the client ID and
  client secret used to authenticate to Graph. Application permissions
  `Mail.ReadWrite` and `Mail.Send` granted with admin consent.
- **ApplicationAccessPolicy** in Exchange Online scopes this app's
  Graph access to *only* the `[redacted]` mailbox.
  This is the security boundary that prevents the app from being able
  to read other mailboxes, even though the Graph permission is granted
  at the tenant level. If Graph calls start returning 403 for the
  the shared mailbox unexpectedly, check whether this policy still
  exists.
- **Microsoft Exchange Server** — hosts the shared mailbox.
- **AppDatabase (SQL Server)** — destination for parsed test report data.
  The application connects using SQL authentication (not Windows
  integrated auth). `Encrypt = false` and `TrustServerCertificate = true`
  are set for the internal server connection.
- **Cron on Debian** — triggers the application once per day.

## Intentional quirks (do not "fix" or refactor away)

- **.txt-only filter.** Only `.txt` attachments are processed. Other
  attachment types on matching messages are skipped, not errored on.
  The original importer accepts only fixed-width text reports.
- **`<Compile Remove="Tests\**" />` in `MailDataImporter.csproj`.** The
  .NET SDK's default glob compiles all `*.cs` files recursively from the
  project root, which would pull the `Tests/` subdirectory into the main
  project's build. This exclusion prevents that. Do not remove it.

## Coding conventions

- The maintainer is a self-taught programmer at junior-to-mid level.
  When suggesting changes, explain **why** — not just what to change.
  Link to reputable documentation (Microsoft Learn, official docs,
  Martin Fowler, OWASP, refactoring.guru, c4model.com, Stephen
  Cleary's blog) when citing a best practice.
- Don't change more than what was asked. If a request is ambiguous,
  ask for clarification rather than guessing.
- Offer alternative options for design decisions so they can be
  evaluated explicitly.
- Don't cite Reddit, Stack Overflow comments, social media, or
  AI-generated content as authoritative.
- Avoid GitHub repos as code references when official docs cover the
  topic. When using them, prefer repos created or actively maintained
  since 2025 that don't appear to be primarily AI-generated.
- Output complete code only when explicitly asked. For exploratory
  conversation, sketches or pseudocode are preferred.

## Deployment targets

**Development:** Windows 11 with JetBrains Rider. User Secrets stores
the dev secrets (Graph client secret and SQL password) per developer.

**Production:** Debian server, .NET 10 installed from the Microsoft apt
repository. The application runs as a dedicated unprivileged user
(`app-svc`) and is triggered by cron. Secrets are supplied via the
cron environment or systemd `LoadCredential=`. Logging uses
`AddSystemdConsole` on Linux for structured systemd journal output.

## Related documentation

- `/docs/mail-data-importer-context-design.webp` — C4 System Context diagram
- `/docs/container-diagram.drawio` — *(planned)* C4 Container view
- `docs/CHANGELOG.md` — notable changes by version; update `[Unreleased]` with each change
- `CLAUDE.md` (this file) — project context for Claude Code and future
  human maintainers
