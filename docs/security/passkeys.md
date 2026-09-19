# Signing in with a fingerprint

Reference: [ARCHITECTURE.md §13](../../ARCHITECTURE.md) (Authentication).

A passkey is a key pair. The device keeps the private half and never gives it
up; the Platform stores the public half. Signing in is the device proving it
still has the private key, and it will only do that after checking the person
holding it — by fingerprint, by face, or by the device's own passcode.

## What the Platform never receives

**No fingerprint, no face, no biometric of any kind.** The sensor unlocks a key
that is already on the device. The Platform learns one thing: that the
authenticator says it verified a human before signing. That is the whole of the
biometric involvement, and it is worth being precise about it when somebody
asks whether the company is storing their fingerprint. It is not. Nothing here
could store one.

A database taken whole yields no passkey anybody can sign in with. There is no
shared secret anywhere in this design.

## Why it is stronger than a password, not merely quicker

- **Phishing stops working.** The browser binds the credential to this
  Platform's domain. A convincing copy of the sign-in page on another domain
  cannot ask for it, and the person cannot be talked into handing it over,
  because there is nothing to hand over.
- **Nothing is reused.** A password typed here is a password that may be typed
  elsewhere. A passkey exists for this Platform only.
- **Nothing is breached.** The public key is public. Losing the database loses
  nothing.
- **It is two factors in one gesture.** The key is in hardware the person holds
  and the device verified the person. The Platform refuses to store a
  credential from an authenticator that did not verify — see below.

## What it does not replace

**Step-up is still the authenticator app.** Granting a role, resetting somebody
else's second factor and the other privileged actions ask for a code from an
authenticator app, exactly as before. A passkey signs you in; it does not
currently elevate a session. Making it do so is a reasonable next step and a
deliberate separate decision.

**The password does not go away.** It is how a person signs in on a device
without a sensor, and it is what authorises adding a passkey in the first place.

## The two ceremonies

### Adding a passkey

1. The person enters their **current password** on the security screen.
2. The Platform checks it, issues a random 32-byte challenge, stores it against
   their account with a five-minute life, and returns the creation options.
3. The browser asks the device to make a key. The device verifies the person.
4. The Platform spends the challenge, verifies the response, and stores the
   public key.

**The password is not ceremony.** A passkey is a new way into the account that
survives a password change, so adding one is exactly what somebody holding a
stolen session would do to keep their way in. Asking for the password means a
stolen session alone is not enough.

**A second factor is deliberately not demanded on top of it.** Requiring step-up
would mean only people who already have an authenticator app could ever set up a
fingerprint, which is the wrong way round: the passkey is the stronger
credential of the two.

### Signing in

1. The browser asks the Platform for a challenge. **It sends no username.**
2. The device finds a passkey for this Platform, verifies the person, and signs.
3. The Platform spends the challenge, finds the credential, verifies the
   signature, and starts a session exactly as a password sign-in does.

**No username is asked for, and that is a security property rather than a
convenience.** An endpoint that took a username and answered differently
depending on whether that account had a passkey would tell an anonymous caller
which accounts exist. The credential is discoverable — the device holds the
account identifier alongside the key — so the Platform learns whose it is from
the answer.

## What is checked, and why each one matters

Every one of these, left out, leaves a sign-in that appears to work and proves
nothing:

| Check | What it prevents |
|---|---|
| The challenge is one **this Platform issued** | Replay. Comparing the client's value against itself is the attacker choosing the question. |
| The challenge is **spent before verification** | A captured exchange being retried, including by the person who sent it. |
| The **origin** is one of ours | A copy of the sign-in page on another domain collecting usable assertions. |
| The **relying-party hash** inside the authenticator data is ours | The same, asserted by the device rather than by the browser. Two parties, checked separately. |
| The **ceremony type** matches | A signature collected while registering being replayed as a sign-in. |
| **User presence** is set | A signature made with nobody there. |
| **User verification** is set | A credential that proves possession only being treated as two factors. |
| The **signature** verifies against the stored key | Everything. |
| The **counter** has advanced | A cloned authenticator, after the fact. |

Attestation is neither asked for nor verified. It identifies the model of
device; an enterprise restricting which hardware may be used would need it, and
this Platform does not. Asking for it would collect an identifier that lets
devices be told apart, for a question nobody here is asking.

## Refusals

A failed passkey sign-in is answered uniformly — the same message for an unknown
credential, a spent challenge, a bad signature and an account that may not sign
in — with one exception: **the device not having verified the person** is said
plainly, because it is the only refusal the person can act on.

**Failures do not count towards lockout.** Lockout exists to stop guessing, and
a passkey cannot be guessed. If failures counted, somebody who had once seen a
credential identifier could lock a person out of their own account from across
the internet. Every attempt is still written to login history.

## Configuration

Not secret, and load-bearing. A mismatch fails in the browser with a message the
server never sees, so it is checked at startup and the Platform refuses to start
with every problem named.

| Setting | Meaning |
|---|---|
| `Identity:WebAuthn:RelyingPartyId` | The domain, no scheme and no port. **Baked into every passkey and unchangeable**: moving the Platform to another domain means everybody enrols again. |
| `Identity:WebAuthn:Origins` | The full origins the portal is served from, scheme and port included. Matched exactly; no wildcards. |
| `Identity:WebAuthn:ChallengeLifetime` | Five minutes by default. |
| `Identity:WebAuthn:Enabled` | A switch, so this can be turned off without a deployment. |

As environment variables, for a deployment at `platform.example.com`:

```
CCP_Identity__WebAuthn__RelyingPartyId=platform.example.com
CCP_Identity__WebAuthn__Origins__0=https://platform.example.com
```

The defaults are `localhost` and `http://localhost:3000`, which is what
development and the end-to-end suite use. Browsers treat `localhost` as a secure
origin; **everywhere else must be HTTPS**, and startup refuses anything that is
not.

## How it is tested

- **The verifier is checked against real vectors.** A registration and a
  sign-in captured from Chrome's own WebAuthn implementation
  (`frontend/scripts/capture-webauthn-vectors.mjs`), because a fixture written
  by the same understanding that wrote the verifier proves only that the
  understanding is self-consistent.
- **Most of those tests are refusals**, one per row of the table above. A
  verifier that accepts a genuine assertion and also accepts a tampered one is
  worse than none, because it looks like security.
- **The whole chain runs in a browser.** An end-to-end test registers a device
  with Chrome's virtual authenticator, signs out, and signs back in with it —
  and a second one proves an authenticator that cannot verify a person is
  refused.
