import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { WebhookSubscriptionDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

export async function PUT(request: Request, { params }: Params) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<WebhookSubscriptionDto>({
      path: `/api/v1/integrations/subscriptions/${id}`,
      method: 'PUT',
      body,
    }),
  );
}

export async function DELETE(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/integrations/subscriptions/${id}`,
      method: 'DELETE',
    }),
  );
}
