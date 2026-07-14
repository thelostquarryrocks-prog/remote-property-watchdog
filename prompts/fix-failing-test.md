# Codex Prompt — Diagnose a Failing Test

```text
Investigate the failing test(s) below without weakening the expected behavior:
<PASTE FAILURE>

Read the relevant task, contract, and state-machine documentation. Determine whether the
failure is in implementation, test setup, clock control, race handling, or the stated contract.
Do not delete assertions, increase arbitrary sleeps, or broaden retry windows merely to make the test pass.
Apply the smallest correct fix, run the focused test repeatedly, then run the full suite.
Report root cause, fix, commands, and evidence of determinism.
```
