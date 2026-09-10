# Backup and recovery

**A backup nobody has restored from is a belief, not a backup.** Everything below
is written so that the restore is the thing that gets practised, because the
restore is the thing that will one day be needed and the backup is only its
input.

---

## 1. What is actually at stake

The Platform holds four kinds of data, and they do not carry the same weight.

| Data | Where | Losing it means |
|---|---|---|
| Audit trail | `audit.audit_events` | The company cannot answer "who changed this". It is append-only and legally interesting, and it is the one thing here that **cannot be reconstructed from anywhere else**. |
| Identity, roles, organization | `identity`, `authz`, `organization` | Nobody can sign in and nobody has any access. Recoverable by hand, slowly, if somebody remembers the structure. |
| Documents metadata | `documents` | The bytes are in object storage and the rows say what they are, who may see them and which version is current. **The bytes without the rows are unreadable** — the object keys are random by design. |
| Operational | `kernel`, `integrations`, `notifications`, `configuration`, `workflow` | Work in flight is lost; the Platform still functions. |

**The documents case is the one people get wrong.** Object storage is usually
backed up separately and on a different schedule from the database. If the two
are restored to different points in time, the result is rows referring to objects
that do not exist and objects nothing refers to. Restore both to the same
instant, or accept and record which way the mismatch runs.

---

## 2. What must be backed up

1. **The PostgreSQL database**: eleven module schemas plus `kernel`. They live in
   one database, so this is one backup and there is no cross-schema consistency
   problem to solve.
2. **The object storage bucket** holding document content.
3. **The secrets**, which are *not* in either of the above and never will be —
   they are held by the platform's secret store or environment. A perfect
   database restore into an environment with no `CCP_Security__ProtectionKey`
   yields a Platform where **every enrolled second factor is undecryptable**.
   This is the failure most likely to be discovered during a real recovery, and
   the reason it is item three rather than a footnote.
4. **The migration history**, which is inside the database (`__ef_migrations_history`
   per schema) and therefore comes with it. Worth knowing it exists, because a
   restore to an older point in time means the application binary may be ahead of
   the schema.

---

## 3. Taking a backup

```bash
pg_dump \
  --format=custom \
  --no-owner \
  --no-privileges \
  --file="ccp-$(date -u +%Y%m%dT%H%M%SZ).dump" \
  "$DATABASE_URL"
```

`--format=custom` because it can be restored selectively and in parallel;
`--no-owner` and `--no-privileges` because the restore target's roles are its own
business and a dump that insists on the source's role names fails on a fresh
database.

**Point-in-time recovery needs more than this.** `pg_dump` gives a single
instant. Recovering to "just before the deletion at 14:32" requires continuous
archiving, which every managed PostgreSQL offers as a product feature and which
is the reason to use one. Whichever provider is chosen (Q4), the retention window
and the recovery point objective are a decision to be written down here, not
inherited silently from a default.

---

## 4. Restoring

```bash
createdb ccp_restored
pg_restore --dbname=ccp_restored --no-owner --jobs=4 ccp-20260910T031500Z.dump
```

Then point a Platform at it with migrations **off** and check `/health/ready`.

**Restore into a new database, never over the live one.** A restore that
overwrites is a restore that cannot be abandoned when it turns out the dump was
older than expected.

---

## 5. Verifying a restore

`scripts/verify-restore.sh` answers the question the dump file cannot: *is this
thing usable?* It restores into a scratch database and asserts what has to be
true.

```bash
scripts/verify-restore.sh ccp-20260910T031500Z.dump
```

It checks:

| Check | Why it is the one worth checking |
|---|---|
| Every module schema exists | A partial restore is the common failure, and it looks like success until somebody uses the missing module. |
| `audit.audit_events` is still **range-partitioned** | Partitioning is a table property. A restore that flattened it would work perfectly and quietly break retention, which is only noticed years later. |
| Its partitions came back | The parent surviving without its children is a table that accepts nothing. |
| At least one user and one role exist | An empty `identity` restores cleanly and locks everybody out for ever. This is the check that distinguishes "the file restored" from "the company can come back". |
| Every migration history is present | Otherwise the application will try to re-apply migrations onto a schema that already has them. |

