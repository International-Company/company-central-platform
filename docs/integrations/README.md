# Integrations Documentation

Reference: [ARCHITECTURE.md §19](../../ARCHITECTURE.md) · [ARCHITECTURE.md §7.2.9](../../ARCHITECTURE.md).

## Planned documents

| Document | Phase |
|---|---|
| `connectors.md` — writing an `IIntegrationConnector` | Phase 12 |
| `credentials.md` — secret manager references and rotation | Phase 12 |
| `resilience.md` — timeout, retry, circuit breaker, bulkhead | Phase 12 |
| `webhooks.md` — inbound signature verification and replay protection | Phase 12 |
| `integration.md` — **how a business system makes an outbound call** | Phase 12 |

## Rules

- Business applications do **not** call external providers directly. Everything goes through this layer.
- Provider credentials are stored as **references** to the secret manager, never as values in the database.
- Outbound targets are restricted to an allow-list of hosts (SSRF prevention).
- Every call is logged with sensitive fields redacted.
- Connectors are built when a real provider is needed — never speculatively.

