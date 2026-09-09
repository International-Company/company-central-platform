import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { RoleDto } from '@/types/platform';

/**
 * Replaces what a role grants.
 *
 * Step-up protected at the Platform, like a grant is: changing a role's
 * permissions changes what everyone holding it can do, without touching a
 * single assignment. The refusal comes back as SECURITY.STEP_UP_REQUIRED and
 * the screen turns it into a prompt.
 */
export async function PUT(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<RoleDto>({
      path: `/api/v1/roles/${encodeURIComponent(id)}/permissions`,
      method: 'PUT',
      body,
    }),
  );
}
