import { NextResponse } from 'next/server';
import { z } from 'zod';
import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/**
 * Enabling, disabling and unlocking an account.
 *
 * One handler for three actions because they are one decision — what state
 * should this account be in — and the Platform enforces the same permission and
 * the same step-up on all three.
 *
 * **Unlock is the one that had no interface at all.** The endpoint has existed
 * since Phase 2, and until now the only way to release an account the
 * progressive lockout had caught was a hand-written HTTP request. That is a poor
 * position for a company: lockouts are routine, they happen to people who
 * mistyped a password, and the person who can fix it should not have to be an
 * engineer.
 */
const StatusRequest = z.object({
  action: z.enum(['enable', 'disable', 'unlock']),
});

export async function POST(
  request: Request,
  context: { params: Promise<{ id: string }> },
) {
  const { id } = await context.params;
  const parsed = StatusRequest.safeParse(await request.json().catch(() => null));

  if (!parsed.success) {
    return NextResponse.json({ code: 'BFF.INVALID_REQUEST' }, { status: 422 });
  }

  // The action becomes a path segment, and the schema above is what makes that
  // safe: only the three literals it permits can reach here, so nothing a
  // caller writes is interpolated into the URL.
  return relay(
    await callPlatform({
      path: `/api/v1/users/${encodeURIComponent(id)}/${parsed.data.action}`,
      method: 'POST',
    }),
  );
}
