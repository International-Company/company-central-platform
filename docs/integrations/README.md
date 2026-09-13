# Integrations — one governed door to the outside world

Reference: [ARCHITECTURE.md §19](../../ARCHITECTURE.md).

**Every outbound call the Platform makes goes through this layer.** Not most of
them. All of them.

---

## 1. Why a layer at all

Uncontrolled outbound calls scattered across business systems mean credentials
scattered across business systems, no shared retry behaviour, no unified log,
and no way to answer *what did we send them, and when?* — which is the only
question that matters when an integration is disputed.

```
Business application
        │  (never calls the outside world directly)
        ▼
Platform integration layer   ← registry, credentials, resilience, logging, allow-list
        ▼
External provider   (bank, SMS gateway, government API, …)
```

The layer **transports; it does not interpret**. A body is a string it carries.
A connector that understood invoices would be a business rule living in the
Platform, which is the one thing this project exists not to have.

---

## 2. Registering a provider

```http
POST /api/v1/integrations/providers
{ "code": "acme-bank", "name": "Acme Bank", "baseAddress": "https://api.acme.test" }

PUT /api/v1/integrations/providers/{id}
{
  "timeoutSeconds": 10,
  "maxRetries": 2,
  "failuresBeforeBreaking": 5,
  "breakDurationSeconds": 30,
  "maxConcurrentCalls": 8,
  "redactedFields": ["cardNumber", "cvv", "nationalId"],
  "credentialReference": "integrations/acme-bank/api-key"
}
```

Then its operations:

```http
POST /api/v1/integrations/providers/{id}/endpoints
{ "key": "transfer", "method": "POST", "pathTemplate": "v1/transfers/{account}" }
```

A caller names a provider and an operation. **It never constructs a URL** — that
is what makes the allow-list mean anything, and it is why a path template must be
relative. An absolute one would let an endpoint definition point at a host the
provider was never registered for.

### From the portal

The Integrations screen registers a provider — code, name, base address — and
edits its resilience settings, its redacted field list and its credential
reference. Both behind `platform.integrations.manage`.

Registration deliberately does not ask for resilience. A provider arrives with
the Platform's defaults and is tuned afterwards, because a form demanding five
numbers before it would accept anything gets answered by guessing, and the
guesses then look like decisions.

**Endpoints are still an API call.** They are the part of a provider that
business applications depend on by key, and adding one is a change to a contract
somebody else is calling — which belongs with that change rather than in a form.

---

## 3. Credentials are names, never values

`credentialReference` holds something like `integrations/acme-bank/api-key`. The
value is resolved at call time, from wherever the deployment keeps secrets.

**There is no column in this module that could hold a secret.** A database backup
that leaks is therefore not a credential leak, and rotating one is an operation
on the secret store with no deployment and no downtime.

Pasting a value where a name belongs is refused — anything long, or carrying a
recognisable secret prefix. It is a guard rail rather than a guarantee; the
guarantee is the absent column.

The default resolver reads the environment:

```
integrations/acme-bank/api-key
    ↓
CCP_Secrets__INTEGRATIONS__ACME_BANK__API_KEY
```

References are confined to a `Secrets` section, so one cannot be pointed at
`ConnectionStrings:Platform` and used to read the database password out of the
Platform's own configuration. Moving to a cloud secret manager later is one
implementation of `ISecretResolver` and no change to any provider row.

---

## 4. Where the Platform may go

**Deny by default.** An empty allow-list is a Platform that makes no outbound
calls at all — the safe direction, and one an operator notices in a minute.

```jsonc
"Integrations": {
  "AllowedHosts": [ "api.acme.test", ".partner.example" ]
}
```

Exact match, or a subdomain when the entry begins with a dot. `.partner.example`
covers `api.partner.example`; `partner.example` without the dot would also match
`notpartner.example`, which somebody else can register — so the dot is required.

### Two checks, and both must pass

The **name** must be on the list, and every **address it resolves to** must be
public. The second exists because a name on the list today can be pointed at
`127.0.0.1` tomorrow by whoever controls its DNS.

