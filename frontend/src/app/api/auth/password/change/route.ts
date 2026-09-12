import { callPlatform, renewSession } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/**
 * Changing your own password, with the current one as proof.
 *
 * Distinct from the reset flow, which proves identity with an emailed token.
 * This one is for someone who is already signed in — including, importantly, a
 * person signing in for the first time on an account created with
 * `MustChangePassword`. Reset cannot serve that case: it needs an email, and
 * mail delivery does not exist until Phase 9.
 *
 * Authenticated, so the session cookie carries the caller. There is no user id
 * in the body: an endpoint that let a caller name whose password to change
 * would be an account takeover with extra steps.
 */
export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  const result = await callPlatform({
    path: '/api/v1/auth/password/change',
    method: 'POST',
    body,
  });

  if (result.status >= 200 && result.status < 300) {
    // The obligation to change a password travels in the access token, so the
    // one this session is holding still says it stands. Until it expires the
    // Platform refuses everything — correctly, and seconds after the person did
    // exactly what it asked of them. A refresh mints from current state and
    // closes that window.
    await renewSession();
  }

  return await relay(result);
}
