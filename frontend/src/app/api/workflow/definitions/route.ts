import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { WorkflowDefinitionDto } from '@/types/platform';

export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['applicationCode', 'includeRetired']);

  return await relay(
    await callPlatform<WorkflowDefinitionDto[]>({
      path: `/api/v1/workflow/definitions${query}`,
    }),
  );
}
