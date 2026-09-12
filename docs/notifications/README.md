# Notifications — telling people things

Central, reliable, multi-channel. **Sending enqueues; a background dispatcher
delivers.**

---

## 1. Sending something

```csharp
await sender.SendAsync(new SendRequest(
    RecipientUserId: userId,
    TemplateCode: "workflow.task.assigned",
    Category: "workflow",
    Variables: new Dictionary<string, string>
    {
        ["resourceType"] = "purchase-order",
        ["resourceId"] = "PO-2026-0041",
        ["step"] = "manager-review",
        ["dueAt"] = "1 October 2026"
    }));
```

That returns as soon as a row is written. Talking to a mail server is slow and
fails for reasons that have nothing to do with the action that caused the
message, so a send that blocked on SMTP would make every action in the Platform
as slow and as unreliable as the slowest thing it notifies about.

### Everything that can go wrong happens before anything is queued

- The template must exist **in the recipient's language**.
- Every variable it declares must be supplied.
- The person must not have turned this category off.

A notification reaching the queue is one that will be attempted. A message
discovered to be wrong at delivery time is a message already halfway to
somebody.

---

## 2. Templates

One row per message per language. `code` + `locale` is the key.

```http
PUT /api/v1/notifications/templates
Authorization: Bearer <token with platform.notifications.manage>

{
  "code": "purchasing.order.shipped",
  "locale": "ar",
  "subject": "شُحن طلبك",
  "body": "<p>الطلب {{orderNumber}} شُحن في {{shippedAt}}.</p>",
  "variables": ["orderNumber", "shippedAt"]
}
```

### Every template exists in Arabic and English

A send in a locale with no template is **refused**, not substituted. A director
receiving an approval request in the wrong language is a failure the company
sees, and a silent English fallback is how that ships.

### Variables are declared, not inferred

A template that discovered its own variables by scanning its body would accept a
typo as a new variable and render an empty space where a number should be.
Declaring them makes an undeclared placeholder an authoring error, and a missing
value a send error.

The reverse is allowed: a declared variable the body never uses is harmless.

### Substitution only — there is no template language

`{{name}}` and nothing else. No conditionals, no loops, no property paths. A
template language is a language, and a language stored in a database is code
nobody reviews running with the Platform's privileges.

### Every value is escaped

Without exception and with no way to opt out. A notification body reaches an
email client and an in-app inbox, both of which render markup, and a display
name containing a script tag is the whole of a stored cross-site-scripting
attack if it arrives unescaped.

The **body** is never escaped — an administrator writing it may legitimately use
markup. The **values** always are.

### Revising bumps the version

A notification records which template and which version produced it, so "what
did we actually send them?" still has an answer after two revisions.

---

## 3. Channels

| Channel | State |
|---|---|
| In-app | Always available. Cannot fail. |
| Email | Off by default; SMTP once configured. |
| SMS, Push | Designed for, deliberately not built (§17.2, Q9). |

In-app cannot fail, and that is its point: every other channel depends on
somebody else's server being up, and this one gives a person somewhere to look
when none of them were.

Email is off by default because a Platform that starts talking to a mail server
the moment it boots is one that emails real people from somebody's laptop.
Enabling an outbound channel is the owner's decision.

### Email goes through the governed door

**It was the one outbound call in the Platform that passed no door at all.** The
channel opened a socket to a mail server directly: no allow-list, no entry in the
call log, and no way for an operator to discover the Platform had been failing to
send anything for a day. Recorded twice — as #33 and again as #45 — and the
reason it stayed open is written in both: **SMTP is not HTTP**, so the
integration connector cannot carry it.

What can be carried is the part that was never about HTTP. An allow-list is a
question about a host name; a call log is a row. So the mail host is checked
against the same allow-list and the same private-address rules as every other
outbound call ([integrations §4](../integrations/README.md)), and every attempt
lands in the same call log under the provider code `smtp` — which puts the mail
server in the health view beside the HTTP providers rather than in a category
nobody thinks to open.

A refused host is a **permanent** failure. Retrying a configuration fact for half
an hour delays every message queued behind it and then reports the same answer.

**The host is recorded, never the recipient.** The call log is read by
administrators and exported; a list of who was emailed is not theirs to browse,
and this would be the one place in the Platform where that list existed.

**No second retry policy.** The dispatcher already retries with backoff and gives
up after five attempts. A circuit breaker here as well would be two policies over
the same failure with no way to reason about the result — the same argument that
keeps the HTTP connector's handler chain free of retries.

### Adding one

Implement `INotificationChannelProvider` and register it. That is the whole
change — no edit to dispatch, to templates, or to any caller:

```csharp
services.AddScoped<INotificationChannelProvider, SmsChannelProvider>();
```

A provider **classifies its failures and does nothing else with them**:

```csharp
DeliveryOutcome.Delivered("accepted");     // done
DeliveryOutcome.Transient("server busy");  // try again
DeliveryOutcome.Permanent("no such address"); // never try again
```

Retry, backoff and giving up belong to the dispatcher, so every channel behaves
the same way when a vendor is down and a provider written next year cannot
invent its own policy by accident.

Getting the transient/permanent distinction wrong is expensive in both
directions: retrying a dead address for half an hour delays every message behind
it, and abandoning a timeout loses a message because a server was busy for a
second.

---

## 4. Delivery

The dispatcher polls, attempts, and records **every attempt** — not just the
last. "It failed three times and then worked" and "it worked first time" are
different facts, and only the first says the provider is unhealthy.

Backoff is exponential **with jitter**. Without the jitter, a mail server coming
back after an outage is met by every queued message at the same instant, which
is how a recovery becomes a second outage.

After the configured number of attempts, or immediately on a permanent failure,
the notification is abandoned — and **stays visible** in the delivery log. A
message nobody received and nobody can see was never sent, and the person who
needed it finds out some other way, usually badly.

---

## 5. Preferences

Per user, per category, per channel. **A row exists only when somebody has said
no**, so a new employee receives everything without anybody generating a
preference row per category per channel for them, and a category invented next
year works for existing users with no migration.

```http
PUT /api/v1/me/notification-preferences
{ "category": "workflow", "channel": "Email", "isEnabled": false }
```

### Security notifications cannot be turned off

Refused at the resolver **and** at creation, so no stored row can exist that a
future reader might respect by accident.

They tell somebody their account may have been taken, and the person most likely
to want them silenced is whoever took it. Not a policy choice — a structural
one.

---

## 6. What the Platform sends for itself

Sixteen templates, eight messages in both languages: a task assigned, a task
overdue, a request approved/rejected/cancelled, a password changed, a password
reset, a second factor enrolled.

Seeded at startup and **never overwritten**. An administrator who rewords one
keeps their wording; a seeder that overwrote on boot would silently undo
somebody's work on every deployment.

---

## 7. Subscribing to what other modules do

Notifications listens; the raising module knows nothing about it. That is why
Workflow's escalation sweep could be written without deciding what "escalate"
means to a person, and why adding a channel changes nothing in the workflow
engine.

To notify on a new event, write an `IIntegrationEventHandler<TEvent>` and
register it. The event types live in the raising module's **Contracts**
project — subscribing never means referencing another module's internals.

---

## 8. Permissions

| Permission | For |
|---|---|
| `platform.notifications.manage` | Writing templates |
| `platform.notifications.view` | The delivery log |
| *(none)* | Your own inbox and your own preferences |

Reading one's own messages needs no permission. Gating it would mean granting
that permission to everybody, which makes it meaningless.
