import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { MyPermissionsDto } from '@/types/platform';

export async function GET() {
  return await relay(
    await callPlatform<MyPermissionsDto>({ path: '/api/v1/me/permissions' }),
  );
}
