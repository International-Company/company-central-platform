import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { LoginAttemptDto } from '@/types/platform';

/**
 * Recent sign-in attempts on this account, successful and failed.
 *
 * The failures are the point. Someone seeing attempts they did not make is the
 * earliest signal available that their account is being tried, and it reaches
 * them before any administrator would notice.
 */
export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['count']);

  return await relay(
    await callPlatform<LoginAttemptDto[]>({
      path: `/api/v1/me/login-history${query}`,
    }),
  );
}
