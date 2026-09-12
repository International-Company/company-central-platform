import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { PagedResult, WebhookDeliveryDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

const AllowedQuery = ['page', 'pageSize'] as const;

/** What happened to the events this subscription was meant to receive. */
export async function GET(request: Request, { params }: Params) {
  const { id } = await params;
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<PagedResult<WebhookDeliveryDto>>({
      path: `/api/v1/integrations/subscriptions/${id}/deliveries${query}`,
    }),
  );
}
