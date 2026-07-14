# R0 Task Execution

## Order

1. R0-001 Repository bootstrap
2. R0-002 Shared contracts
3. R0-003 Windows Service shell
4. R0-004 Local SQLite journal/outbox
5. R0-005 Gateway/internet/device probes
6. R0-006 Agent registration and identity
7. R0-007 Heartbeat ingestion
8. R0-008 Lease evaluator/property silence
9. R0-009 Incident state machine
10. R0-010 Notification adapters
11. R0-011 Offline replay/reconciliation
12. R0-012 Failure scenario runner
13. R0-013 Minimal local configuration/status
14. R0-014 End-to-end simulated outage
15. R0-015 Controlled home dogfood pilot

## Task completion rule

A ticket is complete only when:

- all acceptance criteria are demonstrated;
- automated tests pass;
- required docs/schemas are updated;
- no secrets or customer data are committed;
- the completion report identifies assumptions and remaining risks.
