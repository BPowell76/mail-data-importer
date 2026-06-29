# Mail Data Importer

A .NET 10 console application that automates the import of supplier test report
emails received by `[redacted]`. It replaces a legacy Access
VBA macro that relied on COM automation against classic Outlook, which is no
longer viable as Microsoft transitions users to the New Outlook for Windows.

## What it does

1. Connects to the `[redacted]` shared mailbox via the
   Microsoft Graph API using app-only (client credentials) authentication.
2. Retrieves messages from the Inbox where the subject contains `"Test Report"`
   and the message has attachments.
3. Fetches `.txt` attachment bytes, parses them in-memory into typed
   `TestReport` objects, and derives the supplier name from keywords in the
   email subject.
4. Writes parsed `TestReport` data to the `AppDatabase` SQL Server database.
5. Moves processed messages to the `Imported Test Reports` mailbox folder
   after a successful database write, so they are not picked up on the next run.
6. Sends a summary email to Importing Personnel when processing errors occur.

## Getting started

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup instructions, configuration
reference, and development guidelines.

## Changelog

See [CHANGELOG.md](docs/CHANGELOG.md) for a history of notable changes by version.

## Testing

Unit tests are in the `Tests/` project using NUnit 4. They do not require
a live database or Graph API connection and can be run at any time:

```bash
dotnet test
```

# Design Concept

The software architecture concepts outlined below are in alignment with the
general guidelines of the [C4](https://c4model.com) model.

## System Context
![Mail Data Importer application context diagram](/docs/mail-data-importer-context-design.webp)

## Container
*Coming soon*

## Component
*Coming soon*

## Code
*Coming soon*

# Development Team
This application was originally developed by Brian Powell.
