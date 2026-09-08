import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/**
 * Password recovery.
 *
 * The Platform answers 202 whatever the input, so that an anonymous endpoint
 * cannot be used to discover which accounts exist. That answer is passed through
 * unchanged — distinguishing "sent" from "no such account" here would undo the
 * protection at the last step.
 */
export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return relay(
    await callPlatform({
      path: '/api/v1/auth/password/forgot',
      method: 'POST',
      body,
      authenticated: false,
    }),
  );
}
