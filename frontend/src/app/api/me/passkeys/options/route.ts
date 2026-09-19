import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { PasskeyRegistrationOptionsDto } from '@/types/platform';

/**
 * The challenge for adding a passkey, issued only once the password has been
 * given again.
 *
 * The password is in the body and goes no further than the Platform, which
 * checks it. A passkey is a new way into the account that outlives a password
 * change, so adding one is exactly what somebody holding a stolen session would
 * do to keep their way in.
 */
export async function POST(request: Request) {
  return await relay(
    await callPlatform<PasskeyRegistrationOptionsDto>({
      path: '/api/v1/me/passkeys/options',
      method: 'POST',
      body: await request.json(),
    }),
  );
}
