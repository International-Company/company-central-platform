import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { PermissionDto } from '@/types/platform';

/** Every permission the Platform declares, for choosing what a role carries. */
export async function GET() {
  return await relay(
    await callPlatform<PermissionDto[]>({ path: '/api/v1/roles/permissions' }),
  );
}
