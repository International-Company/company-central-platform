# ADR-003: Frontend Technology — Next.js / React / TypeScript

| Field | Value |
|---|---|
| Status | **Accepted (owner-mandated)** |
| Date | 2026-09-06 |
| Deciders | Project Owner (final decision), Platform Architecture |

## Context

The brief states this as a final decision: React + TypeScript + Next.js with Tailwind CSS and shadcn/ui. Vue, Angular and other frameworks are excluded. The decision may only change for a strong architectural reason, and then only through a documented process with the owner's approval.

This ADR records the decision and its consequences rather than re-opening it.

## Problem

Confirm the frontend stack and the supporting library choices that follow from it.

## Options

### Option A — Next.js + React + TypeScript (mandated)
*Pros:* the largest ecosystem; server components reduce client bundle size; route handlers provide a natural BFF layer for secure token handling; first-class TypeScript; shadcn/ui gives owned, restyleable components rather than an opaque dependency; strong RTL support through Tailwind logical properties.
*Cons:* the App Router has a learning curve; React Server Components add conceptual complexity; shadcn/ui's defaults include decorative icons that conflict with the icon policy and must be stripped.

### Option B — Angular
Considered and excluded by the owner. Strong for enterprise forms, but not the chosen stack.

### Option C — Vue / Nuxt
Considered and excluded by the owner.

### Option D — A plain React SPA (Vite) without Next.js
*Pros:* simpler mental model; no server runtime.
*Cons:* no server layer, so authentication tokens must live in the browser — which forecloses the BFF pattern chosen in ADR-006 and reintroduces XSS token theft as a risk. This alone justifies Next.js independently of the mandate.

## Decision

**Next.js (App Router) + React + TypeScript (strict) + Tailwind CSS + shadcn/ui**, as mandated.

Supporting choices, decided here: **TanStack Query** for server state; **react-hook-form + zod** for forms; **TanStack Table** (headless) for tables; **next-intl** for localization; **Vitest + Testing Library + Playwright** for testing. Global client state uses React's own primitives; a state library is added only where a proven need appears (brief §43).

## Reason

The stack is mandated, and it is also defensible on its merits. The decisive technical point is the one in Option D: Next.js gives a server layer, and a server layer is what makes it possible to keep authentication tokens out of the browser entirely. That is a security property, not a convenience.

The supporting libraries are each the mainstream headless or unopinionated choice in their category — headless matters because the design system (ADR-010) requires full control of markup and styling. A component library with its own visual opinions would fight the design direction on every screen.

## Consequences

**Positive.** One stack for every future frontend. Server components reduce shipped JavaScript, which matters for users on slower connections. shadcn/ui components live in our repository, so restyling them to the design system is editing our own code. Headless table and form libraries impose no visual opinions.

**Negative.** shadcn/ui ships `lucide-react` icons in its default component markup, which directly conflicts with the no-icons-by-default policy — **every adopted component must have its decorative icons removed**, and this must be enforced, not remembered. The App Router requires real understanding of the server/client component boundary. Next.js is opinionated, and fighting its conventions is unproductive.

**Follow-up actions.**
- A lint rule flagging icon imports in `components/ui/` (Phase 7).
- A lint rule prohibiting physical `left`/`right` Tailwind utilities (ADR-011).
- Generate TypeScript API types from OpenAPI, so a backend contract change breaks the build rather than production.
- Any proposal to change this stack follows ARCHITECTURE.md §26.3 and requires the owner's approval.

## Status
Accepted.

