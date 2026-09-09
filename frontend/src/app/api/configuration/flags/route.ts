import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { FeatureFlagDto } from '@/types/platform';

export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['applicationCode']);

  return await relay(
    await callPlatform<FeatureFlagDto[]>({
      path: `/api/v1/configuration/flags${query}`,
    }),
  );
}
