import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

export async function POST(
  _request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/organization/units/${encodeURIComponent(id)}/deactivate`,
      method: 'POST',
    }),
  );
}
