import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { RegisteredApplicationDto } from '@/types/platform';

/** Every system registered to call the Platform. */
export async function GET() {
  return await relay(
    await callPlatform<RegisteredApplicationDto[]>({ path: '/api/v1/applications' }),
  );
}

export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<RegisteredApplicationDto>({
      path: '/api/v1/applications',
      method: 'POST',
      body,
    }),
  );
}
