# Package Inventory and License Rationale — R0-001

R0-001 adds only Microsoft and xUnit packages needed for the bootstrap solution, executable smoke tests, and the cloud health endpoint integration test.

| Package | Version | License | Maintainer activity | Rationale |
|---|---:|---|---|---|
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.0 | MIT | Microsoft-maintained with .NET servicing | Enables the agent executable to be hosted as a native Windows Service without adding monitoring behavior. |
| Microsoft.NET.Test.Sdk | 18.0.0 | MIT | Microsoft-maintained test platform | Required for `dotnet test` discovery and execution. |
| xunit | 2.9.3 | Apache-2.0 | Actively maintained xUnit project | Provides smoke and contract test assertions for the bootstrap. |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | Actively maintained xUnit project | Integrates xUnit tests with the .NET test platform and CI. |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | MIT | Microsoft-maintained with ASP.NET Core servicing | Provides in-memory ASP.NET Core integration testing for the cloud health endpoint. |
| JsonSchema.Net | 7.4.0 | MIT | Actively maintained community JSON Schema implementation | Parses repository schema documents as Draft 2020-12 JSON Schemas in contract tests so schema syntax errors fail the bootstrap validation workflow. |

No package added in this task performs packet capture, tunneling, scanning, credential storage, software updating, or external provider access.
