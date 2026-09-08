# ADR-013: Background Jobs & Asynchronous Processing

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture |

## Context

Several Platform capabilities need work done outside the request: dispatching audit events, sending notifications, retrying integration calls, escalating workflow SLAs, creating audit partitions, and purging expired data.

Two properties matter. Audit and notification dispatch must be **durable** — an event committed with a transaction must not be lost. And the API must be able to run on more than one instance without work being executed twice or not at all.

## Problem

What mechanism executes durable background work, without adding infrastructure that is not needed?

## Options

### Option A — Hangfire (PostgreSQL storage)
*Pros:* mature; durable; retries; a dashboard; scheduling.
*Cons:* an additional dependency with its own schema and conventions; its dashboard is another surface to secure; more than is needed for what is mostly event dispatch.

### Option B — Quartz.NET
*Pros:* powerful scheduling; clustering support.
*Cons:* configuration-heavy; oriented to scheduling rather than to durable event dispatch, which is the actual dominant need.

### Option C — A transactional outbox plus hosted `BackgroundService` workers
Events are written to an outbox table **in the same transaction** as the change; a background worker claims rows using `SELECT ... FOR UPDATE SKIP LOCKED` and dispatches them.

*Pros:* no new infrastructure or dependency; durable by construction — the event and the change commit or roll back together; multi-instance safe through `SKIP LOCKED`; fully testable; the outbox is a plain table that can be inspected and queried when debugging.
*Cons:* retry, backoff, dead-lettering and monitoring must be implemented rather than inherited; no dashboard; no cron scheduling.

### Option D — A message broker (RabbitMQ, Azure Service Bus)
*Cons:* new infrastructure to operate for in-process module communication. Directly contrary to P1 and to the brief's prohibition on unnecessary distributed infrastructure.

## Decision

**Option C.** A transactional outbox in the `kernel` schema, dispatched by hosted `BackgroundService` workers using `FOR UPDATE SKIP LOCKED`, with retry, exponential backoff with jitter, a maximum attempt count, dead-lettering, and metrics on queue depth and dispatch lag.

Simple recurring maintenance (partition creation, purges) runs as timed hosted services with a database-based lease so that only one instance executes each run.

**A scheduling framework (Quartz.NET) will be adopted only when genuine cron-style scheduling requirements appear** — likely around Phase 14 — and that adoption requires an ADR amending this one. A message broker is adopted only if cross-service messaging genuinely comes to exist.

## Reason

The dominant requirement is durable event dispatch, and the outbox pattern solves that better than a job framework does — because it is transactional with the originating change. A job enqueued after a commit can be lost if the process dies in between; an outbox row committed *with* the change cannot.

The remaining need is periodic maintenance, which a timed hosted service with a lease handles in a few dozen lines.

`FOR UPDATE SKIP LOCKED` is the detail that makes this safe with multiple instances, and it is a standard PostgreSQL feature rather than something clever we invented.

Adding Hangfire would mean carrying a dependency, a schema and a dashboard for capability we would mostly not use. If real scheduling requirements appear later, adopting a framework then is a small, well-scoped change — and by then we will know what we actually need, rather than guessing now.

## Consequences

**Positive.** No new infrastructure. Durable by construction. Multi-instance safe. The outbox is inspectable with plain SQL, which makes debugging straightforward. Fully testable, including concurrency, with Testcontainers.

**Negative.** Retry, backoff, dead-lettering and monitoring are ours to implement and get right — an outbox implemented subtly wrong loses events silently, which is the worst possible failure for an audit system. No dashboard; visibility comes from the Monitoring module instead. No cron scheduling, so complex schedules would need the framework this ADR defers.

**Follow-up actions.**
- Implement the outbox in Phase 1, with explicit concurrent-dispatch integration tests using multiple workers — this is the highest-risk piece of the kernel.
- Expose outbox depth and dispatch lag as metrics, and alert on growth (Phase 14).
- Revisit if genuine scheduling requirements appear; amend this ADR rather than adding a framework quietly.

## Status
Accepted.

