# Company Central Platform

Shared infrastructure every business system in the company is built on: identity,
organization, authorization, security, audit, workflow, notifications, documents,
integrations, configuration and observability.

**It holds no business logic of its own, and that is the whole design.** The
dependency runs one way — business applications depend on the Platform, never the
reverse — so that a payroll system, a procurement system and whatever gets built
in three years all get the same sign-in, the same permission model, the same
audit trail and the same approval engine, without any of them being able to reach
into the Platform and bend it toward their own domain.

That constraint is enforced rather than asked for. The Workflow module has a test
that fails the build if business vocabulary appears in it at all.

---

## What it is made of

| | |
|---|---|
| Backend | C#, ASP.NET Core 10, EF Core 10, PostgreSQL 17 |
| Frontend | React 19, TypeScript, Next.js 15, Tailwind 4 |
| Shape | Modular monolith — one schema per module, one migration history per module, **no foreign key across a schema boundary** |
| Languages | Arabic and English, with real RTL/LTR mirroring |

A modular monolith rather than microservices because the boundaries are enforced
in the compiler and the test suite, and can be enforced across process boundaries
later if the load ever justifies the cost — whereas a distributed system built
before the boundaries were understood cannot be undistributed. See
[ADR-001](docs/architecture/adr/ADR-001-architecture-style.md).

---

## Getting started

```bash
docker compose up -d          # PostgreSQL, MinIO, Mailpit
cp .env.example .env
dotnet run --project src/Host/CCP.Api.Host
```

Full instructions, including how to apply all twelve migration histories and how
to create the first administrator, are in
[docs/development/getting-started.md](docs/development/getting-started.md).

The frontend:

```bash
cd frontend && npm install && npm run dev
```

---

## Where things are

| Path | What is in it |
|---|---|
| [`src/Kernel/`](src/Kernel/) | Primitives, seams and cross-cutting infrastructure that belong to no module |
| [`src/Modules/`](src/Modules/) | The eleven capability modules, each Domain / Application / Infrastructure / Api / Contracts |
| [`src/Host/`](src/Host/) | The composition root — the only place that knows every module exists |
| [`frontend/`](frontend/) | The administration portal |
| [`tests/`](tests/) | Unit, architecture and integration tests |
| [`contracts/platform-api.json`](contracts/) | The API contract, generated from the endpoints themselves |
| [`samples/reference-client/`](samples/) | A working client for the machine-to-machine API |

A module never references another module's Domain, Application or Infrastructure
— only its `Contracts`. Where a module needs something from another, the
**Application layer declares the seam and Infrastructure implements it**, so the
dependency is on an interface the module owns rather than on a neighbour.

---

## Documentation

| Document | Read it when |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | You want to know why something is the way it is |
| [DEVELOPMENT_STATUS.md](DEVELOPMENT_STATUS.md) | You want to know what is actually finished, what is not, and every open defect |
| [docs/development/getting-started.md](docs/development/getting-started.md) | You are setting up |
| [docs/development/integration-guide.md](docs/development/integration-guide.md) | You are building a business system against the Platform |
| [docs/development/adding-a-module.md](docs/development/adding-a-module.md) | You are adding a twelfth module to the Platform itself |
| [docs/deployment/observability.md](docs/deployment/observability.md) | Something is broken at three in the morning |
| [docs/deployment/backup-and-recovery.md](docs/deployment/backup-and-recovery.md) | You are responsible for the data surviving |
| [docs/architecture/adr/](docs/architecture/adr/) | You are about to change a decision somebody already made |

**`DEVELOPMENT_STATUS.md` is the honest one.** It carries a per-phase report of
what was built and what broke, and a technical-debt register that names every
known gap — including the ones that are embarrassing. Read it before trusting any
claim made anywhere else in this repository, including in this file.

---

## Rules that are enforced, not requested

These are checked by the build, not by review:

- **No secret in the repository.** Gitleaks runs on every push. It has already
  failed the build on test fixtures that merely *looked* like credentials, and
  the fixtures were changed rather than the scanner.
- **No warnings.** Warnings are errors (`Directory.Build.props`).
- **No schema crossing.** Architecture tests assert the module boundaries and
  that every module's migrations are applied.
- **Every endpoint declares a permission** or explicitly allows anonymous, and
  the published contract is generated from that metadata so the two cannot drift.
- **Every declared metric is emitted by something.** Added after two instruments
  were found to have no caller at all, with alerts written against them, reading
  permanently green.
- **The API contract is regenerated and committed**, so the frontend's generated
  types cannot be built from a stale document.
- **Accessibility**, in CI, with axe against WCAG 2.1 AA on every screen in both
  locales.

---

## Design constraints

The portal is deliberately plain: **tables first, text buttons, white and blue,
no icon package installed at all**. Not a stylistic preference — an
administrative tool is read far more often than it is admired, and every
decoration is something between the reader and the data. There is a test that
fails the build if an icon library, a gradient or a colour outside the token set
appears.

Arabic and English are equal, with layout mirrored by logical properties rather
than by a right-to-left stylesheet, and no user-facing string is hardcoded.

---

## Status

The Platform is deployed and running. What remains, what is broken, and what is
merely believed rather than verified are all in
[DEVELOPMENT_STATUS.md](DEVELOPMENT_STATUS.md) — **including the phase count,
which is deliberately not repeated here.**

This paragraph used to say "seventeen of twenty-two phases are done or
substantially done". It was wrong, and it was wrong in the ordinary way: a number
copied into a second place goes stale in one of them, and the copy is never the
one somebody thinks to update. The status document counts its own table now, and
CI checks that it does.

The most serious open item: **nothing is taking a backup.** The procedure is
written; the restore verification has never been run against a real dump, which
this paragraph also used to claim it had. Whether the database it is deployed on
has provider-level backups is not recorded anywhere — see debt #65 and #66.
