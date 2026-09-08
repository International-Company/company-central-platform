import { NextResponse } from 'next/server';
import { z } from 'zod';
import { callPlatform } from '@/lib/platform-client';
import { writeSession } from '@/lib/session';

/**
 * Sign-in, through the BFF.
 *
 * The browser posts here. This handler calls the Platform, and the tokens it
 * gets back **stay on this server**: they go into an `httpOnly` cookie the
 * browser cannot read and never into the response body (ADR-006 §13.2).
 *
 * The response carries only what the page needs to decide where to go next —
 * whether a second factor is required, whether the password must change. Nothing
 * a script can steal.
 */

const SignInRequest = z.object({
  username: z.string().min(1).max(64),
  password: z.string().min(1).max(256),
});

interface AuthenticationResult {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  requiresMfa?: boolean;
  mustChangePassword?: boolean;
}

export async function POST(request: Request) {
  const parsed = SignInRequest.safeParse(await request.json().catch(() => null));

  if (!parsed.success) {
    return NextResponse.json(
      { code: 'BFF.INVALID_REQUEST' },
      { status: 422 },
    );
  }

  const result = await callPlatform<AuthenticationResult>({
    path: '/api/v1/auth/login',
    method: 'POST',
    body: parsed.data,

    // No session exists yet, so nothing to attach — and attaching a stale one
    // would make a sign-in behave differently depending on what came before it.
    authenticated: false,
  });

  if (!result.data) {
    // The Platform's answer is passed through unchanged, including its uniform
    // failure for a wrong username or password. Softening or elaborating on it
    // here would undo the enumeration protection built in Phase 2: any
    // difference the browser can observe is a difference an attacker can use to
    // discover which accounts exist.
    return NextResponse.json(
      {
        code: result.problem?.code ?? 'PLATFORM.ERROR',
        correlationId: result.correlationId,
      },
      { status: result.status },
    );
  }

  await writeSession({
    accessToken: result.data.accessToken,
    refreshToken: result.data.refreshToken,
    expiresAt: Date.now() + result.data.expiresIn * 1000,
  });

  return NextResponse.json({
    requiresMfa: result.data.requiresMfa ?? false,
    mustChangePassword: result.data.mustChangePassword ?? false,
  });
}
