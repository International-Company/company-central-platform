import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { NotificationDto, PagedResult } from '@/types/platform';

/** The caller's own inbox. Needs no permission: it is one person's messages. */
export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['unreadOnly', 'page', 'pageSize']);

  return await relay(
    await callPlatform<PagedResult<NotificationDto>>({
      path: `/api/v1/me/notifications${query}`,
    }),
  );
}
