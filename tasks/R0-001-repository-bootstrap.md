# R0-001 — Repository bootstrap

Priority: P0  
Release: R0  
Depends on: None

## Objective

Create a compiling, testable .NET 10 solution and development workflow that respects the package boundaries.

## In scope

- Create solution/projects for shared contracts, agent service, cloud host, cloud domain/application/infrastructure modules, and test projects.
- Add deterministic build settings, analyzers, basic health endpoints, and local PostgreSQL startup instructions.
- Extend CI to restore, build, test, validate schemas, and scan for accidental secrets.

## Out of scope

- Business monitoring logic.
- Real provider credentials.
- Kubernetes or production infrastructure.

## Implementation notes

- Prefer project references with dependency direction documented in `docs/system-context.md`.
- Keep cloud deployable as one process plus workers.
- Add a `make` equivalent via PowerShell scripts rather than requiring global tools beyond .NET, Docker, and PowerShell.

## Acceptance criteria

- `dotnet build` succeeds with warnings treated as errors.
- `dotnet test` runs at least one smoke test per executable project.
- `pwsh ./scripts/test.ps1` succeeds from a clean checkout.
- CI workflow has no secret values and passes on pull requests.
- README contains exact Windows developer prerequisites and startup commands.

## Required tests

- Build/test script test.
- Cloud health endpoint integration test.
- Agent host construction smoke test.
- JSON schema validation test.

## Required evidence

- Console output for restore/build/test.
- Project dependency map.
- Any added package inventory with license rationale.

## Codex completion report

Use the completion report in `AGENTS.md` and do not begin another task.
