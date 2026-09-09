import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

export async function DELETE(
  _request: Request,
  { params }: { params: Promise<{ id: string; assignmentId: string }> },
) {
  const { id, assignmentId } = await params;

  return await relay(
    await callPlatform({
      path:
        `/api/v1/users/${encodeURIComponent(id)}` +
        `/roles/${encodeURIComponent(assignmentId)}`,
      method: 'DELETE',
    }),
  );
}
