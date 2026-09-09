import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { DocumentDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

export async function GET(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<DocumentDto>({ path: `/api/v1/documents/${id}` }),
  );
}

export async function PUT(request: Request, { params }: Params) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<DocumentDto>({
      path: `/api/v1/documents/${id}`,
      method: 'PUT',
      body,
    }),
  );
}

/** Marks it for deletion. The content survives the grace period. */
export async function DELETE(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<null>({ path: `/api/v1/documents/${id}`, method: 'DELETE' }),
  );
}
