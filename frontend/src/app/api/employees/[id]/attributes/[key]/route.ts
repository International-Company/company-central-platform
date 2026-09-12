import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

interface Params {
  params: Promise<{ id: string; key: string }>;
}

export async function PUT(request: Request, { params }: Params) {
  const { id, key } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/organization/employees/${id}/attributes/${encodeURIComponent(key)}`,
      method: 'PUT',
      body,
    }),
  );
}

export async function DELETE(_request: Request, { params }: Params) {
  const { id, key } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/organization/employees/${id}/attributes/${encodeURIComponent(key)}`,
      method: 'DELETE',
    }),
  );
}
