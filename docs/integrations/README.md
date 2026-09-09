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

**The known gap:** between the check and the connection, a name can be
re-resolved to a different address — DNS rebinding. Closing it means connecting
to a checked address rather than to a name. Recorded as debt rather than
pretended away; the allow-list means an attacker would first need control of a
host somebody deliberately allowed.

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

## 8. When a provider is down

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

## 9. Permissions

| Permission | For |
|---|---|
| `platform.integrations.view` | Providers, health, the call log |
| `platform.integrations.manage` | Registering and configuring providers |
| *(none — signature only)* | Inbound webhooks |

Registering a provider demands a second factor: it decides where the Platform may
send data and which credential travels with it.
