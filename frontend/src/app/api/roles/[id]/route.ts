import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { RoleDetailDto, RoleDto } from '@/types/platform';

/**
 * One role, with the permissions it carries.
 *
 * The list endpoint reports only a count. A screen editing what a role grants
 * needs the set itself — starting from empty would turn every save into a
 * silent wipe of everything the role already held.
 */
export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;

  return await relay(
    await callPlatform<RoleDetailDto>({
      path: `/api/v1/roles/${encodeURIComponent(id)}`,
    }),
  );
}

export async function PUT(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<RoleDto>({
      path: `/api/v1/roles/${encodeURIComponent(id)}`,
      method: 'PUT',
      body,
    }),
  );
}
