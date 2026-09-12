import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { WebhookSubscriptionDto } from '@/types/platform';

/** Who has asked to be told when something happens. */
export async function GET() {
  return await relay(
    await callPlatform<WebhookSubscriptionDto[]>({
      path: '/api/v1/integrations/subscriptions',
    }),
  );
}

/**
 * Registers a subscription.
 *
 * The body carries the *name* of the signing secret, never its value. There is
 * no field in the Platform that could hold one.
 */
export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<WebhookSubscriptionDto>({
      path: '/api/v1/integrations/subscriptions',
      method: 'POST',
      body,
    }),
  );
}
