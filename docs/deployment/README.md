# Deployment & Operations Documentation

Reference: [ARCHITECTURE.md §20](../../ARCHITECTURE.md) (Cloud), §21 (Deployment), §22 (Observability), §23 (Backup & Recovery).

## Written

| Document | Covers |
|---|---|
| [`observability.md`](observability.md) | Logs, metrics, traces, the seven alert conditions, and the runbook — including how to answer "did last night's sweep run?" |
| [`backup-and-recovery.md`](backup-and-recovery.md) | What is at stake, what to back up, how to restore, how to **verify** a restore, the Platform's own database limits and retention, and the recovery drill |
| [`railway.md`](railway.md) | The current deployment, its environment variables, and the ephemeral-storage trap |

`backup.md` and `recovery-runbook.md` were planned as two documents and written
as one. Splitting them would put the backup procedure in a file nobody opens
during an incident and the recovery procedure in a file nobody reads while
setting the backup up — and the whole argument of that document is that the two
are one subject, because a backup is only the input to a restore.

## Planned documents

| Document | Phase |
|---|---|
| `environments.md` — local, development, staging, production | Phase 1 |
| `ci-cd.md` — the pipeline and its gates | Phase 1 |
| `docker.md` — images, compose, local parity | Phase 1 |
| `alerts.md` — every alert and the action it requires | Phase 14 — written into `observability.md` §6 instead; the alerts are not yet deployed anywhere (Q4) |
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
