# Audit Documentation

Reference: [ARCHITECTURE.md §15](../../ARCHITECTURE.md) · [ARCHITECTURE.md §7.2.5](../../ARCHITECTURE.md).

## Planned documents

| Document | Phase |
|---|---|
| `event-model.md` — every field and its meaning | Phase 6 |
| `integration.md` — **how a business system sends audit events** | Phase 6 |
| `search-and-export.md` | Phase 6 |
| `retention.md` — partitioning, archival, retention periods | Phase 6 |

## Guarantees

- **Append-only.** No update, no delete — enforced by database privileges, not only by the absence of code.
- Every event carries actor, action, resource, time, origin, result and correlation ID.
- The actor's username is stored alongside the ID, so the trail stays readable after a rename.
- No password, token, recovery code or secret is ever recorded, including in old/new values.
- Exporting audit data is itself an audited event.

