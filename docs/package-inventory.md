# Package Inventory and License Rationale — R0

R0-001 added the Microsoft and xUnit packages needed for the bootstrap solution, executable smoke tests, and cloud health integration test. R0-004 adds the narrow SQLite provider and an explicit patched native-library pin required for the local journal/outbox.

| Package | Version | License | Maintainer activity | Rationale |
|---|---:|---|---|---|
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.0 | MIT | Microsoft-maintained with .NET servicing | Enables the agent executable to be hosted as a native Windows Service without adding monitoring behavior. |
| Microsoft.NET.Test.Sdk | 18.0.0 | MIT | Microsoft-maintained test platform | Required for `dotnet test` discovery and execution. |
| xunit | 2.9.3 | Apache-2.0 | Actively maintained xUnit project | Provides smoke and contract test assertions for the bootstrap. |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | Actively maintained xUnit project | Integrates xUnit tests with the .NET test platform and CI. |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | MIT | Microsoft-maintained with ASP.NET Core servicing | Provides in-memory ASP.NET Core integration testing for the cloud health endpoint. |
| JsonSchema.Net | 7.4.0 | MIT | Actively maintained community JSON Schema implementation | Parses repository schema documents as Draft 2020-12 JSON Schemas in contract tests so schema syntax errors fail the bootstrap validation workflow. |
| Microsoft.Data.Sqlite | 10.0.10 | MIT | Microsoft-maintained .NET 10 servicing line; released July 2026 | Provides the narrow ADO.NET SQLite boundary required by ADR-005 without adding an ORM or migration framework. |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 | SQLitePCLRaw-maintained native SQLite bundle; released July 2026 | Explicitly pins the patched native SQLite library. Microsoft.Data.Sqlite 10.0.10 otherwise resolves 2.1.11, which NuGet flags under high-severity advisory GHSA-2m69-gcr7-jv3q. |

No package in this inventory performs packet capture, tunneling, scanning, credential storage, software updating, or external provider access. R0-004 adds no ORM and no general migration framework.
