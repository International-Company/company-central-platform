# Deployment & Operations Documentation

Reference: [ARCHITECTURE.md §20](../../ARCHITECTURE.md) (Cloud), §21 (Deployment), §22 (Observability), §23 (Backup & Recovery).

## Planned documents

| Document | Phase |
|---|---|
| `environments.md` — local, development, staging, production | Phase 1 |
| `ci-cd.md` — the pipeline and its gates | Phase 1 |
| `docker.md` — images, compose, local parity | Phase 1 |
| `observability.md` — logs, metrics, traces, dashboards | Phase 14 |
| `alerts.md` — every alert and the action it requires | Phase 14 |
| `backup.md` — what is backed up, how, and how often | Phase 17 |
| **`recovery-runbook.md`** — exact commands for each recovery scenario | Phase 17 |
| `cloud-infrastructure.md` | Phase 19 |
| `release-procedure.md` — deploy, verify, roll back | Phase 19 |
| `go-live-checklist.md` | Phase 21 |

## Standing rules

- **Production is never a development environment.**
- Staging mirrors production configuration and contains **no real personal data**.
- Migrations run as an explicit deployment step, preceded by a backup — never automatically at application start.
- The same container image is promoted through environments; only configuration differs.
- Secrets come from the secret manager. They are never in an image, a build argument, or the repository.
- A backup that has never been restored is a hypothesis. Restore drills are scheduled, timed against the RTO, and their results recorded.
