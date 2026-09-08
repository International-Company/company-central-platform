# Development Documentation

## Planned documents

| Document | Phase |
|---|---|
| `getting-started.md` — clone to running system | Phase 1 |
| `local-environment.md` — Docker Compose, configuration, seed data | Phase 1 |
| `coding-standards.md` | Phase 1 |
| `testing.md` — what to test at which level, and how to run the suites | Phase 1 |
| `review-checklist.md` | Phase 1 |
| `adding-a-module.md` | Phase 2 |
| `frontend-guide.md` — design system, i18n, forms, tables | Phase 7 |
| `configuration.md` | Phase 13 |
| **`integration-guide.md`** — **how to connect a business system to the Platform** | Phase 11 |
| `troubleshooting.md` | Phase 18 |

## Prerequisites (verified 2026-09-06)

| Tool | Required | Status on this machine |
|---|---|---|
| .NET SDK | 10 (LTS) | ❌ **Not installed** |
| Node.js | 22+ | ✅ 24.18 |
| Docker | Any recent | ✅ 29.6 |
| Git | Any recent | ✅ 2.54 |
| PostgreSQL | 17 | Provided by Docker Compose |

## Non-negotiables

- No business logic in endpoints.
- No secret in source control.
- No hardcoded user-facing text.
- No endpoint without an explicit permission or an explicit anonymous marker.
- No cross-module reference except to `.Contracts`.
- Tests accompany the change, not a later phase.