The attack is worth stating plainly, because it does not look dangerous until it
happens. A layer that will fetch a URL somebody else chose is a proxy into the
network it runs in:

| Reachable from inside | What it gives away |
|---|---|
| `169.254.169.254` | The cloud metadata service. Hands out the instance's credentials to anything that asks. |
| `127.0.0.1`, `10.x`, `172.16–31.x`, `192.168.x` | Internal services, usually unauthenticated because "they are not reachable from outside". |
| `file://`, `gopher://` | Local files, and services that never expected an HTTP request. |

All refused, along with credentials in the URL (`https://allowed.test@evil.test/`
points at `evil.test` and reads as the allowed host).

A caller is told only that the address was refused. Learning *which* check failed
would let it map the internal network one probe at a time; the Platform's own log
records exactly which.

### And the check that has the last word

Everything above works in names. The allow-list is a list of names, a provider's
base address is a name, and the check before a call resolves that name and
approves what it finds. **If the HTTP client then resolves the name a second time
to connect, the approval and the connection are about different addresses** — because
whoever runs that name's DNS chooses the second answer. That is DNS rebinding,
and it turns a host somebody deliberately allowed into a route to the metadata
service.

The fix is not a better check. It is one lookup instead of two: the guard opens
every outbound socket itself, resolves once, approves what it resolved, and
connects to **those addresses**. There is no second lookup to poison.

The same callback catches a destination nobody upstream ever saw. A redirect
sends the client to a host the original URI never named, and the connection layer
is the only place that learns where — so the allow-list is consulted there too.

Two consequences worth knowing:

- A connection refused this way is logged as **blocked**, not failed. "We would
  not connect" and "they did not answer" are different answers to the question an
  operator is asking.
- It is **not retried** and does not count towards opening the provider's
  circuit. A policy decision does not become truer on the third attempt, and a
  provider that is perfectly healthy should not be marked down because somebody
  pointed it somewhere it may not go.

Pooled connections are given a two-minute lifetime for the same reason. The
default is forever, which would mean a connection approved once is kept no matter
what the name resolves to afterwards.

### Everything that goes out, counted

There are exactly three outbound clients in the Platform, and a test fails the
build when a fourth appears without a reason recorded beside it
(`OutboundCoverageTests`).

| Client | How it is governed |
|---|---|
| The integration connector | The door itself. Its primary handler is the guard, so every socket it opens is checked and connected to the address that was checked. |
| SMTP (notifications) | Asks `IOutboundGateway` to approve the mail host before dialling, and records every attempt in the call log. |
| Breached-password screening (identity) | The same, before calling the range API. Off by default; enabling it means allow-listing the host too. |

**The count exists because the rule was previously kept by memory.** Both the
email channel and the breach checker were written before this layer existed, both
reached third parties on the public internet, and both were found by reading the
code rather than by anything failing. A second instance is what turns a defect
into a class — and a class is closed by a guard, not by a correction.

What the guard cannot check is whether a listed client actually asks the door:
that is a call at the top of a method, not a shape a scan can see. Each client
has its own test refusing at the door and then insisting nothing was sent.

**A forward proxy would defeat this**, and fails closed rather than quietly. If
`HTTP_PROXY` is set in the environment, every connection is made to the proxy
instead — and the proxy's own host is not on the allow-list, so calls are refused
rather than sent unchecked. Routing outbound traffic through a proxy is a change
to make deliberately, in this file, not by an environment variable nobody
noticed.

---

## 5. Resilience, applied identically to everybody

Four things, in a fixed order, and the order decides what each protects:

| | Why there |
|---|---|
| **Bulkhead** (outermost) | A slow provider absorbs every thread the Platform has if nothing stops it. The symptom is not "the integration is slow" — it is that the whole Platform stops answering. |
| **Circuit breaker** | Sees the outcome of the retries, so it opens on genuine repeated failure rather than on one blip. |
| **Retry** | So each attempt gets a fresh timeout. |
| **Timeout** (innermost) | Bounds one attempt. Outside the retry it would bound the whole sequence, and the third attempt would inherit whatever the first two left. |

