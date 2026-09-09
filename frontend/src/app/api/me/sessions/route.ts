import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { SessionDto } from '@/types/platform';

/**
 * Where this account is currently signed in.
 *
 * Carries no token and no token hash — only enough to recognise a session. The
 * value of showing it is that a person who does not recognise a device knows
 * to change their password, which ends every session but the one they are
 * using.
 */
export async function GET() {
  return await relay(
    await callPlatform<SessionDto[]>({ path: '/api/v1/me/sessions' }),
  );
}
