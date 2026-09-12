# Company Central Platform — Documentation

| Status | Written through Phase 18 |
|---|---|
| Last updated | 2026-09-10 |

## Start here

| Document | What it is |
|---|---|
| [ARCHITECTURE.md](../ARCHITECTURE.md) | The architecture: scope, boundaries, modules, and every technical layer |
| [PROJECT_PLAN.md](../PROJECT_PLAN.md) | The 22 phases, each with objective, tasks, tests and acceptance criteria |
| [DEVELOPMENT_STATUS.md](../DEVELOPMENT_STATUS.md) | Where the project actually stands, including blockers and risks |
| [architecture/adr/](architecture/adr/) | Why each significant decision was made |

## Areas

| Directory | Contents | Written in |
|---|---|---|
| [architecture/](architecture/) | Architecture detail and decision records | Phase 0 onward |
| [security/](security/) | Threat model, controls, secrets, hardening | Phases 2, 5 |
| [identity/](identity/) | Users, sessions, devices, organization structure | Phases 2, 3 |
| [authorization/](authorization/) | Permissions, roles, scope, integration for consumers | Phase 4 |
| [audit/](audit/) | The event model and how a system sends events | Phase 6 |
| [workflow/](workflow/) | Defining and running approval workflows | Phase 8 |
| [notifications/](notifications/) | Templates, channels, providers | Phase 9 |
| [documents/](documents/) | Upload, access control, storage | Phase 10 |
| [integrations/](integrations/) | Connectors, credentials, webhooks | Phase 12 |
| [api/](api/) | REST conventions, versioning, errors, OpenAPI | Phases 1, 11 |
| [development/](development/) | Getting started, adding a module, configuration, and the integration guide | Phase 1 onward |
| [deployment/](deployment/) | Observability and the runbook, backup and recovery, the Railway deployment | Phases 14, 17 |

## For a developer building a business system

The question this documentation must answer is: **"How do I connect my system to the Company Platform?"** — without reading Platform source code.

1. [`development/integration-guide.md`](development/integration-guide.md) — register an application, obtain a token, make a first authenticated call, declare your permissions, and use the shared services
2. [`../samples/reference-client/`](../samples/reference-client/) — the same thing as working code
3. [`../contracts/platform-api.json`](../contracts/) — every endpoint, its shapes, and **the permission each one requires**, generated from the endpoints themselves
4. [`api/versioning.md`](api/versioning.md) — what is promised, what may change, and how a deprecation is announced

The per-service integration documents planned as separate files were written into
the integration guide instead. Five documents, each with its own preamble about
authentication, is five places for the same paragraph to go out of date; a
business system integrates once, in one sitting.

**One honest caveat, recorded rather than glossed:** Phase 11's acceptance
criterion asks that the guide be validated by an outside developer actually
following it, and that has not happened (debt #40). Reading it back is precisely
the self-assessment the criterion rules out — and checking it against the
generated contract already caught two real errors, an endpoint that did not exist
and a method that was wrong, which is evidence that reading it back is not
enough.

## Writing rules

- Documentation is updated **within** the phase that changes the system, never deferred.
- If the documentation and the code disagree, that is a defect in one of them — find out which and fix it.
- No secret, credential or real personal data appears in any document or example.
- Examples must be runnable. An example that has never been run is a guess.

