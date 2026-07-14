# Technology Stack — R0 Baseline

## Selected baseline

| Layer | Selection | Rationale |
|---|---|---|
| Agent runtime | C# on .NET 10 LTS, native Windows Service | Strong Windows lifecycle and OS integration; shared contracts with cloud |
| Local persistence | SQLite | Embedded transactional journal and outbox without a separate service |
| Local UI | Loopback-only ASP.NET Core or tray companion | Fast configuration/status without exposing a LAN service |
| Cloud API/workers | ASP.NET Core on .NET 10 | Shared language, container-friendly deployment, mature background services |
| Cloud database | PostgreSQL 18 for development; managed supported PostgreSQL in hosted environments | Relational integrity, JSON support, reporting, long support window |
| Testing | xUnit plus unit, integration, contract, and failure-scenario suites | Clear separation of logic, storage, HTTP, and system behavior |
| Logging/telemetry | Microsoft.Extensions.Logging and OpenTelemetry-compatible instrumentation | Structured, portable logs/metrics/traces |
| Cloud packaging | OCI containers on a managed application/container platform | Low operations burden before Kubernetes is justified |
| Infrastructure | Declarative infrastructure as code, selected before R1 | Repeatability and reviewability |

## Dependency policy

- Prefer platform libraries and maintained first-party packages.
- Add only dependencies necessary to satisfy an approved task.
- Record package name, version, license, maintainer activity, and security rationale.
- A package that performs scanning, packet capture, tunneling, credential handling, or software updating requires explicit review.
- Do not embed or redistribute Nmap/Npcap without separate licensing approval.

## Version rationale

As of July 2026, .NET 10 is the active LTS release and is supported through November 2028. PostgreSQL 18 is a supported major release through November 2030. Pin development images and SDKs to supported major versions, consume current security patches, and avoid preview releases.

## Not required for R0

- Kubernetes or service mesh;
- Kafka or a large event-streaming platform;
- separate time-series database;
- native mobile application;
- packet capture/deep packet inspection;
- public inbound access to the property network;
- cloud video storage.

## Primary references

- OpenAI Codex project instructions: https://learn.chatgpt.com/docs/agent-configuration/agents-md
- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- PostgreSQL versioning policy: https://www.postgresql.org/support/versioning/
