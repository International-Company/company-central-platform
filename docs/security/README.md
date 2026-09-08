# Security Documentation

Reference: [ARCHITECTURE.md §12](../../ARCHITECTURE.md) (Security), §13 (Authentication), §14 (Authorization).

## Documents

| Document | Phase | Status |
|---|---|---|
| [`authentication.md`](authentication.md) — flows, tokens, sessions, key rotation | 2 | Written |
| [`mfa.md`](mfa.md) — TOTP, recovery codes, secret storage, step-up | 5 | Written |
| [`rate-limiting.md`](rate-limiting.md) — per-endpoint classes and partitioning | 5 | Written |
| [`secrets-management.md`](secrets-management.md) — where secrets live per environment, and rotation | 5 | Written |
| [`security-headers.md`](security-headers.md) — headers, CORS, and the bug the design prevents | 5 | Written |
| `threat-model.md` | 2 | **Outstanding** — carried from Phase 2 |
| `password-policy.md` | 2 | **Outstanding** — the policy is implemented and tested; the document is not written |
| `incident-response.md` | 21 | Planned |

## Standing rules

- **No secret in source control.** Not in configuration, not in a test fixture, not in a comment, not in an example. CI fails the build on a detected credential.
- Every endpoint is authenticated and authorized unless explicitly and deliberately anonymous.
- Frontend authorization is UX. Backend authorization is security. Only the backend is trusted.
- Passwords are Argon2id. Refresh tokens are stored hashed. Neither is ever returned by any endpoint.
- Security findings are not deferred to meet a date. Scope is negotiable; security is not.

