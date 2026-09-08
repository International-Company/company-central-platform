# ADR-011: Localization & RTL Strategy

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture, UI/UX |

## Context

The Platform must support Arabic and English as equals, with real localization and no hardcoded UI text. Arabic requires RTL and English LTR, and the brief is explicit that **the entire layout must change, not only the text** — navigation, sidebar, forms, tables, pagination, dialogs, dropdowns, breadcrumbs, spacing and alignment.

RTL support has a well-known failure mode: it is treated as a translation task, added late, and remains permanently half-finished because hundreds of components hardcode physical directions.

## Problem

How is genuine bidirectional support achieved and kept working as the application grows?

## Options

### Option A — Two stylesheets, one mirrored by a build step
*Pros:* works with existing physical-direction CSS.
*Cons:* two artifacts to ship and keep consistent; automated mirroring gets edge cases wrong; the mismatch is discovered by users.

### Option B — CSS logical properties throughout
`margin-inline-start` instead of `margin-left`; Tailwind's `ms-` / `me-` / `ps-` / `pe-` / `start-` / `end-` utilities. One stylesheet; direction handled by `dir` on the root element.

*Pros:* one stylesheet; correct by construction; no build step; the browser does the work.
*Cons:* requires discipline — one physical utility in one component breaks that component in one direction, and it will be found by a user rather than by a developer.

### Option C — Runtime direction switching in JavaScript
*Cons:* fragile, slow, and reinvents what CSS already does.

## Decision

**Option B, enforced by lint.**

- Locale is a route segment (`/ar/...`, `/en/...`) so any page is linkable in a specific language.
- `<html lang dir>` set from the locale; the whole layout mirrors.
- **Tailwind logical properties only.** A physical `left`/`right` directional utility in a component is a defect, and a lint rule fails the build on it.
- **No hardcoded user-facing strings**, enforced by a lint rule. All text comes from `ar.json` / `en.json` via next-intl.
- Numbers, dates and times formatted with `Intl` per locale; stored as UTC.
- API error `title` and `message` localized from `Accept-Language`; `code` never localized.
- Notification templates exist per locale.
- Domain data that is genuinely bilingual (a department name) is stored as `name_ar` and `name_en`, not machine-translated at render time.
- **Both locales are in the E2E suite from the first screen**, and both-direction verification is an acceptance criterion of every frontend phase.

## Reason

Logical properties are the modern, correct mechanism, and they place the work on the browser rather than on a build step or a developer's memory. But the mechanism alone is not enough — the failure mode of RTL is a discipline failure, not a technical one. That is why the lint rules and the both-locale E2E requirement are part of this decision rather than a recommendation: without them, the tenth developer to join writes `ml-4` and nobody notices until a user does.

Testing both locales continuously, rather than in a single RTL pass at the end, is what prevents Arabic from becoming a second-class experience.

## Consequences

**Positive.** One stylesheet, one build. Correct by construction. New components are automatically bidirectional if they follow the rules. Arabic is a first-class experience rather than a translation layer.

**Negative.** The team must learn logical properties, which are less familiar than `left`/`right`. Every translated string is maintained in two files, and a missing key must fail loudly rather than silently render a key name. Bilingual domain fields add columns and form complexity. Both-locale E2E doubles that suite's runtime.

**Follow-up actions.**
- Configure both lint rules in Phase 7, before the first component is written.
- Add both-direction verification to the acceptance criteria of every frontend phase.
- Confirm the Arabic typeface with the owner (Q12).
- Make a missing translation key a visible failure in development, not a silent fallback.

## Status
Accepted.
