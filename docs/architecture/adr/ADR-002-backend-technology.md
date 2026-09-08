# ADR-002: Backend Technology — .NET 10 LTS / ASP.NET Core

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture, Project Owner |

## Context

The brief specifies C#, ASP.NET Core, REST, EF Core and PostgreSQL, and directs the use of the latest stable and appropriate version, with no old technology chosen without reason. The remaining decision is which .NET release line.

.NET alternates release types: even-numbered releases are LTS (three years of support), odd-numbered are STS (eighteen months). At the time of writing, .NET 10 is the current LTS, released November 2025 and supported until November 2028.

The .NET SDK is **not currently installed** on the development machine.

## Problem

Which .NET version, and which supporting libraries, for a system intended to last a decade?

## Options

### Option A — .NET 10 (LTS)
*Pros:* three years of support; the target of most library ecosystems; stable; suitable for long-lived production systems.
*Cons:* not the newest feature set at every moment.

### Option B — The current STS release
*Pros:* newest language and runtime features.
*Cons:* eighteen months of support, forcing an upgrade cadence that a small team building a foundational platform should not accept. Upgrading a decade-long asset every eighteen months to stay supported is a recurring tax with no corresponding benefit.

### Option C — An older LTS (.NET 8)
*Pros:* very mature; extensive existing material.
*Cons:* shorter remaining support window; starting a new decade-long project on an ageing runtime is exactly the "old technology without a reason" the brief prohibits.

## Decision

**.NET 10 (LTS)** with ASP.NET Core 10, EF Core 10 and the Npgsql provider. Supporting libraries: FluentValidation, Serilog, OpenTelemetry, `Microsoft.Extensions.Http.Resilience` (Polly), xUnit, FluentAssertions, Testcontainers, NetArchTest.

Deliberately **not** adopted: AutoMapper (hand-written mapping is explicit and debuggable — P9); a mediator library is used only if pipeline behaviours prove genuinely valuable, otherwise plain handler classes; Duende IdentityServer (commercial licensing and far more capability than one company's directory needs — see ADR-006).

## Reason

LTS is the correct choice for foundational infrastructure. Support duration matters more here than having the newest language features, because the Platform's job is to be dependable for a long time. Everything else on the list is either specified by the brief or is the mainstream, well-supported option in its category.

The library exclusions follow P1 and P9: each excluded library solves a problem we do not have, at a cost in indirection we would pay every day.

## Consequences

**Positive.** Supported until November 2028 with a clear upgrade path to the next LTS. Excellent PostgreSQL support via Npgsql. Native OpenTelemetry, health checks and rate limiting in the framework — three infrastructure needs met without third-party dependencies. Strong performance and low memory footprint, which reduces hosting cost.

**Negative.** The team must be current with modern .NET (minimal APIs, nullable reference types, the newer configuration and DI patterns). Some older tutorials and Stack Overflow answers will not apply. The SDK must be installed before any work begins — currently a blocker.

**Follow-up actions.**
- Install the .NET 10 SDK (blocker B2) and pin the version in `global.json` so every machine and CI runner builds identically.
- Enable nullable reference types and treat warnings as errors from the first commit.
- Plan the upgrade to the next LTS for 2028, before support ends rather than after.

## Status
Accepted.

