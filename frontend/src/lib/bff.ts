import 'server-only';

import { NextResponse } from 'next/server';
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
export function relay<T>(response: PlatformResponse<T>): NextResponse {
  if (response.data !== null && response.problem === null) {
    return NextResponse.json(response.data, { status: response.status });
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
