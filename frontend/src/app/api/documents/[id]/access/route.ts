import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { DocumentAccessRuleDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

export async function GET(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<DocumentAccessRuleDto[]>({
      path: `/api/v1/documents/${id}/access`,
    }),
  );
}

export async function PUT(request: Request, { params }: Params) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<DocumentAccessRuleDto>({
      path: `/api/v1/documents/${id}/access`,
      method: 'PUT',
      body,
    }),
  );
}
