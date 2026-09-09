import { NextResponse } from 'next/server';
import { z } from 'zod';
import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

const VerifyRequest = z.object({
  code: z.string().min(1).max(32),
  isRecoveryCode: z.boolean().optional(),
});

/**
 * The second factor.
 *
 * The session cookie already exists at this point — the password succeeded —
 * and the Platform decides what that half-authenticated session may do. The BFF
 * does not grant or withhold anything of its own here; treating the cookie as
 * proof of full authentication would put the decision in the wrong place.
 */
export async function POST(request: Request) {
  const parsed = VerifyRequest.safeParse(await request.json().catch(() => null));

  if (!parsed.success) {
    return NextResponse.json({ code: 'BFF.INVALID_REQUEST' }, { status: 422 });
  }

  return await relay(
    await callPlatform({
      path: '/api/v1/me/mfa/verify',
      method: 'POST',
      body: parsed.data,
    }),
  );
}
