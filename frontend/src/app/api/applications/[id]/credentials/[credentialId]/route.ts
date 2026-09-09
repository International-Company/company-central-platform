import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

interface Params {
  params: Promise<{ id: string; credentialId: string }>;
}

export async function DELETE(_request: Request, { params }: Params) {
  const { id, credentialId } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/applications/${id}/credentials/${credentialId}`,
      method: 'DELETE',
    }),
  );
}
