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

  const requested = new URL(request.url).searchParams.get('locale');

  // Only the locales this application has. A redirect target taken from a query
  // string is a redirect target an attacker can write, and `/${anything}/login`
  // would happily accept a path segment nobody intended.
  const locale = requested === 'ar' || requested === 'en' ? requested : 'ar';

  // A **relative** Location, resolved by the browser against the address it is
  // actually on.
  //
  // `NextResponse.redirect` needs an absolute URL, and the only one available
  // here is built from `request.url` — which inside the container is
  // http://0.0.0.0:8080, the address the server binds to rather than the one
  // the person is browsing. Signing out sent everyone to 0.0.0.0:8080/ar/login,
  // which no browser can reach.
  //
  // The alternative is trusting X-Forwarded-Host to rebuild the public origin.
  // A relative path needs no host at all, and a header a caller can set is a
  // poor thing to build a redirect from.
  return new NextResponse(null, {
    // 303, so the browser follows with GET rather than repeating this POST.
    status: 303,
    headers: { Location: `/${locale}/login` },
  });
}
