import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { PositionDto } from '@/types/platform';

export async function PUT(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<PositionDto>({
      path: `/api/v1/organization/positions/${encodeURIComponent(id)}/title`,
      method: 'PUT',
      body,
    }),
  );
}
