import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { PagedResult, WorkflowInstanceDto } from '@/types/platform';

const AllowedQuery = [
  'applicationCode',
  'resourceType',
  'resourceId',
  'status',
  'page',
  'pageSize',
] as const;

export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<PagedResult<WorkflowInstanceDto>>({
      path: `/api/v1/workflow/instances${query}`,
    }),
  );
}
