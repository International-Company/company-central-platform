import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { RoleDto } from '@/types/platform';

export async function GET(request: Request) {
  // No paging: the role catalogue is a short, human-maintained list, and
  // paginating twelve rows would add a control nobody needs. Inactive ones are
  // asked for by the screen that brings them back — a role that vanishes when
  // deactivated cannot be reactivated.
  const query = forwardQuery(request.url, ['includeInactive']);

  return await relay(
    await callPlatform<RoleDto[]>({ path: `/api/v1/roles${query}` }),
  );
}

export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<RoleDto>({
      path: '/api/v1/roles',
      method: 'POST',
      body,
    }),
  );
}
