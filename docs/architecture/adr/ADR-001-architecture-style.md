# ADR-001: Architecture Style — Modular Monolith

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture, Project Owner |

## Context

The Platform will be the foundation for every future company system. It has eleven capability areas, serves one company, and will be maintained by a small team over many years. The brief explicitly directs a modular monolith and explicitly prohibits microservices and Kubernetes "just because the project is enterprise".

Two forces pull in opposite directions. The Platform must have genuinely independent modules with clean boundaries — otherwise it becomes the tangle it exists to prevent. But it must not carry the operational cost of a distributed system, which a small team serving one company cannot absorb.

## Problem

What architecture style gives strict module independence without distributed-systems overhead?

## Options

### Option A — Microservices
Each module a separately deployed service with its own database and API.

*Pros:* independent deployment and scaling; strong physical isolation; technology freedom per service.

*Cons:* distributed transactions; network failure between every module boundary; service discovery, orchestration and a message broker to operate; distributed tracing becomes mandatory rather than useful; local development requires running eleven services; a small team spends more time on infrastructure than on the Platform. Every cross-module call becomes a network call that can fail — and Authorization is called on *every single request*.

### Option B — Traditional layered monolith
One application, layered by technical concern (controllers / services / repositories), no module boundaries.

*Pros:* simplest possible; fastest to start; one deployment.

*Cons:* no enforced boundaries, so coupling grows silently; a shared service layer becomes a dumping ground; extraction later is a rewrite. This is precisely the architecture that produces the systems the Platform exists to replace.

### Option C — Modular monolith
One deployable application, internally divided into modules with enforced boundaries, contract-only cross-module references, and one schema per module.

*Pros:* strict boundaries enforced by the compiler and by architecture tests; single deployment and single transaction scope; local development is one process; in-process calls are fast and cannot fail on the network; any module can be extracted later because it has no inbound coupling to its internals.

*Cons:* one deployment unit, so a change anywhere requires redeploying everything; one process, so scaling is all-or-nothing; boundary discipline must be actively enforced or it erodes.

## Decision

**Modular Monolith** (Option C). A single ASP.NET Core deployable containing eleven modules, each with `Contracts`, `Domain`, `Application`, `Infrastructure` and `Api` projects, one PostgreSQL schema per module, and no cross-module reference except to `.Contracts`.

## Reason

The cost of microservices is paid continuously in operations; the benefit is only realised when independent scaling and independent deployment are genuinely needed. Neither is needed here: the Platform serves one company, all modules scale together, and there is no team-per-service structure to justify independent deployment.

Meanwhile, the *actual* requirement — module independence — is fully achievable in a monolith through enforced boundaries. Microservices are one way to get boundaries; they are not the only way, and they are the most expensive way.

The decisive detail: **Authorization is consulted on every request.** In a microservices design that is a network hop on the hot path of the entire company's software. In a modular monolith it is an in-process call against a cached lookup.

## Consequences

**Positive.** One thing to deploy, monitor, back up and debug. Real transactions within a module. Fast local development. No broker, no service mesh, no cluster. Boundaries enforced at compile time, which is stronger than the "boundaries enforced by network" of microservices — a forbidden call does not compile, rather than merely being slow.

**Negative.** Every change redeploys the whole Platform, so deployment must be fast and safe (rolling, health-gated, rehearsed rollback). Scaling is uniform. Boundary discipline requires active enforcement — good intentions are insufficient over years and staff changes. A serious bug can affect the whole process.

**Follow-up actions.**
- Architecture tests asserting the dependency rules, failing the build on violation (Phase 1).
- One schema per module and **no cross-schema foreign keys**, so extraction stays possible (ADR-004).
- Rolling deployment with health checks and a rehearsed rollback (Phase 19).
- Revisit only if a module develops a genuinely different scaling profile — a hypothesis to be tested with measurements, not assumed.

## Status
Accepted.

