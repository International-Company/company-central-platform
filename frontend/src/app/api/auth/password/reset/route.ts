import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/** Completing a reset with the emailed token. */
export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return relay(
    await callPlatform({
      path: '/api/v1/auth/password/reset',
      method: 'POST',
      body,
      authenticated: false,
    }),
  );
}
