# Development Documentation

## What is here

| Document | What it answers |
|---|---|
| [`getting-started.md`](getting-started.md) | Clone to a running Platform, both halves, with the twelve migration histories and the first administrator |
| [`integration-guide.md`](integration-guide.md) | How to connect a business system to the Platform, without reading Platform source |
| [`configuration.md`](configuration.md) | Declaring settings and feature flags, and why a setting refuses a value that looks like a secret |

## Not written, and honestly so

| Document | Why it does not exist |
|---|---|
| `coding-standards.md` | The standards are enforced rather than described: warnings are errors, `dotnet format` gates the build, and the architecture tests refuse a violation outright. A prose restatement of a rule the compiler already enforces is a second copy that drifts. What is *not* mechanisable is in [`getting-started.md`](getting-started.md) §9. |
| `testing.md` | Same reason, mostly. Which suite to run is in [`getting-started.md`](getting-started.md) §6; what to test at which level is visible in the suites themselves, each of which opens with why it exists. |
| `adding-a-module.md` | Worth writing, and not written. The pattern is eleven times consistent, so a new module is best begun by copying the smallest existing one (Configuration) — but that is folklore, and folklore is what documentation is for. |
| `troubleshooting.md` | Folded into [`getting-started.md`](getting-started.md) §8 and [`../deployment/observability.md`](../deployment/observability.md) §6, because a problem is looked up where it is met rather than in a file named after the act of being stuck. |

## Non-negotiables

Each of these is enforced by the build, not by review or memory:

- No business logic in endpoints.
- No secret in source control — gitleaks runs on every push, with full history.
- No hardcoded user-facing text.
- No endpoint without an explicit permission or an explicit anonymous marker.
- No cross-module reference except to `.Contracts`.
- No declared metric without a caller.
- Tests accompany the change, not a later phase.
- Anything knowingly left undone is written into `DEVELOPMENT_STATUS.md` with the
  reason. A gap that is recorded is a decision; one that is not is a surprise
  waiting for somebody else.
