import { NextResponse } from 'next/server';
import { callPlatform } from '@/lib/platform-client';
import { clearSession } from '@/lib/session';

/**
 * Sign-out.
 *
 * Tells the Platform first, so the session is revoked server-side and the
 * refresh token stops working everywhere — then drops the cookie. Clearing the
 * cookie alone would only make *this browser* forget, leaving a live refresh
 * token in the Platform for anyone who had captured it.
 */
export async function POST(request: Request) {
  // Best effort. If the Platform cannot be reached the local session is still
  // cleared: leaving the user signed in because the network failed would be
  // the wrong answer to a request to sign out.
  await callPlatform({ path: '/api/v1/auth/logout', method: 'POST' });

  await clearSession();

  // Back to sign-in, keeping the locale the user was reading in.
  const locale = new URL(request.url).searchParams.get('locale') ?? 'ar';

  return NextResponse.redirect(new URL(`/${locale}/login`, request.url), {
    // 303, so the browser follows with GET rather than repeating this POST.
    status: 303,
  });
}
