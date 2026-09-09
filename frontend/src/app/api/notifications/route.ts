import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { NotificationDto, PagedResult } from '@/types/platform';

const AllowedQuery = ['status', 'category', 'channel', 'page', 'pageSize'] as const;

/** Everything sent, with every delivery attempt. Where a failure surfaces. */
export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<PagedResult<NotificationDto>>({
      path: `/api/v1/notifications${query}`,
    }),
  );
}
