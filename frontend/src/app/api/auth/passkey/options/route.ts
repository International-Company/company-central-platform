import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { PasskeySignInOptionsDto } from '@/types/platform';

/**
 * The one-time challenge a passkey signs to sign in.
 *
 * Anonymous, and it asks for nothing. No username is sent, so this endpoint
 * cannot be used to discover which accounts exist or which of them have a
 * passkey: the browser finds the credential on the device, and the Platform
 * learns whose it is from the answer.
 */
export async function POST() {
  return await relay(
    await callPlatform<PasskeySignInOptionsDto>({
      path: '/api/v1/auth/passkey/options',
      method: 'POST',

      // Nobody is signed in. Attaching a session would be attaching whoever
      // last used this browser to a sign-in that has not happened yet.
      authenticated: false,
    }),
  );
}
