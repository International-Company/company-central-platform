# ADR-005: Platform / Business Boundary

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Project Owner, Platform Architecture |

## Context

This is the most consequential decision in the project. The Platform must be business-agnostic: it must serve Financial, HR, Administrative, IT, Design, Gold and future systems without knowing anything about any of them. The brief lists sixteen architectural prohibitions, of which the first several concern exactly this boundary.

The pressure against the boundary is constant and it always sounds reasonable. "Just store this one field." "It would be so much easier if the Platform knew about invoices." "Only our system uses this, but it's basically generic." Every one of those requests, granted, moves the Platform a step closer to being the tangled system it exists to replace.

## Problem

Where exactly is the line, and how is it defended over years against constant, well-intentioned pressure?

## Options

### Option A — Strict boundary: no business knowledge at all
The Platform models only generic concepts. Business systems extend it through registration.

*Pros:* genuinely reusable; each system stays independent; the Platform stays small enough to understand.
*Cons:* business systems must do more work; some duplication across business systems; the Platform cannot offer business-specific conveniences.

### Option B — Pragmatic boundary: allow "common" business concepts
Permit widely-shared business concepts — customers, currencies, an approval threshold — into the Platform.

*Pros:* less duplication in the short term; convenient.
*Cons:* "common" has no definition, so the boundary moves with each request until it does not exist. Currency handling drags in exchange rates; customers drag in customer types; a threshold drags in the rule that sets it. This is the path by which every shared platform becomes an unmaintainable monolith, and it always begins with one reasonable exception.

### Option C — Vertical slices: business modules inside the Platform
Build business systems as Platform modules.

*Pros:* one codebase; easy cross-module queries.
*Cons:* directly contradicts the brief; the Platform stops being a foundation and becomes the whole company system; the modules can never be independently owned or replaced.

## Decision

**Option A — a strict boundary**, defended by a written test applied to every proposed feature:

1. Would a completely different company, in a different industry, need this?
2. Can it be described without naming a company business process?
3. Does it require the Platform to know a business rule, rate, or threshold?
4. Would three future systems each rebuild it otherwise?

A feature enters the Platform only if it passes 1, 2 and 3.

The three grey zones are resolved explicitly so they are not re-argued:

- **Employees** are Platform (an organizational fact: who works here, where, reporting to whom). **Payroll, leave, attendance and appraisals are HR's**, referencing Platform employees by ID. The Platform stores an employee's department; it does not store their salary.
- **Workflow**: the engine is Platform; definitions are data supplied by business applications; **business-conditional routing is resolved by the calling application**, never by the engine. The engine holds no thresholds.
- **Documents**: storage, metadata, versioning and access control are Platform; the business classification of a document is an opaque tag supplied by the owner.

## Reason

Option B fails not because it is wrong in any single instance, but because it has no stopping rule. Every individual exception is defensible; the accumulation is fatal. A boundary that bends on reasonable request is not a boundary — it is a preference. The strict version is the only one that survives contact with five years of feature requests.

The cost is real: business systems do more work, and some logic is duplicated across them. That cost is accepted, because the alternative cost — a platform nobody can change because eleven systems depend on its business assumptions — is the failure mode this project exists to avoid.

## Consequences

**Positive.** The Platform stays small, comprehensible and genuinely reusable. Business systems remain independently replaceable. New systems onboard through registration rather than through Platform changes. The Platform team is never blocked on business decisions, and business teams are never blocked on Platform releases.

**Negative.** Business systems carry more responsibility. Some logic is duplicated across business systems — accepted deliberately, because coupling costs more than duplication here. Some workflows need an extra call between the business system and the Platform. Boundary requests will be refused, and refusing them will sometimes be unpopular.

**Follow-up actions.**
- The four-question test is part of the pull request review checklist.
- Every phase includes an architecture review confirming the boundary held.
- A refused request is answered with an extension point, not merely a refusal — "no, and here is how you do it in your system" (P4).
- This ADR is quoted, not paraphrased, when the boundary is challenged.

## Status
Accepted. **Binding on all phases.**
