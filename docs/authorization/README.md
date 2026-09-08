# Authorization Documentation

Reference: [ARCHITECTURE.md §14](../../ARCHITECTURE.md) · [ADR-007](../architecture/adr/ADR-007-authorization-strategy.md) · [ADR-012](../architecture/adr/ADR-012-application-registry.md).

## Planned documents

| Document | Phase |
|---|---|
| `model.md` — roles, permissions, assignments, scope | Phase 4 |
| `permission-naming.md` — the `<application>.<resource>.<action>` convention | Phase 4 |
| `scope.md` — Self / Unit / UnitAndBelow / All, and how each filters data | Phase 4 |
| `integration.md` — **how a business system declares and checks its permissions** | Phase 4 |
| `application-registry.md` — registering a consumer system | Phase 11 |

## The extensibility point

A future business system declares its own permissions under its own namespace:

```
finance.invoices.approve
hr.leave-requests.view
```

The Platform stores and evaluates these without knowing what they mean. This is how the Platform serves systems that do not exist yet — see [ADR-012](../architecture/adr/ADR-012-application-registry.md).

