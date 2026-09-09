import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { IntegrationHealthDto } from '@/types/platform';

/** How each provider has been behaving, from its own recent calls. */
export async function GET() {
  return await relay(
    await callPlatform<IntegrationHealthDto[]>({
      path: '/api/v1/integrations/providers/health',
    }),
  );
}
