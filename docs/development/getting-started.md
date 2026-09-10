# Getting Started

From a clean machine to a running Platform.

| | |
|---|---|
| Status | Current as of Phase 17 |
| Applies to | The whole Platform: eleven modules, the kernel, and the administration portal |

---

## 1. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | **10.0.400** (pinned in `global.json`) | `dotnet --version` |
| Docker | Any recent, with a working Linux engine | `docker info` |
| Git | Any recent | `git --version` |
| Node.js | 22+ (for the portal) | `node -v` |

PostgreSQL does **not** need installing — Docker Compose provides it.

If the SDK is missing:

```bash
# Windows
winget install --id Microsoft.DotNet.SDK.10 --exact

# macOS
brew install --cask dotnet-sdk

# Linux — see https://learn.microsoft.com/dotnet/core/install/linux
```

---

## 2. Start the dependencies

```bash
docker compose up -d
```

This starts three services:

| Service | Port | Purpose |
|---|---|---|
| PostgreSQL 17 | 5432 | The Platform database |
| MinIO | 9000 (API), 9001 (console) | S3-compatible object storage for Documents (Phase 10) |
| Mailpit | 1025 (SMTP), 8025 (web) | Catches all outbound mail so nothing is sent from a developer machine |

Confirm they are healthy:

```bash
docker compose ps
```

---

## 3. Configure

```bash
cp .env.example .env
```

`.env` is gitignored. The defaults match `docker-compose.yml`, so it works as copied.

> **The credentials in `.env.example` are local-development values and grant access to nothing but a throwaway container.** Real credentials never appear in any file in this repository; in staging and production they come from the cloud secret manager (ARCHITECTURE.md §12.7). If you ever put a real secret in a file here, rotate it — removing it from git history is not sufficient.

Configuration is read from environment variables prefixed `CCP_`, using `__` for nesting:

```
CCP_ConnectionStrings__Platform    → ConnectionStrings:Platform
CCP_Outbox__BatchSize              → Outbox:BatchSize
```

---

## 4. Apply migrations

**There are twelve migration histories, not one.** Each module owns its schema
and its own history table (ADR-004), so a single `dotnet ef database update` on
the kernel leaves you with a Platform that starts, reports healthy, and answers
500 from every module. Locally, let the host apply them all:

```bash
export CCP_Database__ApplyMigrationsOnStartup=true
dotnet run --project src/Host/CCP.Api.Host
```

The migrator takes a PostgreSQL advisory lock, walks every context with the
kernel first — its `outbox_messages` table is mapped into all the others and
excluded from their migrations, so it must exist before they run — and clears
the statement timeout for the duration, because an index build is legitimately
long.

To apply one module by hand, or to create a new migration:

```bash
dotnet tool install --global dotnet-ef --version 10.*

dotnet ef database update \
  --project src/Modules/Identity/CCP.Modules.Identity.Infrastructure \
  --context IdentityDbContext

dotnet ef migrations add AddSomething \
  --project src/Modules/Identity/CCP.Modules.Identity.Infrastructure \
  --context IdentityDbContext \
  --output-dir Persistence/Migrations
```

> **`ApplyMigrationsOnStartup` is off by default and belongs off in production.**
> A schema change should be a reviewed step, not a side effect of a restart. It
> is safe with several instances — the advisory lock means one migrates and the
> rest wait — and it is on in the Railway deployment only because that platform
> offers nowhere else to run it. An architecture test asserts that both the host
> and the integration-test factory know about every context, because both lists
> have already been wrong and the symptom was three tests answering 500 with no
> clue why.

---

## 4a. Create the first administrator

Bootstrapping refuses to run once any user exists, so this is a one-time step:

```bash
export CCP_Identity__Bootstrap__Enabled=true
export CCP_Identity__Bootstrap__Username=admin
export CCP_Identity__Bootstrap__Email=admin@example.invalid
export CCP_Identity__Bootstrap__DisplayName=Administrator
export CCP_Identity__Bootstrap__InitialPassword=a-long-one-time-passphrase
```

Run once, sign in, then **remove those variables**. See
[../identity/bootstrap-administrator.md](../identity/bootstrap-administrator.md).

---

## 5. Run

```bash
dotnet run --project src/Host/CCP.Api.Host
```

The API listens on <http://localhost:5080>.

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Liveness. Anonymous, bare status. |
| `GET /health/ready` | Readiness. Checks the database; 503 when it is unreachable. |
| `GET /api/v1/diagnostics/ping` | Confirms the API is reachable; returns the correlation id. |
| `GET /openapi/v1.json` | OpenAPI document (Development only). |

Quick check:

