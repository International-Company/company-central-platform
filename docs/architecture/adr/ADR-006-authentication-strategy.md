# ADR-006: Authentication Strategy

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture, Security |

## Context

The Platform is the single identity for the whole company. Every business system delegates authentication to it. Two very different kinds of caller must be authenticated: humans in a browser, and business applications acting machine-to-machine.

The frontend is Next.js, which has a server runtime — this matters, because it makes it possible to keep tokens out of the browser entirely.

## Problem

How do humans and applications authenticate, and where do tokens live?

## Options

### Option A — Cookie sessions with server-side state
*Pros:* simple; revocation is immediate; nothing for JavaScript to steal.
*Cons:* awkward for machine-to-machine callers; requires shared session state if instances scale out; CSRF must be handled.

### Option B — JWT bearer tokens held in the browser
The SPA stores an access token and calls the API directly.

*Pros:* stateless; simple to reason about; works identically for humans and machines.
*Cons:* **any XSS becomes token theft.** `localStorage` is readable by any script on the page. Tokens cannot be revoked before expiry. This is the most common pattern and the most commonly regretted one.

### Option C — BFF (Backend-for-Frontend) for browsers, OAuth 2.0 client credentials for applications
Browsers get an httpOnly cookie; the Next.js server holds the tokens and proxies to the API. Applications authenticate with client credentials and hold bearer tokens themselves.

*Pros:* no token is ever reachable by browser JavaScript, which removes XSS token exfiltration as a class; refresh tokens can be rotated and revoked server-side; machine callers get the standard, appropriate flow.
*Cons:* an extra network hop for browser requests; the BFF must be implemented correctly, including CSRF protection.

### Option D — A full identity server (Duende IdentityServer or similar)
*Pros:* complete OAuth2/OIDC; battle-tested; supports every flow.
*Cons:* commercial licensing; substantial configuration surface; solves federation problems this Platform does not currently have. One company, one user directory, no third-party relying parties — building a general-purpose authorization server for that is P1's textbook violation. It remains the right answer if external federation becomes a requirement.

## Decision

**Option C.**

**Humans:** the browser authenticates against the Next.js BFF, which calls the Platform API and stores the resulting tokens server-side, returning an `httpOnly`, `Secure`, `SameSite=Strict` session cookie. **No token is ever written to `localStorage` or `sessionStorage`.** CSRF protection is applied to cookie-authenticated routes.

**Applications:** OAuth 2.0 client credentials. The client secret is stored hashed and shown once at issuance. Machine tokens carry the application identity and, when acting for a user, the user identity as well — so audit always records which application acted on whose behalf.

**Tokens:** access tokens are JWTs signed RS256, 15-minute lifetime, minimal claims, validated by business applications against a published JWKS endpoint without calling the Platform. Refresh tokens are opaque 256-bit random values, stored **hashed**, 14-day sliding lifetime, bound to a session and device, with **rotation and reuse detection** — presenting an already-used refresh token revokes the entire session family and raises a security event.

Full SSO federation (SAML, OIDC relying party) is designed for as an extension point in the Identity module but not implemented until required (Q5).

## Reason

Option B is rejected on a single decisive point: it makes every XSS vulnerability a full account compromise. Since the frontend already has a server runtime, the BFF costs one network hop and eliminates that entire risk class. That trade is not close.

Asymmetric signing matters because business applications must validate tokens locally — if every request from every business system required a call to the Platform to validate a token, the Platform becomes a synchronous bottleneck for the whole company.

Refresh token rotation with reuse detection is what turns a stolen refresh token from a long-lived compromise into a detected incident. It is the single highest-value control in this design.

## Consequences

**Positive.** XSS cannot exfiltrate a session. Refresh tokens are revocable and their theft is detectable. Business applications validate tokens without a round trip. Short access-token lifetime limits the window of any leak. Sessions and devices are visible and revocable by users and administrators.

**Negative.** One extra hop for every browser request. The BFF must be implemented carefully, and CSRF protection is now required. Access tokens remain valid for up to 15 minutes after a revocation — accepted, and the reason the lifetime is short; operations requiring immediate revocation check authorization server-side, which is already the case. Key rotation is an operational procedure that must actually be exercised.

**Follow-up actions.**
- Implement rotation with reuse detection in Phase 2, with an explicit test that a replayed token revokes the family.
- Publish JWKS and document key rotation; rotate once in staging during Phase 2, not first at go-live.
- MFA integrates into this flow in Phase 5.
- Verify in Phase 7 that no token appears in browser storage.

## Status
Accepted.

