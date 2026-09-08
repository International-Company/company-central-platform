# Workflow Documentation

Reference: [ARCHITECTURE.md §16](../../ARCHITECTURE.md) · [ARCHITECTURE.md §7.2.6](../../ARCHITECTURE.md).

## Planned documents

| Document | Phase |
|---|---|
| `definition-schema.md` — the JSON schema for a workflow definition | Phase 8 |
| `integration.md` — **how a business system registers a definition and starts an instance** | Phase 8 |
| `assignee-resolution.md` — the available strategies | Phase 8 |
| `sla-and-escalation.md` | Phase 8 |

## The boundary

The engine understands states, transitions, assignees and timers. It does **not** understand what is being approved.

A rule like "amounts above 5,000 require the finance director" is a business rule. It lives in the business application, which either supplies the next step explicitly or answers a callback asking what comes next. **The engine holds no thresholds.** See [ADR-005](../architecture/adr/ADR-005-platform-business-boundary.md).

