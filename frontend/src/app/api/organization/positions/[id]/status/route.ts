import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

export async function POST(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform({
      path: `/api/v1/organization/positions/${encodeURIComponent(id)}/status`,
      method: 'POST',
      body,
    }),
  );
}
