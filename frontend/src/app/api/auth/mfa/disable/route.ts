import { NextResponse } from 'next/server';
import { z } from 'zod';
import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

const DisableRequest = z.object({ code: z.string().min(1).max(32) });

/**
 * Turns the second factor off, and requires a current code to do it.
 *
 * The code is the point: without it, anyone holding a stolen session could
 * remove the protection that would have stopped them using it.
 */
export async function POST(request: Request) {
  const parsed = DisableRequest.safeParse(await request.json().catch(() => null));

  if (!parsed.success) {
    return NextResponse.json({ code: 'BFF.INVALID_REQUEST' }, { status: 422 });
  }

  return await relay(
    await callPlatform({
      path: '/api/v1/me/mfa/disable',
      method: 'POST',
      body: parsed.data,
    }),
  );
}
