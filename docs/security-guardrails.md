# Security and Privacy Guardrails

## Trust boundaries

1. Untrusted/partially trusted home LAN.
2. Local Windows host and service identity.
3. Public internet.
4. Cloud application boundary.
5. Third-party messaging providers.
6. Restricted support/administrative boundary.

## Required controls for R0

- Per-agent credentials issued through a single-use enrollment token.
- Credential revocation and replacement.
- TLS for every agent/cloud request.
- Authorization checks by organization/property/agent on every request.
- Constant-time or framework-standard secret comparison.
- Secrets stored using Windows-bound protected storage and restrictive ACLs.
- Logs redact bearer tokens, enrollment tokens, camera URLs, usernames, passwords, phone numbers where not operationally required, and connection-string passwords.
- Provider webhook signatures verified before delivery state changes.
- Tenant-isolation tests for all customer-facing queries and writes.
- No generic remote command execution. Any command queue is typed, capability-scoped, expiring, and auditable.
- No camera credential upload in R0.
- No video, audio, or snapshot upload in R0.

## Security failure behavior

- Invalid or expired credentials fail closed.
- A malformed configuration does not replace last-known-good configuration.
- A failed update signature blocks installation.
- If secrets cannot be unprotected, monitoring enters visible degraded state rather than deleting/recreating them silently.
- Cloud/provider failure must not be presented as a property outage without correlation and platform-health checks.

## Required security review questions

- Could this change expose the customer LAN?
- Could it allow one tenant to access another tenant?
- Could retries duplicate an alert or incident?
- Could a log/support bundle disclose a credential or detailed LAN inventory?
- Could an attacker stop monitoring without producing an observable event?
- Could customer-facing text overstate evidence?
