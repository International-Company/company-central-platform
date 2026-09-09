import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { UserDto } from '@/types/platform';

export async function GET(
  _request: Request,
  context: { params: Promise<{ id: string }> },
) {
  const { id } = await context.params;

  return relay(
    await callPlatform<UserDto>({
      path: `/api/v1/users/${encodeURIComponent(id)}`,
    }),
  );
}

export async function PUT(
  request: Request,
  context: { params: Promise<{ id: string }> },
) {
  const { id } = await context.params;
  const body: unknown = await request.json().catch(() => null);

  // Passed through unreshaped. The Platform validates it and answers with
  // field-level errors the form attaches to inputs; validating here as well
  // would be a second set of rules to keep in step with the first.
  return relay(
    await callPlatform<UserDto>({
      path: `/api/v1/users/${encodeURIComponent(id)}`,
      method: 'PUT',
      body,
    }),
  );
}
