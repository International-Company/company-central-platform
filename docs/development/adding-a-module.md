# Adding a module

Reference: [ARCHITECTURE.md §6–§7](../../ARCHITECTURE.md), [ADR-001](../architecture/adr/ADR-001-architecture-style.md) and [ADR-004](../architecture/adr/ADR-004-database-technology.md).

**Eleven modules follow the same shape, and until this file existed that shape
was folklore** — a thing you learned by opening Configuration and copying it.
Folklore is what documentation exists to replace, and the cost of it here is
specific: the rules below are enforced by tests that fail the build, so a module
built by half-remembering the pattern does not compile its way to a merge. It
stops, and somebody reads the failure instead of the reasoning.

This is that reasoning.

Start by copying **Configuration**. It is the smallest module that has
everything: five projects, a schema, a repository, a background-facing seam and a
screen.

---

## 1. Is it a Platform module at all?

Ask first, because the answer is usually no.

The Platform holds capabilities **every** business system needs and **no**
business rule that belongs to one of them. A module that would know what an
invoice is, or what a leave request is, is a business application that depends on
the Platform — not a module inside it. The dependency runs one way, always:

```
Business applications  →  Platform
```

The Workflow module is the shape to aim at. It runs approvals for anything and
holds no rule about what is being approved, and a test fails the build if
business vocabulary appears in it at all (`WorkflowBoundaryTests`).

---

## 2. Five projects

```
src/Modules/<Name>/
  CCP.Modules.<Name>.Domain           entities, value objects, errors. No EF, no ASP.NET.
  CCP.Modules.<Name>.Contracts        DTOs and the interfaces other modules may use.
  CCP.Modules.<Name>.Application      handlers, and the seams the module needs answered.
  CCP.Modules.<Name>.Infrastructure   DbContext, repositories, anything that does I/O.
  CCP.Modules.<Name>.Api              endpoints and the module registration.
```

`LayerDependencyTests` enforces the arrows: Application references neither
Infrastructure, Api, EF Core nor ASP.NET Core; Api references neither
Infrastructure nor EF Core. **One of its tests exists to check the checker** —
`ReferenceInspection_ActuallySeesReferences` fails if the inspection stops seeing
references at all, because a dependency test that silently matches nothing passes
for ever.

### What a module may reference of another module

**Contracts. Nothing else.** Not its Domain, not its Application, not its
Infrastructure. That single rule is what keeps a module extractable into its own
service later without a rewrite.

When your module needs something only another module knows, do not reach for it.
**Declare the question in your Application layer and answer it in your
Infrastructure layer** using the other module's Contracts:

```csharp
// Application/Abstractions
public interface IAccessSubjectResolver { … }

// Infrastructure — may reference Organization.Contracts and Authorization.Contracts
public sealed class PlatformAccessSubjectResolver : IAccessSubjectResolver { … }
```

Documents, Configuration and Workflow each do this. The Application layer stays
ignorant of which modules exist; the composition root is where they meet.

If the question is one **every** module will ask, it belongs in the kernel as a
neutral seam instead — `IAuditTrail`, `IJobJournal`, `IPlatformSettings` are all
kernel contracts with exactly one module behind them. `AuditCoverageTests` fails
the build if any module references the Audit module directly.

---

## 3. One schema, one migration history

```csharp
public sealed class ThingDbContext(DbContextOptions<ThingDbContext> options) : DbContext(options)
{
    public const string SchemaName = "thing";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        // …entity configuration…

        // Not optional, and not cosmetic. This was defined and never called in
        // Phase 1, so columns generated as PascalCase while hand-written index
        // filters referred to snake_case ones that did not exist. The migration
        // would have failed on the first deployment.
        //
        // ColumnNamingTests builds every module's model through its design-time
        // factory and fails the build on a column that is not snake_case, so
        // forgetting this line costs a red build rather than a deployment.
        modelBuilder.ApplySnakeCaseNames();
    }
}
```

**No foreign key may cross a schema boundary.** `SchemaBoundaryTests` walks every
context's model and fails the build on one. A reference to another module's data
is an id you resolve through that module's Contracts, not a join.

The same test checks that your context does **not** generate the kernel's
`outbox_messages` table. The outbox is mapped into every module context so that a
module can write an event in the same transaction as its own change, and excluded
from every module's migrations so that only the kernel creates it.