Retries use exponential backoff **with jitter**. Without it, every caller whose
request failed at the same moment retries at the same moment, and a provider
coming back from an outage is met by the whole backlog at once — which is how a
recovery becomes a second outage.

**Not every failure is retried.** A 400 means the request was wrong and will be
wrong again; a 401 means the credential is wrong, which a retry cannot fix. A 429
*is* retried, because the provider has explicitly said "later".

The pipeline is built from each provider's own settings and cached, so a circuit
breaker keeps its memory between calls — one built per call would have none,
which is an expensive way of doing nothing.

---

## 6. The call log

Every call: provider, endpoint, correlation id, request, response, status,
duration and attempt count.

**Payloads are redacted before storage, not on the way out.** Storing the real
payload and hiding it at read time leaves the secret in the database, where the
next query, the next export and the next backup will find it.

Which fields are sensitive is declared **per provider**, because only its owner
knows which of its fields carry a card number or a national id. Matching is by
field name at any depth: path-based matching would be more precise and would miss
the same field nested one level deeper than whoever wrote the policy expected —
and the failure mode of being too precise here is a leak.

A payload that is not JSON is not passed through. It cannot be inspected, so it
cannot be shown to be safe; its shape is recorded instead.

Retention is ninety days by default, swept in bounded batches. The log grows with
traffic rather than with the company, which is why it has a retention policy from
the first day rather than from the day somebody notices the table is the largest
in the database.

---

## 7. Inbound webhooks

```http
POST /api/v1/integrations/webhooks/acme-bank
X-CCP-Timestamp: 1789041720
X-CCP-Signature: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08

{ "event": "payment.succeeded", "id": "pay_123" }
```

The signature is `HMAC-SHA256(secret, "{timestamp}.{raw body}")`, hex-encoded,
where the secret is the provider's credential reference resolved at verification
time.

**An inbound webhook is untrusted input from the internet.** The URL is guessable
and often published; anybody can post to it. Three things must be true:

1. The provider has a signing secret.
2. The signature matches the **raw** bytes. Parsing and re-serialising first
   would change whitespace and key order and produce a different signature.
3. The request has not been seen before.

**The timestamp is inside the signed material**, not beside it. If it were only a
header, an attacker replaying a captured request would simply change it, and the
five-minute window would be checked against a value they control.

Replay detection remembers every accepted signature until the window has passed.
A correctly signed request that arrives twice is authentic both times — the
signature proves who sent it and says nothing about whether it has been acted on.
A unique index settles the race when two copies arrive at the same instant.

Every refusal returns the same error. Telling a sender that the signature was
wrong but the timestamp was fine tells an attacker which half to work on.

---

## 8. Outbound webhooks — telling a business system what happened

The other direction, and the one that took two phases to arrive.

**The Platform decides *that* something happened; the business application
decides what that means** (§16.4). Until now the only way for it to find out was
to poll.

```http
POST /api/v1/integrations/subscriptions
{
  "applicationId": "…",
  "name": "Payroll",
  "endpoint": "https://payroll.acme.test/platform-events",
  "eventTypes": ["workflow.instance.completed", "identity.user.disabled"],
  "secretReference": "integrations/payroll/webhook-secret"
}
```

### What arrives

```http
POST https://payroll.acme.test/platform-events
X-CCP-Signature: v1=9f86d081…
X-CCP-Timestamp: 1789041720
X-CCP-Event-Id: 0192f3…
X-CCP-Event-Type: workflow.instance.completed
X-CCP-Attempt: 1
Content-Type: application/json; charset=utf-8
```

**The same signature scheme the Platform demands of its own providers**, in the
same shape: `HMAC-SHA256(secret, "{timestamp}.{raw body}")`. Symmetry is the
point — one implementation to get right, one to explain, and §7 above already
documents how to verify it.

`X-CCP-Event-Id` is stable across retries. **Delivery is at-least-once**: a
response lost on the way back is indistinguishable from one that never arrived,
so the Platform tries again and the receiver needs something to deduplicate on.

### Which event types exist

```http
GET /api/v1/integrations/event-types
```

