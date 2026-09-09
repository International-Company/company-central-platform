import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { DocumentLinkDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

export async function GET(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<DocumentLinkDto[]>({
      path: `/api/v1/documents/${id}/links`,
    }),
  );
}

export async function POST(request: Request, { params }: Params) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<DocumentLinkDto>({
      path: `/api/v1/documents/${id}/links`,
      method: 'POST',
      body,
    }),
  );
}

export async function DELETE(request: Request, { params }: Params) {
  const { id } = await params;
  const query = forwardQuery(request.url, ['resourceType', 'resourceId']);

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/documents/${id}/links${query}`,
      method: 'DELETE',
    }),
  );
}
