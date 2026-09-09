import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { PagedResult, SecurityEventDto } from '@/types/platform';

/** The parameters the security screen offers. Anything else is dropped. */
const AllowedQuery = [
  'page',
  'pageSize',
  'eventType',
  'minimumSeverity',
  'from',
  'to',
] as const;

export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<PagedResult<SecurityEventDto>>({
      path: `/api/v1/security/events${query}`,
    }),
  );
}
