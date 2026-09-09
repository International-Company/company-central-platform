import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

export async function PUT(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<null>({
      path: '/api/v1/configuration/settings/value',
      method: 'PUT',
      body,
    }),
  );
}
