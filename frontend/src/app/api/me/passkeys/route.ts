import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { PasskeyDto } from '@/types/platform';

/** Which devices can sign in as this person. */
export async function GET() {
  return await relay(
    await callPlatform<PasskeyDto[]>({ path: '/api/v1/me/passkeys' }),
  );
}

/**
 * Stores a passkey the device has just created.
 *
 * The body is the authenticator's response and the name the person gave the
 * device. What authorises it is the challenge issued a moment ago, which the
 * Platform only issued after checking the password.
 */
export async function POST(request: Request) {
  return await relay(
    await callPlatform<PasskeyDto>({
      path: '/api/v1/me/passkeys',
      method: 'POST',
      body: await request.json(),
    }),
  );
}
