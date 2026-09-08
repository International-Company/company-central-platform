# Audit

Reference: [ARCHITECTURE.md §15](../../ARCHITECTURE.md). Built in Phase 6.

One immutable trail, spanning every company system, answering: **who did what, to
what, when, from where, and what changed.**

---

## 1. Audit is not the security event log

They were built in consecutive phases and are deliberately separate.

| | Audit (`audit.audit_events`) | Security events (`security.security_events`) |
|---|---|---|
| Answers | *Who changed what* | *What is being attempted* |
| Reader | Compliance, a manager settling a dispute | Whoever watches for attacks |
| Lifetime | Years, by retention policy | Months |
| Mutability | **Append-only, enforced by privilege** | Ordinary table |
| Scope | Every company system | The Platform |

Merging them would mean either keeping attack noise for seven years or throwing
away evidence after ninety days. Neither is right.

---

## 2. The event

Every record carries the fields in ARCHITECTURE.md §15.2. Two deserve
explanation.

**`occurred_at` is the time the thing happened, not the time it was written**,
and it is the partition key. Audit writes travel asynchronously and land a moment
later; recording the write time would misorder events under load and file an
event in the wrong month at a boundary.

**`actor_username` is stored beside `actor_user_id`, denormalized on purpose.** A
trail that renders as "user 8f3a… did X" after an employee record changes is not
usable evidence. The name is captured as it stood at the time, which is also what
a later reader needs.

---

## 3. Immutability, actually enforced

Append-only is a claim that most systems make and few can demonstrate. Here it
rests on two independent things:

1. **No update path in code.** `IAuditRepository` has `AppendAsync` and
   `SearchAsync`. There is no update and no delete, and `AuditEvent` exposes no
   mutation.
2. **No privilege to update in the database.** The migration revokes everything
   on the `audit` schema from the application role and grants back `INSERT` and
   `SELECT` only — including on tables created later, through
   `ALTER DEFAULT PRIVILEGES`.

Either alone is insufficient. Code discipline without the privilege is a promise
that survives exactly as long as nobody opens `psql`. The privilege without the
discipline is an accident waiting to be made.

> **Deployment check.** The migration tightens privileges for `current_user` and
> emits a `WARNING` rather than failing if it lacks the authority to do so — a
> schema must still be creatable by a restricted role. **If your deployment runs
> migrations as a different role from the application, grant `INSERT, SELECT`
> only, by hand, and confirm it.** Until you have confirmed it, append-only is a
> promise here, not a guarantee.

Retention removes whole partitions with `DROP`. It never edits a row.

---

## 4. Partitioning

`audit.audit_events` is `PARTITION BY RANGE (occurred_at)`, one partition per
month, plus a `DEFAULT` partition.

`AuditPartitionMaintenance` creates the coming months on startup and daily
thereafter — three ahead by default, and one month behind for late arrivals. A
partitioned table **rejects** a row with no partition to hold it, and the first
minute of a new month is the worst possible time to discover that.

The `DEFAULT` partition exists so that a late maintenance job costs a misfiled
row rather than a lost event. **Rows appearing there are a signal that
maintenance is behind**, and worth an alert.

`occurred_at` is part of the primary key because PostgreSQL requires the
partition key in every unique constraint on a partitioned table.

---

## 5. Redaction

Secrets are removed **before** storage (§15.7). Redaction on the way out would
leave the secret sitting in a table nobody can go back and clean.

**Deny by field name, not by value.** Recognising a secret by looking at it is
guesswork — a password can be any string. A field called `password` holds one
whatever it contains.

Names are matched as case-insensitive substrings, so `newPassword`,
`password_hash` and `PasswordConfirmation` are all caught without anyone
enumerating them. A sensitive name redacts the **whole subtree**: an object called
`credentials` holds nothing worth keeping, and descending into it to redact field
by field would preserve exactly the structure an attacker wants.

Over-redacting an innocent field is a small loss. Under-redacting a credential is
a breach.

---

## 6. Writing to the trail

### From inside the Platform

```csharp
await auditRecorder.RecordAsync(AuditEvent.Record(
    application: "platform",
    module: "identity",
    action: "user.created",
    occurredAt: clock.UtcNow,
    resourceType: "user", resourceId: id.ToString(),
    actorUserId: actor, actorUsername: actorName,
    newValue: serializedUser));
```

**A failure here is logged, never thrown.** An audit write must not fail the
operation it records (§15.6) — refusing a legitimate role grant because the trail
was briefly unwritable trades a real capability for a record nobody asked to
prioritise that way.

That is a genuine trade, not a free one: a swallowed failure is a lost event. It
is logged at error level precisely so "the trail is behind" is an alert rather
than a silence.

### From a business application

```http
POST /api/v1/audit/events          # one
POST /api/v1/audit/events/batch    # up to 500
```

Requires `platform.audit.write`.

**The application is taken from the caller's identity, never from the request
body.** A caller able to name its own application could write entries attributed
to Finance or HR, and a trail anyone can forge is evidence of nothing.

An event dated more than five minutes in the future is **refused, not corrected**
— a broken clock or a forgery, and quietly rewriting the timestamp would erase
the evidence of either. Batches are all-or-nothing: a partial accept leaves the
caller unable to say what was recorded, and retrying would duplicate it.

---

## 7. Reading

```http
GET /api/v1/audit/events?from=…&to=…&application=…&actorUserId=…
```

Requires `platform.audit.view`.

**The date range is required and capped at 90 days.** It is not defaulted: a
caller who omitted the dates would otherwise believe they had searched
everything, which in an investigation is worse than an error. The range is also
what lets PostgreSQL prune partitions — the difference between reading three
months and reading everything ever recorded.

Every filter offered is backed by a composite index leading with the filter and
ending with `occurred_at`. **Adding a filter without adding an index turns a
bounded query into a scan of a table designed to grow forever.**

---

## 8. Not built in Phase 6

Recorded rather than implied:

- **Asynchronous signed export.** Planned task 7. Until it exists, the 90-day
  search cap has no escape hatch for a wider investigation.
- **Retrofit across Phases 2–5.** Planned task 10. The module is ready and the
  seam exists, but Identity, Organization, Authorization and Security do not yet
  call it — so **the trail is currently empty of Platform activity**. This is the
  largest gap in the phase.
- **Retention and archival by partition detach.** Partitions exist; nothing yet
  detaches or archives them.
- **The outbox consumer.** Events are written directly rather than ridden along
  with the transaction that produced them.
- **Search performance against 10 million rows.** Untested at that scale.
