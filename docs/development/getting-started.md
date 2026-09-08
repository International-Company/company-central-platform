# Getting Started

From a clean machine to a running Platform.

| | |
|---|---|
| Status | Phase 1 |
| Applies to | Backend API and kernel. The frontend arrives in Phase 7. |

---

## 1. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | **10.0.400** (pinned in `global.json`) | `dotnet --version` |
| Docker | Any recent, with a working Linux engine | `docker info` |
| Git | Any recent | `git --version` |
| Node.js | 22+ (needed from Phase 7 only) | `node -v` |

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

Install the EF tooling once:

```bash
dotnet tool install --global dotnet-ef --version 10.*
```

Then:

```bash
dotnet ef database update \
  --project src/Kernel/CCP.Kernel.Infrastructure/CCP.Kernel.Infrastructure.csproj \
  --context KernelDbContext
```

> Migrations are **never** applied automatically at application start. In a
> multi-instance deployment that means several instances racing to alter the
> same schema (ARCHITECTURE.md §21.5). Applying them is an explicit step, here
> and in production.

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

---

## 6. Test

```bash
# Everything
dotnet test

# Individually
dotnet test tests/CCP.Kernel.UnitTests          # no dependencies
dotnet test tests/CCP.Architecture.Tests        # no dependencies
dotnet test tests/CCP.Api.IntegrationTests      # requires PostgreSQL
```

The integration tests create a uniquely-named database per test class, apply
migrations to it, and drop it afterwards — so they never collide with your
development database or with each other.

To point them at a PostgreSQL instance other than the Compose one:

```bash
export CCP_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=..."
```

---

## 7. What exists after Phase 1

The Platform runs, but has **no capability modules yet** — that is by design.
Phase 1 delivers the foundation the eleven modules will be built on:

- The solution structure and layer boundaries (ARCHITECTURE.md §8.2)
- Kernel primitives: `Result`/`Error`, `Guard`, `IClock`, UUID v7, paging
- The RFC 9457 error contract, identical for every future endpoint
- Correlation ids flowing through requests, logs and (from Phase 6) audit
- Security headers, rate limiting, strict CORS
- The transactional outbox and its relay (ADR-013)
- The module registration mechanism, proved by the Diagnostics module
- Health checks, structured logging, Docker, and CI

The `diagnostics` endpoints exist to prove the pipeline end to end. They are
reduced to the ping endpoint alone when Monitoring arrives in Phase 14.

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
- Documentation updated in the same change, not deferred
