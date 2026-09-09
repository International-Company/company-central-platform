import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { UserRoleDto } from '@/types/platform';

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;

  return await relay(
    await callPlatform<UserRoleDto[]>({
      path: `/api/v1/users/${encodeURIComponent(id)}/roles`,
    }),
  );
}

export async function POST(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  // Granting demands a recent second factor. The refusal comes back as 403 with
  // SECURITY.STEP_UP_REQUIRED, which the screen turns into a prompt rather than
  // into "you do not have permission" — the two have opposite remedies.
  return await relay(
    await callPlatform({
      path: `/api/v1/users/${encodeURIComponent(id)}/roles`,
      method: 'POST',
      body,
    }),
  );
}
