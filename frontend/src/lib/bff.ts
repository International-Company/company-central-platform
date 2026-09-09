import 'server-only';

import { NextResponse } from 'next/server';
import { clearSession } from './session';
import type { PlatformResponse } from './platform-client';

/**
 * Turns a Platform response into one for the browser.
 *
 * **Written once and used by every route handler**, because the alternative is
 * each handler inventing its own error shape and the client ending up with
 * several ways to say the same thing.
 *
 * The status and the error code pass through unchanged. A BFF that reinterprets
 * them adds a second place where "what went wrong" is decided, and the two
 * places drift: the Platform starts answering 409 and the screen keeps showing
 * a generic failure because nobody updated the translation.
 */
export async function relay<T>(response: PlatformResponse<T>): Promise<NextResponse> {
  if (response.sessionExpired) {
    // Drop the cookie the moment the Platform has finished with the session.
    // Keeping it leaves the browser showing an application whose every request
    // fails, which reads as a broken system rather than as a lapsed session —
    // and the person has no way to discover that signing in again is the fix.
    await clearSession();
  }

  // Success is decided by the status, not by whether a body came back.
  //
  // **This is what broke every action button in the application.** The Platform
  // answers 204 to a command that has nothing to return — changing a password,
  // moving a unit, granting a role, disabling an account. A 204 carries no body,
  // so `data` is null, so the old check fell through to the failure branch and
  // built an error response *with a body* at status 204 — which `NextResponse`
  // refuses to construct. The handler threw, the browser saw 500, and the screen
  // reported a failure for work the Platform had already done.
  //
  // The password change was the visible one: it succeeded, the account was
  // updated, and the person was told something went wrong.
  if (response.status >= 200 && response.status < 300 && response.problem === null) {
    return response.data === null
      ? new NextResponse(null, { status: response.status })
      : NextResponse.json(response.data, { status: response.status });
  }

  return NextResponse.json(
    {
      code: response.problem?.code ?? 'PLATFORM.ERROR',

      // Passed on so a user reporting a problem can quote something an engineer
      // can find. Never the Platform's `detail`: that is written for an
      // operator and may name internals a browser has no business seeing.
      correlationId: response.correlationId ?? response.problem?.correlationId,

      // Field errors are the exception. A screen needs them to mark the input
      // that was wrong, and they contain nothing but the caller's own input.
      errors: response.problem?.errors,
    },
    { status: response.status },
  );
}

/**
 * Copies through only the query parameters a screen is allowed to send.
 *
 * An allow-list, not a pass-through. Forwarding the whole query string would let
 * a crafted URL reach the Platform with parameters this screen never offers —
 * and the Platform's own validation should not be the only thing standing
 * between a user and a query nobody designed.
 */
export function forwardQuery(
  url: string,
  allowed: readonly string[],
): string {
  const incoming = new URL(url).searchParams;
  const forwarded = new URLSearchParams();

  for (const key of allowed) {
    const value = incoming.get(key);

    if (value !== null && value !== '') {
      forwarded.set(key, value);
    }
  }

  const query = forwarded.toString();

  return query ? `?${query}` : '';
}
