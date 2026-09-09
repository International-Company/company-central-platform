import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { FeatureFlagDto } from '@/types/platform';

interface Params {
  params: Promise<{ key: string }>;
}

export async function PUT(request: Request, { params }: Params) {
  const { key } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<FeatureFlagDto>({
      path: `/api/v1/configuration/flags/${encodeURIComponent(key)}`,
      method: 'PUT',
      body,
    }),
  );
}
