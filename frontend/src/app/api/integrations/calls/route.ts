import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { IntegrationCallDto, PagedResult } from '@/types/platform';

const AllowedQuery = ['providerCode', 'outcome', 'page', 'pageSize'] as const;

/** What the Platform sent, and what came back. */
export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<PagedResult<IntegrationCallDto>>({
      path: `/api/v1/integrations/calls${query}`,
    }),
  );
}
