import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { IntegrationProviderDto } from '@/types/platform';

/** Every external service the Platform may call. */
export async function GET() {
  return await relay(
    await callPlatform<IntegrationProviderDto[]>({
      path: '/api/v1/integrations/providers',
    }),
  );
}

export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<IntegrationProviderDto>({
      path: '/api/v1/integrations/providers',
      method: 'POST',
      body,
    }),
  );
}
