# Architecture Documentation

| Document | Contents |
|---|---|
| [../../ARCHITECTURE.md](../../ARCHITECTURE.md) | The main architecture document — 27 sections |
| [adr/](adr/) | Architecture Decision Records (15 accepted) |

## Planned additions

| Document | Phase |
|---|---|
| `module-catalog.md` — the implemented shape of each module | Per module phase |
| `data-model.md` — entity relationship diagrams per schema | Phase 17 |
| `event-catalog.md` — every published integration event | Phase 6 onward |
| `fitness-functions.md` — the architecture tests and what each protects | Phase 1 |

## The rules that must not erode

1. Dependencies point one way: business applications depend on the Platform, never the reverse.
2. The Platform contains no business logic (ARCHITECTURE.md §4.4).
3. A module never references another module's internals — only its `.Contracts`.
4. No foreign key crosses a module schema boundary.
5. Every endpoint declares a permission, or is explicitly marked anonymous.

Rules 1, 3, 4 and 5 are enforced by tests that fail the build. Rule 2 is enforced by review, because no test can recognise business logic.

