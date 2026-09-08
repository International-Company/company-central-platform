import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { AuditEventDto, PagedResult } from '@/types/platform';

/**
 * The audit search parameters.
 *
 * `from` and `to` are required by the Platform and deliberately not defaulted
 * here: a caller who omitted the dates would otherwise believe they had searched
 * everything, which in an investigation is worse than an error.
 */
const AllowedQuery = [
  'from',
  'to',
  'application',
  'module',
  'action',
  'actorUserId',
  'resourceType',
  'resourceId',
  'result',
  'page',
  'pageSize',
] as const;

export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return relay(
    await callPlatform<PagedResult<AuditEventDto>>({
      path: `/api/v1/audit/events${query}`,
    }),
  );
}