```bash
curl http://localhost:5080/api/v1/diagnostics/ping
```

```json
{"status":"ok","serverTimeUtc":"2026-09-06T14:15:41Z","correlationId":"01a0771331..."}
```

### The portal

```bash
cd frontend
npm install
npm run dev
```

It serves on <http://localhost:3000> and redirects to `/ar` or `/en`. It talks to
the API through its own server-side routes — **no access token ever reaches the
browser**, and a test fails the build if anything touches `localStorage`.

---

## 6. Test

```bash
# Everything
dotnet test

# By kind
dotnet test tests/CCP.Kernel.UnitTests          # no dependencies
dotnet test tests/CCP.Architecture.Tests        # no dependencies
dotnet test tests/Modules/CCP.Modules.Identity.UnitTests   # and ten siblings
dotnet test tests/CCP.Api.IntegrationTests      # requires PostgreSQL

# The portal
cd frontend
npm run lint && npm run test && npm run build
npx playwright test                             # requires the API running
```

The architecture tests are the ones worth running before you push. They refuse
work rather than reporting it: an endpoint that declares no permission and does
not explicitly allow anonymous fails the build, as does a module context the host
does not migrate, and a metric declared with no caller.

The integration tests create a uniquely-named database per test class, apply
migrations to it, and drop it afterwards — so they never collide with your
development database or with each other.

To point them at a PostgreSQL instance other than the Compose one:

```bash
export CCP_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=..."
```

---

## 7. What exists

Eleven capability modules — identity, organization, authorization, security,
audit, workflow, notifications, documents, integrations, configuration — plus an
operations surface reporting on the Platform's own machinery. Each has a screen
in the portal, in both languages.

[DEVELOPMENT_STATUS.md](../../DEVELOPMENT_STATUS.md) is the authority on what is
finished and what is not, and it names every known gap. Read it rather than
inferring completeness from the fact that a folder exists.

The `diagnostics` endpoints remain: they prove the pipeline end to end — module
registration, correlation ids, the error contract, paging validation — and the
integration tests assert the exact response shapes against them without needing a
real module.

### Changing an endpoint

The API contract is generated from the endpoints themselves and committed, and
CI fails if the committed copy has drifted. After changing any endpoint or DTO:

```bash
bash scripts/regenerate-contract.sh
cd frontend && npm run generate:types
```

Both results are committed. Without this the portal would keep generating its
types from a stale document and still pass — which is the failure the generation
exists to prevent, wearing a different hat.

---

## 8. Common problems

**`Connection string 'Platform' is not configured`**
The `.env` file is missing, or your shell has not loaded it. `dotnet run` does
not read `.env` automatically — export the variable, or use a tool such as
`dotenv`, or set it in `launchSettings.json` locally (that file is committed,
so put no real secret in it).

**`/health/ready` returns 503**
The database is unreachable. Check `docker compose ps` and that migrations have
been applied. This is the readiness check working correctly, not a bug.

**Integration tests fail to connect**
PostgreSQL is not running, or `CCP_TEST_POSTGRES` points somewhere else. The
tests need permission to `CREATE DATABASE`.

**Every module endpoint answers 500, but the Platform reports healthy**
Only the kernel migration was applied. There are twelve. See §4 — this is the
single most common way to get a Platform that looks fine and works for nothing.

**A query is cancelled after sixty seconds**
That is `statement_timeout`, and it is doing its job. No request the Platform
serves is a legitimate minute of database work, so this is a missing index or a
lock nobody expected. Migrations are exempt; see
[../deployment/backup-and-recovery.md](../deployment/backup-and-recovery.md) §6.

**`dotnet ef` is not recognised**
The global tool is not installed, or `~/.dotnet/tools` is not on your `PATH`.

**Build fails on a warning**
Warnings are errors by design (`Directory.Build.props`). Fix the warning rather
than suppressing it; if a rule genuinely does not apply to this codebase, add
it to `.editorconfig` **with a written justification**, as the existing
suppressions have.

---

## 9. Before you commit

- No secret in any file, including test fixtures and examples
- `dotnet build` clean — no warnings
- `dotnet test` green, including the architecture tests
- `dotnet format --verify-no-changes` clean
- Every new endpoint declares a permission or explicitly allows anonymous
- `bash scripts/regenerate-contract.sh` and `npm run generate:types` run if any
  endpoint or DTO changed, and both results committed
- Documentation updated in the same change, not deferred
- Anything you knowingly left undone recorded in `DEVELOPMENT_STATUS.md`,
  including the reason. The debt register is why this project can be trusted: a
  gap that is written down is a decision, and one that is not is a surprise
  waiting for somebody else.
