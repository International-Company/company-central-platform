import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { OrganizationUnitDto } from '@/types/platform';

export async function PUT(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<OrganizationUnitDto>({
      path: `/api/v1/organization/units/${encodeURIComponent(id)}/name`,
      method: 'PUT',
      body,
    }),
  );
}