Create the migration with its own history table:

```bash
dotnet ef migrations add InitialThing \
  --project src/Modules/Thing/CCP.Modules.Thing.Infrastructure \
  --startup-project src/Host/CCP.Api.Host \
  --output-dir Persistence/Migrations
```

---

## 4. Wire it in, in three places

All three, or a test fails.

1. **Infrastructure registration** — `Program.cs`, beside the other ten:

   ```csharp
   builder.Services.AddThingInfrastructure(builder.Configuration, connectionString);
   ```

2. **The migrator's context list** — `Program.cs`, and the integration test
   factory as well. `MigrationCoverageTests` finds every `DbContext` in every
   `CCP.*.Infrastructure.dll` and fails if either list is missing one. It exists
   because the test factory migrated only the kernel for three phases, so every
   module integration test written in that time would have failed on a missing
   table at its first CI run.

3. **The module list** — `PlatformModules.Modules`. A hand-written list rather
   than assembly scanning, so the set of active modules is readable in one place
   and a module cannot activate itself merely by being present.

---

## 5. Endpoints

Every endpoint declares who may call it. There is no default.

```csharp
group.MapGet("/things", async (…) => { … })
    .RequireAuthorization()
    .WithMetadata(new RequirePermissionAttribute("platform.things.view"))
    .Produces<IReadOnlyList<ThingDto>>(StatusCodes.Status200OK)
    .WithName("GetThings")
    .WithSummary("…");
```

`EndpointSecurityTests` refuses anything else. An endpoint that genuinely needs
no permission carries `[AuthenticatedUserOnly]` **with a written reason**, and an
anonymous one has to be added to a reviewed list in the test itself — the guard
has already caught three endpoints that were added without review.

Two more things the tests will ask of you:

- **Bind services with `[FromServices]`.** Inference works until the service
  composition changes, and then it fails at runtime rather than at build.
- **An anonymous endpoint declares a rate-limit policy** (`RateLimitingTests`),
  and a credential endpoint carries the strict authentication policy.

A privileged action — one where a stolen session is the threat — also carries
`[RequireStepUp]`. `StepUpCoverageTests` holds the exact reviewed set and fails
when it changes, in either direction: the point is that adding or removing
step-up is a decision somebody recorded, not a diff nobody read.

---

## 6. Auditing, events and settings

**Audit** what changed state, through the kernel's `IAuditTrail`.
`AuditCoverageTests` fails the build if a state-changing handler cannot reach it.

**Publish** cross-module facts through the outbox, in the same transaction as the
change. Never call another module's handler.

**Declare** any setting your module reads, in your own namespace, through the
Configuration module's seam. `SettingCoverageTests` fails if a key is read that
nobody declared.

**Instrument** with `PlatformMetrics` in the kernel's Application layer.
`InstrumentCoverageTests` fails the build if a recording method has no caller —
written after Phase 14 shipped `ccp.jobs.runs` with an alert against it and no
code path that could emit it, and it immediately found a second dead instrument.

---

## 7. The portal

A module with an API and no screen is half a module, and the two that had one
were finished in Phase 15 rather than left.

1. A **BFF route** under `frontend/src/app/api/…`, relaying through
   `callPlatform`. The browser never holds a token.
2. A **screen** under `frontend/src/features/…`, built from `DataTable` and the
   shared components. Tables first, text buttons, no icons, white and blue.
3. **Both locales**, at parity — a test fails the build on a key present in one
   catalogue and absent from the other. No string is hardcoded in a component.

---

## 8. Before you push

```bash
bash scripts/regenerate-contract.sh   # the committed contract must match the endpoints
cd frontend && npm run generate:types # and the portal's types must match the contract
dotnet format
dotnet build
dotnet test
python scripts/check-doc-links.py
```

CI runs all of it, and fails on a contract that has drifted — otherwise the
frontend would keep generating types from a stale document and still pass, which
is the failure the generation exists to prevent wearing a different hat.

---

## 9. What to write down

Add a `docs/<name>/README.md` explaining what the module refuses and why, not
what its endpoints are shaped like — the contract already says that. Then add the
row to `docs/README.md`.

And record what you did not build. `DEVELOPMENT_STATUS.md` is the honest account,
and an omission written down is a decision; an omission left out is a surprise
for whoever finds it.