Every type the running Platform can send, read from the event records it ships
rather than from a list somebody keeps, so it cannot disagree with what is
actually raised. The portal's subscription form offers this list as choices.

**A subscription naming a type that is not on it is refused**, with
`INTEGRATIONS.SUBSCRIPTION_EVENT_TYPES_UNKNOWN` and every unknown type named.
That was not always so: the form was a comma-separated text box, nothing checked
it, and a typo was accepted and then received nothing, silently and for ever.
Five declared types could never be sent at all, and subscribing to one of those
was accepted too (DEVELOPMENT_STATUS.md §7, #87).

Matching is exact, because delivery is exact: `Workflow.Task.Assigned` is not
`workflow.task.assigned`.

### Why event types are named one by one

There is deliberately no way to subscribe to everything. A subscription that
received every event would receive ones added years later, and the first its
owner would know is a parser failing on a shape nobody told them about.

### The part that made this wait for Phase 12

**A subscription is an SSRF primitive if it is not governed.** Somebody who can
register a URL and have the Platform post to it has a proxy into the network the
Platform runs in.

So every delivery goes out through the same door as every other outbound call:
the allow-list, the private-address check, and the guarded socket that connects
to the address it checked (§4). The address is checked **when the subscription is
registered**, not when an event fires — a subscription nobody can deliver to is a
subscription whose owner believes they are being told things, and the failure
would otherwise surface weeks later in a sweep summary nobody reads.

A payload is never sent unsigned. If the signing secret cannot be resolved the
delivery fails and retries; sending it unsigned because the secret store was
briefly unavailable would teach receivers to accept unsigned messages.

### Retry, and giving up

| | |
|---|---|
| Attempts | 6, over roughly an hour |
| Backoff | Exponential, **with jitter** — without it every delivery queued during an outage retries at the same instant when the endpoint returns, which is how a recovery becomes a second outage |
| After the last attempt | `Abandoned`, and the row is kept |
| After 20 consecutive failures | The **subscription** is suspended |

The row is kept because "we tried six times over an hour and your endpoint
refused every one" is the answer to the question a subscriber eventually asks,
and a delivery mechanism that erased its own failures could only answer with an
opinion.

Suspension is on the subscription rather than the delivery, so a hundred queued
events to a dead endpoint suspend it once. **Suspended, not deleted** — the
owner's configuration survives, and resuming clears the count that suspended it,
because resuming with the failures still recorded would suspend it again on the
next failure and look like resuming had done nothing.

```http
GET  /api/v1/integrations/subscriptions/{id}/deliveries
POST /api/v1/integrations/subscriptions/{id}/resume
```

### Queued, then posted

The fan-out runs while the outbox relay holds a transaction, so it writes rows
and sends nothing. Posting to somebody else's server from inside that
transaction would hold a database transaction open for as long as their slowest
endpoint takes, and a subscriber that never answers would stall the relay for
everybody.

One row per subscriber, never one row with a list: a single row cannot express
"delivered to two of three, retrying the third", which is the ordinary state of
affairs rather than an edge case.

---

## 9. When a provider is down

A provider being down must degrade **one capability, not the Platform**.

- The circuit opens and calls fail immediately without reaching it.
- Each refusal is logged as `CircuitOpen`, because "we did not call them" answers
  a question that an absence of rows does not.
- `GET /api/v1/integrations/providers/health` reports each provider from its own
  recent calls — derived, never stored, because a health column goes stale the
  moment somebody forgets to update it.

A provider with fewer than three recent calls reads as **Idle**, not Healthy.
Nothing has been asked of it, so nothing is known, and a green light nobody
earned is worse than an honest blank.

Disabling a provider stops calls at the connector, so every module using it
degrades in the same way at the same moment.

---

## 10. Permissions

| Permission | For |
|---|---|
| `platform.integrations.view` | Providers, health, the call log |
| `platform.integrations.manage` | Registering and configuring providers, and administering outbound subscriptions |
| *(none — signature only)* | Inbound webhooks |

Registering a provider demands a second factor: it decides where the Platform may
send data and which credential travels with it.
