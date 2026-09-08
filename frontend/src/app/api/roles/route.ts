import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { RoleDto } from '@/types/platform';

export async function GET() {
  // No paging: the role catalogue is a short, human-maintained list, and
  // paginating twelve rows would add a control nobody needs.
  return relay(await callPlatform<RoleDto[]>({ path: '/api/v1/roles' }));
}
