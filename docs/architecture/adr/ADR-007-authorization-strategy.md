# ADR-007: Authorization Strategy — RBAC with Organizational Scope

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture, Security |

## Context

The Platform must define and evaluate permissions for itself and for every future business system — including systems that do not exist yet and whose permissions cannot be known in advance. The brief requires roles, permissions, user-roles, role-permissions, and system-, resource- and action-level permissions, using a `resource.action` pattern, while remaining unbound to any specific business system.

A permission check happens on every request, so evaluation cost is on the hot path of the entire company's software.

## Problem

What authorization model expresses the requirements, stays business-agnostic, is extensible by unknown future systems, and is fast enough to run on every request?

## Options

### Option A — Flat RBAC
Roles contain permissions; users have roles.

*Pros:* simple; well understood; fast; easy to audit.
*Cons:* cannot express "which records" — a "view employees" permission says nothing about *whose* employees. Real organizations need that distinction constantly.

### Option B — Full ABAC / policy engine
Arbitrary attribute-based rules, possibly with a policy language.

*Pros:* expresses anything.
*Cons:* a rule language, a policy engine and an evaluation cost on every request; very hard to audit ("why can this user do this?" becomes a debugging exercise); and the flexibility invites business rules into the Platform, directly threatening ADR-005. No stated requirement needs it.

### Option C — RBAC with organizational scope
Roles carry permissions; each role assignment carries a scope (Self / Unit / UnitAndBelow / All) resolved against the organizational hierarchy.

*Pros:* expresses the real requirement ("this manager sees their own department's employees") with one added dimension; still auditable and fast; scope is *organizational*, not business-conditional, so it does not open the door ADR-005 closes.
*Cons:* scope evaluation must be correct and fast; more complex than flat RBAC.

## Decision

**Option C.**

Permissions are named `<application>.<resource>.<action>` — `platform.users.view`, `finance.invoices.approve`. The Platform owns the `platform.*` namespace only. Registered applications own their own namespaces and declare their permissions through the registry (ADR-012); to the Platform, `finance.invoices.approve` is an opaque string.

Each role assignment carries a scope: `Self`, `Unit`, `UnitAndBelow` (resolved via the organizational materialized path), or `All`. The evaluator returns both a decision and the data filter the query must apply, so scope restricts **data**, not merely access.

**Direct user-permission grants are not supported.** Everything flows through roles.

Full ABAC is explicitly deferred. Adopting it requires a superseding ADR.

## Reason

Flat RBAC does not answer the question real organizations ask, and ABAC answers far more than anyone asked at a cost paid on every request and in every audit. Scoped RBAC sits exactly on the requirement.

The namespace prefix is the mechanism that reconciles two apparently conflicting requirements: the Platform must be business-agnostic, and it must be the authorization authority for business systems. It achieves both by *storing and evaluating* permissions it does not *understand*.

Direct user grants are excluded deliberately. They are always convenient in the moment and always the reason, years later, that nobody can answer "why does this person have access to that?".

Returning a data filter rather than a boolean is a deliberate design choice: an authorization system that returns only yes/no relies on every caller remembering to filter, and eventually one will not.

## Consequences

**Positive.** Auditable — a user's access is the union of their roles, visible in one screen. Fast — permissions cache per user with eager version-stamped invalidation. Extensible to unknown systems without Platform changes. Scope enforced at the data layer, so a forgotten filter cannot leak records.

**Negative.** Scope evaluation adds latency to every request, mitigated by caching and by indexing the materialized path. Cache invalidation must be eager and correct — a stale permission cache that grants revoked access is a security defect, not a performance quirk. Organizational moves must invalidate scope caches. Very fine-grained per-record permissions are not expressible; if that requirement appears, it needs a new ADR rather than an improvised extension.

**Follow-up actions.**
- Eager version-stamped cache invalidation, with an explicit test for the revocation window (Phase 4).
- An architecture test failing the build if any endpoint lacks a permission declaration or an explicit anonymous marker (Phase 4).
- An exhaustive generated authorization matrix test: every endpoint × unauthenticated / wrong permission / correct permission / wrong scope (Phases 4 and 20).
- Anti-escalation rules: no granting a permission you do not hold; no modifying your own roles.

## Status
Accepted.

