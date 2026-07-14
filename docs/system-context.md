# System Context

```text
PROPERTY / HOME NETWORK                           CLOUD CONTROL PLANE

Cameras / DVR / Router / Switch
           | local probes
           v
+-----------------------------+      outbound HTTPS      +--------------------------+
| Windows Monitoring Agent    | -----------------------> | Agent API                 |
| - probe scheduler           |                          | - registration            |
| - local state machine       |                          | - heartbeat ingestion     |
| - SQLite journal/outbox     |                          | - event batch ingestion   |
| - encrypted local secrets   |                          +------------+-------------+
| - loopback status UI        |                                       |
+-----------------------------+                                       v
                                                       +----------------------------+
                                                       | Workers / Domain Modules   |
                                                       | - heartbeat lease evaluator|
                                                       | - incident correlation     |
                                                       | - notification orchestration|
                                                       | - restoration/replay merge |
                                                       +-------------+--------------+
                                                                     |
                                  +------------------+---------------+--------------+
                                  |                  |                              |
                                  v                  v                              v
                           PostgreSQL          Email/SMS adapters             Internal status UI
```

## Authority split

The local agent is authoritative for observations made inside the property network. The cloud is authoritative for whether the agent is still checking in and for remote notification delivery.

When internet service disappears, the cloud initially knows only that the property agent is silent. After recovery, the agent uploads locally buffered evidence so the cloud can refine the incident.

## Deployment shape

- Agent: Windows Service plus loopback-only configuration/status UI.
- Cloud: modular monolith ASP.NET Core API plus one or more hosted/background workers.
- Persistence: SQLite locally; managed PostgreSQL in cloud.
- Messaging: internal provider-neutral adapters.
- No Kubernetes, service mesh, Kafka, public LAN listener, or reverse tunnel in R0.


## R0-001 project dependency map

```text
RemotePropertyWatchdog.Agent.Service
  -> RemotePropertyWatchdog.Shared.Contracts

RemotePropertyWatchdog.Cloud.Host
  -> RemotePropertyWatchdog.Cloud.Application
  -> RemotePropertyWatchdog.Cloud.Infrastructure

RemotePropertyWatchdog.Cloud.Infrastructure
  -> RemotePropertyWatchdog.Cloud.Application

RemotePropertyWatchdog.Cloud.Application
  -> RemotePropertyWatchdog.Cloud.Domain
  -> RemotePropertyWatchdog.Shared.Contracts

RemotePropertyWatchdog.Cloud.Domain
  -> no project references

RemotePropertyWatchdog.Shared.Contracts
  -> no project references

RemotePropertyWatchdog.UnitTests
  -> RemotePropertyWatchdog.Agent.Service

RemotePropertyWatchdog.IntegrationTests
  -> RemotePropertyWatchdog.Cloud.Host

RemotePropertyWatchdog.ContractTests
  -> RemotePropertyWatchdog.Shared.Contracts

RemotePropertyWatchdog.BuildTests
  -> repository scripts only
```

The cloud remains deployable as one ASP.NET Core host process with modular domain, application, and infrastructure projects. The bootstrap does not add business monitoring logic, inbound LAN exposure, tunnels, or provider credentials.
