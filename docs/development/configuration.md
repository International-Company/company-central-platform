# Configuration and feature flags

Typed, scoped, audited settings — and switches that turn a capability off
without a deployment.

---

## 1. Settings are declared before they are set

A store of free-form key-value pairs looks simpler for about a week. Then nobody
can say what keys exist, what a value means, or whether a typo created a new
setting or broke an old one.

```http
PUT /api/v1/configuration/settings/declare
{
  "key": "finance.invoices.approval-timeout",
  "valueType": "Duration",
  "description": "How long an approval waits before it escalates",
  "defaultValue": "3.00:00:00",
  "minimum": null,
  "maximum": null
}
```

**The namespace comes from your token**, exactly as the permission manifest
does — so a system can only ever declare settings it owns. One system quietly
redefining another's behaviour is the failure this prevents.

A key is `application.area.setting`: three parts at least, in your namespace.

### Types

| Type | Accepts | Bounded by |
|---|---|---|
| `Text` | Anything | Length |
| `Number` | A whole number | Value |
| `Boolean` | `true` / `false` | — |
| `Duration` | `00:15:00`, `3.00:00:00` | — |
| `Url` | Absolute http or https | — |

`allowedValues` closes the list. Most settings that look like free text are not —
a log level, a locale, a strategy name — and typing one wrongly should be refused
when it is typed, not discovered by the code that reads it.

Declaring is **idempotent**: send your declarations on every startup. A second
startup must not be an error, and re-declaring never disturbs a value somebody
has set.

---

## 2. What does not belong here

Configuration governs **how the Platform behaves**: how long a session lasts, how
large a document may be, whether a capability is on.

It does not hold **business parameters** — a tax rate, a fee, an approval
threshold. Those belong to the business system that understands them. Putting one
here would make the Platform hold a rule it cannot reason about, which is the
boundary this whole project exists to keep.

The test is simple: if changing the value would change what a *number means*
rather than how the Platform *behaves*, it is not a setting.

---

## 3. Secrets are refused

**A setting is stored in plaintext, exported, backed up and shown on a screen.**
A secret put here is a secret in all of those places, and the person who put it
there did so because it was convenient — which is exactly when it happens.

So a value that looks like a credential is refused outright: a PEM block, a
recognisable token prefix, a JSON Web Token, a connection string carrying a
password, a long high-entropy string.

**Marking a setting sensitive does not make it a safe home for one.** Sensitivity
stops a value being read back; it does not stop it being in the database, in the
backup, or in the hands of whoever gets a copy.

Secrets live in the secret store and are named by reference — see
[`docs/integrations/README.md`](../integrations/README.md) §3.

This is a guard rail and says so. It catches the recognisable paste and will not
catch a short password somebody typed; a heuristic that tried to would refuse
half the legitimate values in the Platform.

### Sensitive settings

`isSensitive` marks a value that may be **written and never read** — an internal
hostname, a support contact nobody should harvest.

The value is **absent from the response shape** rather than masked by the caller,
so there is no code path that could return it by forgetting to check. Its change
history says that it changed and not what to: a value that cannot be read back
through the API but sits in plain sight in its own change log has not been
protected, it has been moved.

---

## 4. Scopes, and which one wins

```
Application   ← narrowest, wins
Company
Platform      ← broadest, the fallback
Definition default   ← when nothing is set anywhere
```

**Narrowest wins.** It is the only rule anybody can hold in their head, and the
alternative — where something broader can override something narrower — produces
the case where changing a Platform default silently undoes somebody's deliberate
local decision.

```http
PUT /api/v1/configuration/settings/value
{
  "key": "finance.invoices.approval-timeout",
  "scope": "Company",
  "scopeId": "8f3a2b1c-…",
  "value": "5.00:00:00",
  "reason": "Board asked for a longer window during the audit"
}
```

**A row exists only where somebody has overridden something.** A setting nobody
has touched has no row, so adding a setting costs nothing and a company that has
customised three things has three rows.

Sending `"value": null` clears the override, and the setting falls back to the
scope above. That is a different act from setting it to an empty string.

---

## 5. Every change is kept

Old value, new value, who and when — plus a reason, which is optional and worth
supplying. Six months later the value explains *what*, and only the reason
explains *why*.

```http
GET /api/v1/configuration/settings/{key}/history
```

`"what is it now"` is never the question being asked when a behaviour changed and
nobody remembers doing it.

---

## 6. A change takes effect on the next request

Settings are cached, and the cache is keyed on a **version stamp held in the
database** rather than on a time-to-live.

That distinction is the whole correctness property. A time-to-live leaves a
window — however short — in which a capability somebody deliberately switched off
is still on, and "however short" is not something anyone can reason about while
deciding whether to switch it off.

The stamp lives in the database rather than in memory so that an instance which
did not make the change still notices it. Without that, a Platform running three
instances would apply a setting change on one of them.

It is the same mechanism the permission resolver uses, deliberately: two caching
strategies in one Platform is one more thing to reason about during an incident.

---

## 7. Feature flags

```http
PUT /api/v1/configuration/flags/declare
{ "key": "finance.invoices.bulk-approve", "description": "Approve several at once" }

PUT /api/v1/configuration/flags/finance.invoices.bulk-approve
{ "isEnabled": true, "roleIds": ["…"], "unitIds": ["…"] }
```

**The reason for a flag is almost never the launch.** It is the evening somebody
has to turn a thing off while they work out what it is doing, and a system where
the only way to do that is a deployment is a system where the answer at eight
o'clock is "we cannot".

### Targeting

By **role** or by **organizational unit**, and by nothing else. Not by a
percentage, not by an arbitrary attribute, not by a rule language — each of those
turns "who has this?" into a question that needs a simulator to answer, and a
flag nobody can reason about is worse than no flag.

- A unit target covers everything beneath it.
- Targets **add up**: either kind is enough. Requiring both would make "the
  finance team and anybody with this role" impossible to express, which is the
  ordinary case.
- Targeting nothing means **everybody**, not nobody. A flag that was on and
  reached nobody would look broken and be working.

### Two things that are deliberate

**A new flag is off.** One created in advance of the thing it guards must not
switch that thing on the moment the row appears.

**An undeclared flag is off.** A typo in a flag key must not release a capability,
and defaulting to on would make every misspelling a silent launch.

**Off is off.** Targeting cannot switch a flag on for anybody, which is what makes
the master switch trustworthy at eight in the evening.

---

## 8. Reading configuration from Platform code

```csharp
TimeSpan timeout = await reader.GetDurationAsync("platform.sessions.timeout");
bool isOn = await reader.IsFeatureOnAsync(key, callerRoles, callerUnitChain);
```

There is deliberately **no endpoint that reads one setting's value.** Reading the
list returns what is set with sensitive values absent; a single-value endpoint
would be the obvious place for somebody to later add a convenience that returns
them. The Platform reads its own settings in process, where no HTTP caller can
reach.

---

## 9. Permissions

| Permission | For |
|---|---|
| `platform.configuration.view` | Settings, flags, change history |
| `platform.configuration.manage` | Declaring and changing |
| *(none)* | Asking whether a feature is on for yourself |

Asking about your own feature state needs no permission. Gating it would mean
granting that permission to everybody, which makes it meaningless.
