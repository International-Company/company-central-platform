import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { WorkflowInstanceDto } from '@/types/platform';

/** One approval with everything that happened to it, in order. */
export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;

  return await relay(
    await callPlatform<WorkflowInstanceDto>({
      path: `/api/v1/workflow/instances/${encodeURIComponent(id)}`,
    }),
  );
}
