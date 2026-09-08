# Notifications Documentation

Reference: [ARCHITECTURE.md §17](../../ARCHITECTURE.md) · [ARCHITECTURE.md §7.2.7](../../ARCHITECTURE.md).

## Planned documents

| Document | Phase |
|---|---|
| `templates.md` — authoring templates, variables, escaping, locales | Phase 9 |
| `channels-and-providers.md` — the provider abstraction | Phase 9 |
| `integration.md` — **how a business system sends a notification** | Phase 9 |
| `preferences.md` — per-user, per-channel, per-category | Phase 9 |

## Notes

- Every template exists in **Arabic and English**.
- Adding a channel means implementing `INotificationChannelProvider` and registering it — no change to dispatch logic or to callers.
- Security notifications cannot be disabled by user preference.
- The Platform delivers messages. Deciding *when* a business event is worth notifying is the business application's decision.

