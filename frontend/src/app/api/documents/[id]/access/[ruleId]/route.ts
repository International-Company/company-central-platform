import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

interface Params {
  params: Promise<{ id: string; ruleId: string }>;
}

export async function DELETE(_request: Request, { params }: Params) {
  const { id, ruleId } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/documents/${id}/access/${ruleId}`,
      method: 'DELETE',
    }),
  );
}
