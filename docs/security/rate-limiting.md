# Rate Limiting

Reference: [ARCHITECTURE.md §12.6](../../ARCHITECTURE.md). Introduced in Phase 5.

---

## 1. Why per-endpoint and not one global limit

A single global limit cannot be right. A limit generous enough for a person
browsing a user list — hundreds of requests a minute — is enormous for password
guessing. A limit tight enough for password guessing makes the application
unusable. The two numbers are three orders of magnitude apart and cannot be
reconciled into one.

So endpoints are classed, and each class gets a number chosen for what that class
actually does.

---

## 2. The classes

| Policy | Limit | Applied to | Reasoning |
|---|---|---|---|
| `Authentication` | **10 / minute** | `/auth/login`, `/auth/refresh`, `/auth/password/forgot`, `/auth/password/reset`, `/auth/password/change`, every `/me/mfa/*` endpoint | These accept credentials or codes. Ten attempts a minute is invisible to a person who mistypes and useless to a guesser. |
| `Anonymous` | **60 / minute** | JWKS and other unauthenticated reads | No credential involved, but no accountable caller either. |
| `Write` | **120 / minute** | Creating, updating, deleting | Bounded by what a person can actually do, not by what a script could. |
| `Read` | **600 / minute** | Queries, listings, status | Generous: a busy screen makes many requests and legitimate use must not be throttled. |

All four are **sliding window**. A fixed window lets an attacker send a full
budget at the end of one window and another at the start of the next — double the
intended rate at the boundary, exactly where an attacker will aim.

A **global limiter remains as a backstop** for anything that declares no policy,
so an endpoint added without one is still bounded rather than unlimited.

---

## 3. Partitioning

Requests are partitioned by authenticated subject where one exists, and by remote
address otherwise. Behind a proxy the address comes from forwarded headers, which
the host is configured to honour — without that, every request would appear to
come from the load balancer and share one bucket.

**Address-based limiting and account lockout cover different directions of the
same problem.** Rate limiting bounds one source attacking many accounts;
account lockout bounds many sources attacking one account. Neither substitutes
for the other, and the Platform has both.

### Two directions, two limits

Sign-in needs guarding against two different attacks, and one limit cannot do
both.

| Attack | Bounded by | Default |
|---|---|---|
| Many machines guessing **one account** | Per-account limit on the authentication endpoints | 10 / min |
| One machine working through **many accounts** | Per-address limit, chained onto the global limiter | 60 / min |
| Sustained guessing of one account | That account's progressive lockout (Phase 2) | — |

**The per-account key is what fixes NAT.** Partitioning authentication by source
address was the obvious design, and it collapses in an office: every employee
shares one public address, so a ten-a-minute budget is ten sign-ins a minute for
the whole company — the eleventh person to arrive on Sunday morning is refused,
and nothing they can do helps.

This was surfaced by the integration suite, which tripped the limit for exactly
that reason: a test process behind one address is an accurate simulation of an
office behind one NAT.

Keying on the account being attacked means fifty colleagues signing in at nine
o'clock occupy fifty separate budgets, while an attacker still gets ten attempts
per account **however many addresses they spread across** — which is strictly
better than the address-based version it replaced.

**The account name comes from the request body**, so `AuthenticationTargetMiddleware`
buffers the body and lifts it out before the limiter runs. It lower-cases the
name to match storage — otherwise `Amira` and `amira` would receive separate
budgets, and there are a great many spellings. A body it cannot parse falls back
to the address, which is the safe direction to fail in; validation refuses the
request properly a moment later.

The per-address limit is chained onto the global limiter rather than attached to
the endpoints, because an endpoint carries one policy and the authentication
endpoints already carry the per-account one. Both have to apply: per-account
alone would let a single machine try ten attempts against each of a thousand
names, which is exactly what credential stuffing is.

---

## 4. What this does not solve

A distributed attacker with many source addresses defeats per-address limiting by
construction — that is what account lockout, breach screening and the security
event log are for. Rate limiting raises the cost of the cheap attacks; it does
not stop the expensive ones.

Limits are per-process. A multi-instance deployment multiplies the effective
limit by the instance count. If that becomes material, the limiter needs shared
state — noted here rather than assumed away.

---

## 5. Enforcement

`RateLimitingTests` requires every authentication-class endpoint to declare the
strict policy. An endpoint accepting a credential without one fails the build,
so the protection cannot be lost by someone adding a route and forgetting.
