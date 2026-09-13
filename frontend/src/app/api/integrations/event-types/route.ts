import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/**
 * Every event type a subscription can name.
 *
 * Read from the event records the running Platform ships, so the form offers
 * exactly the list the Platform will check the subscription against.
 */
export async function GET() {
  return await relay(
    await callPlatform<string[]>({
      path: '/api/v1/integrations/event-types',
    }),
  );
}
