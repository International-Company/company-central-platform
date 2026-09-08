# ADR-010: Frontend Design System — White/Blue, Text-First, No Icons by Default

| Field | Value |
|---|---|
| Status | **Accepted (owner-mandated)** |
| Date | 2026-09-06 |
| Deciders | Project Owner (final decision), UI/UX, Platform Architecture |

## Context

The brief is unusually specific about the interface, and the specificity is the point. It must look like professional enterprise financial software designed by a human UI/UX team — not like an AI-generated dashboard, a startup SaaS template, a gaming interface or a consumer app.

The prohibitions are explicit: no AI illustrations, robots, sparkles, stars, magic effects, wand icons, abstract AI graphics, 3D or floating decoration, excessive animation, or decorative elements without function. The visual identity is white and blue only. Icons are not used by default. Buttons carry text. Tables are the primary interface. Cards are used sparingly and never in the huge-icon-huge-number-gradient pattern.

This is a real design constraint, not a stylistic preference, and it is easy to violate accidentally — the default output of most component libraries and most contemporary design habits points the other way.

## Problem

How is this design direction specified precisely enough to be followed and enforced, rather than drifting back toward the default look?

## Options

### Option A — A general "keep it professional" guideline
*Cons:* unenforceable. "Professional" means different things to different people, and under time pressure it means whatever is fastest.

### Option B — A token-based design system with explicit prohibitions and automated enforcement
Named tokens for colour, type, spacing, radius and elevation; the prohibitions written down; lint rules and a review checklist enforcing what can be automated.

*Cons:* more setup effort; some rules can only be enforced by human review.

### Option C — Adopt an existing enterprise design system wholesale
*Cons:* brings its own visual identity, which conflicts with the mandated white/blue direction, and its own icon conventions, which conflict with the icon policy.

## Decision

**Option B.**

**Colour:** white and light-neutral surfaces; one calm, desaturated professional blue as the single accent; neutral greys for text, borders and secondary surfaces. Status is expressed through text and shape as well as colour, never colour alone. No additional accent hues, no neon, no loud colour, no heavy gradient, no glow, no glassmorphism.

**Icons: none by default.** No icon beside a nav item, button, table action, card, section or input. Priority to typography, text, whitespace, tables, forms, buttons, borders and layout. An icon is permitted only where it demonstrably improves usability or accessibility, used sparingly, and never as an icon-only control where a word is clearer.

**Buttons carry text**: *Create User*, *Edit*, *Save*, *Cancel*, *Delete*, *View Details*, *Search*, *Filter*, *Export*.

**Tables are the primary interface**: clear, dense, readable, searchable, filterable, sortable, paginated, comfortable with large datasets, with row actions as text links or a text action menu.

**Cards** are used only where functionally useful, in the form *title → value → supporting information*. The huge-icon / huge-number / gradient pattern is prohibited.

**Borders** light, **shadows** minimal, **radius** moderate, **whitespace** doing the work. **Animation** only for loading, state change, feedback and open/close — short, quiet, and honouring `prefers-reduced-motion`. **Typography** chosen for dense-data readability, with clear numerals and a matching-quality Arabic face; no display or expressive fonts.

## Reason

This is the owner's decision and it is also correct for the audience. People use administrative software for hours a day; decoration that is charming on first view is friction on the four-hundredth. Dense, quiet, text-first interfaces are faster to scan and less tiring to use.

The icon policy in particular is stricter than most designers' instinct, and that is deliberate: an icon beside every label is decoration that has to be decoded. Text does not need decoding.

Option B is chosen over a general guideline because a general guideline will not survive. The default of the tooling, and the default habits of anyone writing UI, both point toward the prohibited look. Only explicit tokens, written prohibitions and automated checks keep the design where the owner asked for it.

## Consequences

**Positive.** A consistent, calm, professional interface that suits long working sessions. Fast to render and light to ship. Fewer decisions per screen, so screens get built faster. Accessible by construction, since it does not lean on colour or iconography to carry meaning.

**Negative.** It will look plainer than contemporary marketing-driven interfaces, and someone will eventually suggest making it "more modern" — that suggestion is answered with this ADR. **shadcn/ui components ship with `lucide-react` icons in their default markup**, so every adopted component must have its decorative icons removed at adoption; this is ongoing work, not a one-time cleanup. Purely functional indicators — a select's chevron, a checkbox's check, a sort direction — are the narrow permitted exception.

**Follow-up actions.**
- Define the design tokens in Phase 7 before any screen is built.
- Strip decorative icons from every shadcn/ui component at adoption; add a lint rule flagging icon imports in `components/ui/`.
- Add §9.5 of ARCHITECTURE.md to the pull request review checklist.
- Owner design review at the end of Phase 7 and Phase 15.
- Confirm the Arabic typeface and any brand blue with the owner (Q12).

## Status
Accepted.

