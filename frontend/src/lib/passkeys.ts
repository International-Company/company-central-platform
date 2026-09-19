/**
 * The browser half of signing in with a fingerprint.
 *
 * **Nothing secret passes through here.** The device holds the private key and
 * never gives it up; what crosses this file is a challenge going out and a
 * signature coming back. The fingerprint itself never leaves the sensor — not
 * to this application, not to the Platform, not anywhere. The device checks the
 * finger and then unlocks a key; all the Platform ever learns is that it did.
 *
 * Everything the browser wants is bytes and everything the Platform speaks is
 * base64url, so most of what follows is that conversion, in both directions.
 */

/** Whether this browser can do platform authentication at all. */
export function passkeysAvailable(): boolean {
  return typeof window !== 'undefined'
    && typeof window.PublicKeyCredential === 'function'
    && typeof navigator.credentials?.get === 'function';
}

/**
 * Whether this device has a fingerprint sensor, a face camera or a passcode
 * the browser can use.
 *
 * Asked before offering it. A button that opens a dialog saying "this device
 * cannot do that" is worse than no button, and this is the one question that
 * distinguishes a laptop with Windows Hello from a desktop without.
 */
export async function deviceCanVerifyUser(): Promise<boolean> {
  if (!passkeysAvailable()) {
    return false;
  }

  try {
    return await window.PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
  } catch {
    return false;
  }
}

/** The outcome of a ceremony, as the screens need to tell it apart. */
export type PasskeyOutcome =
  | { kind: 'signed-in'; mustChangePassword: boolean }
  | { kind: 'registered'; name: string }

  // The person closed the dialog, or their device declined. Not an error, and
  // showing one would be telling them off for changing their mind.
  | { kind: 'cancelled' }
  | { kind: 'failed'; code: string };

/**
 * Signs in with a passkey.
 *
 * No username is asked for and none is sent. The browser searches the device
 * for a credential belonging to this Platform, the person confirms with their
 * finger or face, and who they are comes back inside the signature.
 */
export async function signInWithPasskey(): Promise<PasskeyOutcome> {
  const options = await fetch('/api/auth/passkey/options', { method: 'POST' });

  if (!options.ok) {
    return { kind: 'failed', code: await codeOf(options) };
  }

  const challenge = (await options.json()) as {
    challenge: string;
    relyingPartyId: string;
    timeoutMilliseconds: number;
  };

  let assertion: PublicKeyCredential | null;

  try {
    assertion = (await navigator.credentials.get({
      publicKey: {
        challenge: decode(challenge.challenge),
        rpId: challenge.relyingPartyId,

        // No allowCredentials: the credential is discoverable, which is what
        // lets this ask for nothing.
        userVerification: 'required',
        timeout: Number(challenge.timeoutMilliseconds),
      },
    })) as PublicKeyCredential | null;
  } catch {
    // Abort, timeout, no matching credential: all of them arrive here, and
    // none of them is something to put an error banner up for.
    return { kind: 'cancelled' };
  }

  if (!assertion) {
    return { kind: 'cancelled' };
  }

  const response = assertion.response as AuthenticatorAssertionResponse;

  const completed = await fetch('/api/auth/passkey', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      credentialId: assertion.id,
      clientDataJson: encode(response.clientDataJSON),
      authenticatorData: encode(response.authenticatorData),
      signature: encode(response.signature),
      userHandle: response.userHandle ? encode(response.userHandle) : null,
    }),
  });

  if (!completed.ok) {
    return { kind: 'failed', code: await codeOf(completed) };
  }

  const result = (await completed.json()) as { mustChangePassword: boolean };

  return { kind: 'signed-in', mustChangePassword: result.mustChangePassword };
}

/**
 * Adds a passkey to the signed-in account.
 *
 * The password is sent once, to the Platform, which checks it before issuing
 * the challenge. After that the device does the rest.
 */
export async function registerPasskey(
  currentPassword: string,
  name: string,
): Promise<PasskeyOutcome> {
  const options = await fetch('/api/me/passkeys/options', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ currentPassword }),
  });

  if (!options.ok) {
    return { kind: 'failed', code: await codeOf(options) };
  }

  const creation = (await options.json()) as {
    challenge: string;
    relyingPartyId: string;
    relyingPartyName: string;
    userId: string;
    username: string;
    displayName: string;
    algorithms: number[];
    excludeCredentials: string[];
    timeoutMilliseconds: number;
  };

  let created: PublicKeyCredential | null;

  try {
    created = (await navigator.credentials.create({
      publicKey: {
        challenge: decode(creation.challenge),
        rp: { id: creation.relyingPartyId, name: creation.relyingPartyName },
        user: {
          id: decode(creation.userId),
          name: creation.username,
          displayName: creation.displayName,
        },
        pubKeyCredParams: creation.algorithms.map((alg) => ({ type: 'public-key', alg })),

        // Already registered on this device: the authenticator refuses rather
        // than quietly making a second key, so the person is told by their own
        // device instead of by a conflict from the server.
        excludeCredentials: creation.excludeCredentials.map((id) => ({
          type: 'public-key',
          id: decode(id),
        })),

        authenticatorSelection: {
          // The device itself, not a key on a keyring. This is the feature
          // people mean when they say fingerprint.
          authenticatorAttachment: 'platform',

          // Discoverable, which is what lets signing in ask for no username.
          residentKey: 'required',

          // The finger, the face or the passcode. Without it the credential
          // proves possession only, and the Platform refuses to store it.
          userVerification: 'required',
        },

        // Not asked for. It identifies the model of device, an enterprise
        // restricting hardware would need it, and this Platform does not.
        attestation: 'none',
        timeout: Number(creation.timeoutMilliseconds),
      },
    })) as PublicKeyCredential | null;
  } catch {
    return { kind: 'cancelled' };
  }

  if (!created) {
    return { kind: 'cancelled' };
  }

  const response = created.response as AuthenticatorAttestationResponse;

  const stored = await fetch('/api/me/passkeys', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name,
      clientDataJson: encode(response.clientDataJSON),
      attestationObject: encode(response.attestationObject),
    }),
  });

  if (!stored.ok) {
    return { kind: 'failed', code: await codeOf(stored) };
  }

  return { kind: 'registered', name };
}

/** The Platform's error code, so a screen can say something specific. */
async function codeOf(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { code?: string };

    return body.code ?? 'PLATFORM.ERROR';
  } catch {
    return 'PLATFORM.ERROR';
  }
}

// Typed over a real ArrayBuffer rather than the default, which TypeScript
// widens to include SharedArrayBuffer -- and the WebAuthn types will not take
// one of those.
function decode(base64Url: string): Uint8Array<ArrayBuffer> {
  const padded = base64Url.replaceAll('-', '+').replaceAll('_', '/');
  const binary = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));

  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}

function encode(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';

  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}
