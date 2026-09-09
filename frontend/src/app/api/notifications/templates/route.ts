import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { NotificationTemplateDto } from '@/types/platform';

export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['includeInactive']);

  return await relay(
    await callPlatform<NotificationTemplateDto[]>({
      path: `/api/v1/notifications/templates${query}`,
    }),
  );
}

export async function PUT(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<NotificationTemplateDto>({
      path: '/api/v1/notifications/templates',
      method: 'PUT',
      body,
    }),
  );
}
