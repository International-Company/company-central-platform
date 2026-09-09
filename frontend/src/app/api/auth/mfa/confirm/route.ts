import { NextResponse } from 'next/server';
import { z } from 'zod';
import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { RecoveryCodesDto } from '@/types/platform';

const ConfirmRequest = z.object({ code: z.string().min(1).max(32) });

/**
 * Completes enrolment by proving the app was set up correctly.
 *
 * The response carries the recovery codes, which are stored hashed and are
 * therefore readable exactly once. A client that does not put them in front of
 * the user has lost them for good.
 */
export async function POST(request: Request) {
  const parsed = ConfirmRequest.safeParse(await request.json().catch(() => null));

  if (!parsed.success) {
    return NextResponse.json({ code: 'BFF.INVALID_REQUEST' }, { status: 422 });
  }

  return await relay(
    await callPlatform<RecoveryCodesDto>({
      path: '/api/v1/me/mfa/confirm',
      method: 'POST',
      body: parsed.data,
    }),
  );
}
