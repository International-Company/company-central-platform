import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/** Links an employee record to a Platform account, or unlinks it with null. */
export async function PUT(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform({
      path: `/api/v1/organization/employees/${encodeURIComponent(id)}/user`,
      method: 'PUT',
      body,
    }),
  );
}
