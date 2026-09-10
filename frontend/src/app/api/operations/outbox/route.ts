import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { OutboxDepthDto } from '@/types/platform';

/** How far behind event delivery is. */
export async function GET() {
  return await relay(
    await callPlatform<OutboxDepthDto>({ path: '/api/v1/platform/outbox' }),
  );
}
