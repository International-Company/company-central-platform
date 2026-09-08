# Architecture Decision Records

An ADR records a decision that shapes the system: the context it was made in, the options considered, what was chosen, why, and what it costs. It is written when the decision is made, not reconstructed afterwards.

## Why we keep them

Two years from now, someone will ask "why is this a modular monolith?" or "why are there no foreign keys between schemas?" Without an ADR, the answer is guesswork, and the usual outcome is that a well-reasoned decision gets reversed by someone who never saw the reasoning.

## When to write one

Write an ADR when a decision:
- constrains what can be built later, or is expensive to reverse;
- affects more than one module;
- picks between real alternatives with real trade-offs;
- is not stated in the requirements and therefore had to be decided.

Do **not** write one for routine implementation choices with an obvious answer.

## Format

Every ADR contains: **Context · Problem · Options · Decision · Reason · Consequences · Status**.

## Status values

| Status | Meaning |
|---|---|
| Proposed | Under discussion |
| Accepted | In force |
| Superseded by ADR-NNN | Replaced; kept for the record |
| Deprecated | No longer applies |

An ADR is never deleted or edited to say something different. A changed mind produces a new ADR that supersedes the old one.

## Owner-level decisions

Some decisions belong to the project owner, not the engineering team: the frontend stack (ADR-003), the visual design direction (ADR-010), the cloud provider (ADR-009), the database (ADR-004), and the platform/business boundary (ADR-005). Changing one of these requires the process in [ARCHITECTURE.md §26.3](../../../ARCHITECTURE.md) **and** the owner's approval.

## Index

| ADR | Title | Status |
|---|---|---|
| [ADR-001](ADR-001-architecture-style.md) | Architecture Style — Modular Monolith | Accepted |
| [ADR-002](ADR-002-backend-technology.md) | Backend Technology — .NET 10 LTS / ASP.NET Core | Accepted |
| [ADR-003](ADR-003-frontend-technology.md) | Frontend Technology — Next.js / React / TypeScript | Accepted |
| [ADR-004](ADR-004-database-technology.md) | Database Technology — PostgreSQL | Accepted |
| [ADR-005](ADR-005-platform-business-boundary.md) | Platform / Business Boundary | Accepted |
| [ADR-006](ADR-006-authentication-strategy.md) | Authentication Strategy | Accepted |
| [ADR-007](ADR-007-authorization-strategy.md) | Authorization Strategy | Accepted |
| [ADR-008](ADR-008-api-strategy.md) | API Strategy | Accepted |
| [ADR-009](ADR-009-cloud-strategy.md) | Cloud Strategy | Accepted (provider pending) |
| [ADR-010](ADR-010-frontend-design-system.md) | Frontend Design System | Accepted |
| [ADR-011](ADR-011-localization-rtl.md) | Localization & RTL Strategy | Accepted |
| [ADR-012](ADR-012-application-registry.md) | Application Registry & Extensibility Model | Accepted |
| [ADR-013](ADR-013-background-jobs.md) | Background Jobs & Async Processing | Accepted |
| [ADR-014](ADR-014-document-storage.md) | Document Storage | Accepted |
| [ADR-015](ADR-015-observability.md) | Observability | Accepted |

