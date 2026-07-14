# Codex Prompt — Review a Completed Task

```text
Review the current branch/diff against AGENTS.md and <TASK_PATH>.
Do not redesign the product or expand scope.

Check specifically for:
1. unmet acceptance criteria;
2. duplicate/retry races;
3. nondeterministic timing tests;
4. tenant-isolation defects;
5. inbound network exposure;
6. secrets or excess LAN data in logs/support output;
7. misleading customer-facing incident language;
8. missing schema/doc/ADR changes;
9. untested failure paths.

Fix confirmed defects only, run the complete required test command, and report findings,
changes, test results, and any residual risk.
```
