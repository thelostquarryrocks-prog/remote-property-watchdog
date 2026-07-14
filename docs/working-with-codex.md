# Working with Codex on This Repository

## Repository guidance

Codex reads `AGENTS.md` before work and can layer instructions from the repository root down to the working directory. Keep root guidance stable and use task files for the details of one implementation unit. Avoid turning `AGENTS.md` into the entire product specification.

Official guidance: https://learn.chatgpt.com/docs/agent-configuration/agents-md

## Recommended task rhythm

1. Assign exactly one `tasks/R0-XXX-*.md` file.
2. Ask Codex to inspect the repository and provide a compact plan.
3. Let it implement and test the task.
4. Review the diff and acceptance evidence.
5. Request a focused correction if an acceptance criterion is incomplete.
6. Commit only after tests and security/privacy review pass.

## Parallel work

Do not parallelize foundational interface work prematurely.

- Sequential first: R0-001 through R0-009.
- Cautiously parallel after contracts stabilize: R0-010, R0-011, R0-012, and R0-013.
- Integration gate: R0-014.
- Physical pilot: R0-015.

Use separate branches/worktrees for parallel tasks and require each branch to rebase and pass the complete test command before merge.

## Review priorities

For every Codex change, check:

- Did it change scope beyond the task?
- Does it expose an inbound network surface?
- Can retries duplicate state or messages?
- Are timing tests deterministic?
- Can one tenant access another?
- Are secrets absent from logs/tests?
- Does customer text separate facts from interpretations?
- Did the change update contracts/ADRs where needed?

## A useful correction prompt

```text
Review the current diff only against the assigned task and its acceptance criteria.
Identify any missing behavior, insecure shortcut, nondeterministic test, scope expansion,
or customer-facing wording that overstates evidence. Fix only confirmed gaps, run the
full required test command, and provide a criterion-by-criterion completion report.
```
