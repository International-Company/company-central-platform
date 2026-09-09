import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

interface Params {
  params: Promise<{ id: string; assignmentId: string }>;
}

export async function DELETE(_request: Request, { params }: Params) {
  const { id, assignmentId } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/applications/${id}/roles/${assignmentId}`,
      method: 'DELETE',
    }),
  );
}
