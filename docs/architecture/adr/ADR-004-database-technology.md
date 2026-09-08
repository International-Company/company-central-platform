# ADR-004: Database Technology — PostgreSQL

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture, Project Owner |

## Context

The brief specifies PostgreSQL with EF Core and migrations. What remains to decide is how the Platform's data is organised: one database or many, and how module isolation is achieved.

The Platform has eleven modules that must be logically independent (P5) and potentially extractable (ADR-001). The audit table will be large — potentially hundreds of millions of rows over years — while most other tables stay small.

## Problem

How should Platform data be organised so that modules are genuinely isolated without the operational cost of many databases?

## Options

### Option A — One database, one shared schema
*Pros:* simplest; joins anywhere.
*Cons:* no isolation whatsoever; nothing prevents a cross-module join; table name collisions; extraction is a rewrite. This defeats ADR-001.

### Option B — One database, one schema per module
*Pros:* clear ownership; per-module migration history; one backup, one connection pool, one restore; database-enforced separation via schema privileges; extraction is straightforward because a schema with no inbound foreign keys can be dumped and moved.
*Cons:* cross-module queries require application-level joins; the temptation to write a cross-schema join must be actively resisted.

### Option C — One database per module
*Pros:* maximum isolation.
*Cons:* eleven databases to back up, monitor, migrate and restore, with no transaction spanning any two of them — all the cost of microservices data isolation while still deploying a monolith. The worst of both.

## Decision

**PostgreSQL 17, one database, one schema per module** (Option B), with EF Core, one migration history table per schema, and one binding rule:

> **No foreign key crosses a module schema boundary.** Cross-module references store the ID only; integrity is enforced in the application layer through the owning module's contract.

Additional decisions: UUID v7 primary keys (time-ordered, so they index well; opaque, so they are safe in URLs); `timestamptz` in UTC everywhere; `snake_case` naming; hard delete by default with soft delete only where a requirement genuinely exists; monthly range partitioning for `audit.audit_events`.

## Reason

Schema-per-module gives the isolation the architecture requires at almost no operational cost. Option C's isolation is not meaningfully better for this system, but its operational cost is many times higher.

The no-cross-schema-foreign-key rule is the load-bearing part of this decision. It is what makes P5 real: a schema with no inbound foreign keys can be moved to its own database in an afternoon; a schema tangled in cross-module foreign keys never can. The rule has a genuine cost — some integrity checks move to application code, and some queries need two round trips instead of one join — and that cost is accepted deliberately, because the alternative is a boundary that exists only in documentation.

UUID v7 over sequential integers: sequential IDs in URLs leak volume and enable enumeration. UUID v7 over UUID v4: v4's randomness causes index fragmentation and poor insert locality, which matters a great deal on a table with hundreds of millions of rows.

## Consequences

**Positive.** Clear data ownership per module. Independent per-module migrations. One backup and one restore procedure. Database-level privilege separation — which is what makes the audit schema's append-only guarantee real rather than a promise in code. Partitioning handles audit growth. PostgreSQL's JSONB serves the genuinely open metadata fields without abandoning relational modelling elsewhere.

**Negative.** No cross-module joins, so some read paths need two queries and some aggregation happens in application code. Referential integrity across modules is the application's responsibility, and application-enforced integrity is weaker than database-enforced integrity. The team must resist a tempting cross-schema join under deadline pressure — this needs an automated check, not discipline alone.

**Follow-up actions.**
- An automated assertion that no foreign key crosses a schema boundary (Phase 1, verified in Phase 17).
- Audit partitioning and a partition-creation maintenance job (Phase 6).
- Least-privilege database roles, including INSERT/SELECT-only for the audit schema (Phase 6, verified Phase 17).
- Migrations applied as an explicit deployment step, never automatically at application start.

## Status
Accepted.

