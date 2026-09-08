# Security Headers

Reference: [ARCHITECTURE.md §12.9](../../ARCHITECTURE.md). Introduced in Phase 2,
documented in Phase 5.

---

## 1. What is sent

Applied by `SecurityHeadersMiddleware` to **every** API response.

| Header | Value | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Never let a browser guess a content type. Combined with always returning `application/json`, this blocks a class of XSS through uploaded content. |
| `X-Frame-Options` | `DENY` | The API is not a page and must never be framed. |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | URLs carry identifiers. They must not leak off-origin. |
| `Permissions-Policy` | camera, microphone, geolocation, payment, USB and others all `()` | The API needs none of these. Denying them costs nothing and removes them from an attacker's reach. |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` | A JSON API renders nothing, so the strictest possible policy applies. The frontend serves its own, necessarily looser policy for pages it actually renders. |
| `Server`, `X-Powered-By` | *removed* | Advertising the stack helps only an attacker. |

`Strict-Transport-Security` is added by `UseHsts()` outside Development, along
with HTTPS redirection. It is off in Development because pinning HSTS against
`localhost` in a developer's browser is difficult to undo.

---

## 2. The bug this design exists to prevent

Headers are set inside `Response.OnStarting`, not written directly when the
middleware runs.

The first implementation set them directly. The exception boundary calls
`Response.Clear()` before writing a Problem Details body — which discarded every
header this middleware had set. The result was that **every 4xx and 5xx response
went out unprotected**: no CSP, no `nosniff`, no framing protection. Exactly the
responses an attacker is most likely to be looking at.

`OnStarting` runs immediately before the response is written, after any clearing,
so the headers survive. This was found by checking the headers on a 500 response
rather than assuming they matched a 200 — and there is now a regression test that
asserts precisely that.

---

## 3. CORS

A strict origin allow-list, from `Platform:AllowedOrigins`. **Wildcards are
prohibited.** An empty list means no cross-origin access at all, which is the
correct default for a deployment that has not yet been told who its frontend is.

`AllowCredentials()` is set, which is why the wildcard prohibition is not merely
stylistic: browsers reject the combination, and a permissive origin with
credentials would be a genuine hole rather than a lint warning.
