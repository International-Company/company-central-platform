import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { WorkflowInstanceDto } from '@/types/platform';

/**
 * Approving, rejecting, returning, delegating or commenting.
 *
 * The actor is never in the body — the Platform takes it from the token. An
 * endpoint that let a caller name themselves would let anybody approve in
 * somebody else's name, and the whole trail would then attribute a decision to
 * the wrong person.
 */
export async function POST(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<WorkflowInstanceDto>({
      path: `/api/v1/me/tasks/${encodeURIComponent(id)}/actions`,
      method: 'POST',
      body,
    }),
  );
}
