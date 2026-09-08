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
