# MailDataImporter AGENTS.md File

This repository is a .NET 10 console application that retrieves messages and
attachments from Microsoft Exchange Server using the Microsoft Graph API.
Matching `.txt` attachment bytes are decoded and parsed in-memory into typed
`TestReport` objects. The supplier name is derived from keywords in the email
subject and stored on each `TestReport`. Parsed data is written to the
`AppDatabase.import_schema.tbl_import_records` table in SQL Server via `Classes/Query.cs`.
A magnetic saturation correction factor is looked up per grade and supplier
from `AppDatabase.config_schema.tbl_grade_tolerances` before insertion. After a successful
database write, processed messages are moved to the configured imported folder
via `ExchangeMail.MoveProcessedEmails`. `ExchangeMail` is an instance class —
its constructor accepts `GraphServiceClient` and `AppConfig`; do not treat it
as static.

## Dependencies
- You are not allowed to update dependencies. If necessary, inform the user
  about the outdated dependency so they can contact the project maintainer.

## Coding Conventions
- Code must work on both Windows and Debian 13. Windows is used for development
  work, but the project will run on a Debian server.
- You are not allowed to write SQL queries.
- You are not allowed to touch code that is unrelated to the prompt.
- For abstracted code that is used elsewhere, the code should not be changed.
  If a code change is needed for a class or method that meets these criteria,
  the user needs to contact the project maintainer.

## Intentional Design Decisions (do not "fix")
- Mail filtering is split on purpose: `$filter=hasAttachments eq true` runs
  server-side; the subject keyword check is null-safe client-side LINQ in
  `ExchangeMail.GetMailAsync`. Exchange does not reliably support `contains()`
  in mail `$filter`, so do not move the subject check into the OData filter.
- The `"Import This"` category filter was removed from the design in June
  2026. Do not reintroduce it.
- Parsing is fail-fast: `AttachmentData.ProcessTestReports` throws
  `InvalidDataException` on empty content, a bad header, or a malformed row,
  halting the run before any database write. Do not change this to
  skip-and-continue.
- Both Graph calls use explicit `$select` projections listing every property
  the code reads (including `id`). Keep `$select` in sync when consuming new
  properties.
- Shipping list importing is explicitly out of scope. The `ShippingList` value
  was removed from `AttachmentType` in June 2026 by agreement. Do not
  reintroduce it without explicit direction from the maintainer.

## Unit Testing
- Unit tests live in `Tests/AttachmentDataTests.cs` using NUnit 4.3.2. Run
  them with `dotnet test`. Tests do not require a live database or Graph API
  connection.
- Focus on core logic (parsing, supplier detection, validation). Do not attempt
  to unit test `Query.cs`, `ExchangeMail.cs`, or `GraphClient.cs` — they require
  live external dependencies that are not appropriate for unit tests.
- When adding new tests, follow the Arrange / Act / Assert pattern already
  established in `AttachmentDataTests.cs`. Use `BuildValidContent` to construct
  minimal valid single-row attachment byte arrays, or
  `BuildValidMultilineDuplicateContent` for tests that require multiple rows
  with shared key fields (e.g., aggregation tests). Do not duplicate this
  setup inline.
- `AttachmentData.ProcessTestReports` calls `String.Trim()` on each data line
  before splitting on tabs. Do not end a test data row with a tab character —
  it will be stripped, leaving one fewer field than expected.