**Run it on a real backup, on a schedule, and treat a failure as an incident.**
An untested backup is a plan to find out during the disaster.

---

## 6. What the Platform does for itself

These do not replace backups. They are what keeps the database healthy enough
that a backup of it is worth having.

**Statement and idle-transaction timeouts** are applied to the connection string
in one place (`ConnectionHardening`), so all twenty-two `UseNpgsql` call sites
inherit them rather than twenty-two people remembering.

| Limit | Value | What it prevents |
|---|---|---|
| `statement_timeout` | 60s | One runaway query holding a connection until the process restarts, and enough of them exhausting the pool and stopping the Platform on behalf of one endpoint. |
| `idle_in_transaction_session_timeout` | 60s | The real killer: an abandoned open transaction holds its locks *and* stops `VACUUM` reclaiming any row version newer than itself, so tables bloat and, at the extreme, transaction id wraparound protection starts refusing writes across the whole database. |
| Connect timeout | 10s | One slow dependency becoming a queue of held request threads. |

**The migrator is exempt from the server-side timeouts, deliberately.** Creating
an index on a large table is minutes of honest work, and a schema change killed
half-way through by a limit meant for web requests is worse than what the limit
prevents. Its advisory-lock connection is exempt too, and that is the subtler
half: `pg_advisory_lock` *blocks* until granted, a statement timeout applies to a
blocking statement, and a second instance queuing behind a long migration would
have its wait cancelled and then serve requests against a half-migrated schema.

**Retention runs on its own** so no table grows without bound:

| Table | Kept | Swept by |
|---|---|---|
| `kernel.outbox_messages` (delivered) | 7 days | `kernel.outbox-retention` |
| `kernel.outbox_messages` (dead-lettered) | **for ever** | nothing — see below |
| `kernel.job_runs` | 30 days | the journal, on write |
| `audit.audit_events` | by partition | `audit.partitions` |
| `integrations.calls` | configured | `integrations.retention` |
| Documents marked for deletion | 30-day grace | `documents.purge` |

**Dead-lettered outbox rows are never swept.** A dead letter means an event will
never be delivered — an audit entry or a notification permanently missing — and a
timer that quietly erased those would erase the evidence of the one failure this
whole mechanism exists to make visible. They stay until a person deals with them,
and the **Operations** screen counts them.

Every sweep above appears on that screen with its last run, what it did, and
which instance ran it.

---

## 7. Least privilege at the database

The application connects as a role that needs no ownership of anything it does
not write. Two grants are already narrower than the rest:

- `audit.audit_events` is granted **INSERT and SELECT only**, enforced by the
  database rather than by the code that promises not to update it. There is an
  integration test asserting the grant, and it `SET ROLE`s to an ordinary role
  first — because CI connects as a superuser, and a superuser bypasses privilege
  checks entirely, so testing this the obvious way proves nothing.
- DDL is only needed while migrating. Where the deployment allows it, run
  migrations as a role with schema-modification rights and serve requests as one
  without, so an SQL-injection defect cannot become a schema change.

Recorded as an open item rather than claimed: the second half is documented and
**not enforced** — the Platform currently connects with one role for both. See
the technical debt register.

---

## 8. Recovery drill

The drill worth running, in order:

1. Take a fresh dump from production.
2. `scripts/verify-restore.sh` it. If this fails, stop; there is no backup.
3. Restore it into a scratch environment **with the secrets deliberately
   absent**, and confirm the Platform reports the failure clearly rather than
   starting and misbehaving. This is the step people skip and the one that
   matters most.
4. Supply the secrets, restore the object storage bucket to the same instant, and
   sign in.
5. Open a document. If the bytes are missing, the two restores were not to the
   same point in time, and now is when you want to learn that.

Write down how long steps 1 to 5 took. That number is the recovery time
objective, and until the drill has been run it is a guess.
