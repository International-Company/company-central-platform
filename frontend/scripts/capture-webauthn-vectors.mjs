/*
 * Captures a real registration and a real sign-in from Chrome, and writes them
 * where the Platform's tests can read them.
 *
 *     node scripts/capture-webauthn-vectors.mjs
 *
 * **Why a captured vector rather than one this repository generates.** The
 * verifier's job is to accept what browsers and authenticators actually
 * produce, and a fixture built by the same understanding that wrote the
 * verifier proves only that the understanding is self-consistent. CBOR map
 * ordering, the DER wrapping of an ECDSA signature, where the attested
 * credential data sits inside the authenticator data: each of these is a place
 * to be confidently wrong in both files at once.
 *
 * Chrome's virtual authenticator is the real implementation, driven through the
 * DevTools protocol. Nothing here is a secret: a public key, a signature over a
 * challenge, and a credential identifier, from a key that exists for a few
 * seconds inside a headless browser.
 *
 * Served over plain HTTP on localhost, which browsers treat as a secure origin
 * so that development is possible at all.
 */

import { createServer } from 'node:http';
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

const port = 5199;
const origin = `http://localhost:${port}`;
const relyingPartyId = 'localhost';

const server = createServer((_request, response) => {
  response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  response.end('<!doctype html><html><head><meta charset="utf-8"><title>vectors</title></head><body></body></html>');
});

await new Promise((resolve) => server.listen(port, resolve));

const browser = await chromium.launch();
const context = await browser.newContext();
const page = await context.newPage();

await page.goto(origin);

const session = await context.newCDPSession(page);

await session.send('WebAuthn.enable');

const { authenticatorId } = await session.send('WebAuthn.addVirtualAuthenticator', {
  options: {
    protocol: 'ctap2',
    ctap2Version: 'ctap2_1',

    // A fingerprint sensor on the device itself, rather than a key on a
    // keyring: the thing this feature is for.
    transport: 'internal',
    hasResidentKey: true,
    hasUserVerification: true,

    // The person touches the sensor and it recognises them. Simulated, because
    // there is no finger in a headless browser.
    isUserVerified: true,
    automaticPresenceSimulation: true,
  },
});

console.log('virtual authenticator:', authenticatorId);

const vectors = await page.evaluate(async ({ relyingPartyId: rpId }) => {
  const encode = (buffer) => {
    const bytes = new Uint8Array(buffer);
    let binary = '';

    for (const byte of bytes) {
      binary += String.fromCharCode(byte);
    }

    return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
  };

  const decode = (value) => {
    const binary = atob(value.replaceAll('-', '+').replaceAll('_', '/'));
    const bytes = new Uint8Array(binary.length);

    for (let index = 0; index < binary.length; index += 1) {
      bytes[index] = binary.charCodeAt(index);
    }

    return bytes;
  };

  // A fixed user id and two fixed challenges, so the committed fixture is
  // stable and a test can name the values it expects.
  const userId = decode('EjRWeJCrze8SNFZ4kKvN7w');
  const registrationChallenge = 'Zm9yLXJlZ2lzdHJhdGlvbi10ZXN0LXZlY3Rvcg';
  const authenticationChallenge = 'Zm9yLWF1dGhlbnRpY2F0aW9uLXRlc3QtdmVjdG9y';

  const created = await navigator.credentials.create({
    publicKey: {
      challenge: decode(registrationChallenge),
      rp: { id: rpId, name: 'Company Central Platform' },
      user: { id: userId, name: 'e2e-admin', displayName: 'End To End' },
      pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
      authenticatorSelection: {
        residentKey: 'required',
        userVerification: 'required',
      },
      attestation: 'none',
    },
  });

  const asserted = await navigator.credentials.get({
    publicKey: {
      challenge: decode(authenticationChallenge),
      rpId,
      userVerification: 'required',
    },
  });

  return {
    relyingPartyId: rpId,
    origin: location.origin,
    userHandle: encode(userId),

    registration: {
      challenge: registrationChallenge,
      credentialId: created.id,
      clientDataJson: encode(created.response.clientDataJSON),
      attestationObject: encode(created.response.attestationObject),
    },

    authentication: {
      challenge: authenticationChallenge,
      credentialId: asserted.id,
      clientDataJson: encode(asserted.response.clientDataJSON),
      authenticatorData: encode(asserted.response.authenticatorData),
      signature: encode(asserted.response.signature),
      userHandle: asserted.response.userHandle ? encode(asserted.response.userHandle) : null,
    },
  };
}, { relyingPartyId });

const target = join(root, 'tests', 'Modules', 'CCP.Modules.Identity.UnitTests', 'webauthn-vectors.json');

writeFileSync(target, `${JSON.stringify(vectors, null, 2)}\n`, 'utf8');

console.log('registration credential:', vectors.registration.credentialId);
console.log('assertion credential   :', vectors.authentication.credentialId);
console.log('user handle returned   :', vectors.authentication.userHandle);
console.log('written to             :', target);

await context.close();
await browser.close();
server.close();
