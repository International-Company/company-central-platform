import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

interface Params {
  params: Promise<{ id: string }>;
}

export async function POST(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/documents/${id}/restore`,
      method: 'POST',
    }),
  );
}
