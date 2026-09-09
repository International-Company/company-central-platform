import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { PositionDto } from '@/types/platform';

export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['includeInactive']);

  return await relay(
    await callPlatform<PositionDto[]>({
      path: `/api/v1/organization/positions${query}`,
    }),
  );
}

export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<PositionDto>({
      path: '/api/v1/organization/positions',
      method: 'POST',
      body,
    }),
  );
}
