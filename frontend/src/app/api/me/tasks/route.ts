import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { PagedResult, WorkflowTaskDto } from '@/types/platform';

/**
 * The caller's approval inbox.
 *
 * Needs no permission: it shows one person's own tasks, and gating that would
 * mean granting the permission to everybody.
 */
export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['includeCompleted', 'page', 'pageSize']);

  return await relay(
    await callPlatform<PagedResult<WorkflowTaskDto>>({
      path: `/api/v1/me/tasks${query}`,
    }),
  );
}
