import { NextResponse } from 'next/server';
import { z } from 'zod';
import { callPlatform } from '@/lib/platform-client';
import { writeSession } from '@/lib/session';
import type { AuthenticationResultDto } from '@/types/platform';

/**
 * Signing in with a passkey, through the BFF.
 *
 * The same shape as the password sign-in beside it, and for the same reason:
 * the tokens the Platform returns **stay on this server**, in a cookie the
 * browser cannot read. What the page gets back is where to go next.
 *
 * The body is everything the authenticator produced, relayed unchanged. None of
 * it is a secret — it is a signature over a challenge, and it is worthless to
 * anybody who intercepts it, because the challenge is spent the moment the
 * Platform sees it.
 */

const SignInWithPasskeyRequest = z.object({
  credentialId: z.string().min(1).max(512),
  clientDataJson: z.string().min(1).max(4096),
  authenticatorData: z.string().min(1).max(4096),
  signature: z.string().min(1).max(4096),
  userHandle: z.string().max(512).nullish(),
});

export async function POST(request: Request) {
  const parsed = SignInWithPasskeyRequest.safeParse(await request.json().catch(() => null));

  if (!parsed.success) {
    return NextResponse.json({ code: 'BFF.INVALID_REQUEST' }, { status: 422 });
  }

  const result = await callPlatform<AuthenticationResultDto>({
    path: '/api/v1/auth/passkey',
    method: 'POST',
    body: {
      ...parsed.data,
      userHandle: parsed.data.userHandle ?? null,
    },

    // No session yet, and attaching a stale one would make signing in behave
    // differently depending on what came before it.
    authenticated: false,
  });

  if (!result.data) {
    // Passed through unchanged, including the Platform's uniform refusal.
    // Elaborating here would tell an attacker which credential identifiers
    // exist, which is the one thing they could learn from this endpoint.
    return NextResponse.json(
      { code: result.problem?.code ?? 'PLATFORM.ERROR', correlationId: result.correlationId },
      { status: result.status },
    );
  }

  await writeSession({
    accessToken: result.data.accessToken,
    refreshToken: result.data.refreshToken,
    // Widened, because the contract types an int32 as a number or a string and
    // the generated type says so. The hand-written interface next door does
    // not, which is how `expiresIn` was once read as undefined and the session
    // expiry computed to NaN.
    expiresAt: Date.now() + Number(result.data.expiresInSeconds) * 1000,
  });

  return NextResponse.json({
    requiresMfa: false,
    mustChangePassword: result.data.user.mustChangePassword,
  });
}
