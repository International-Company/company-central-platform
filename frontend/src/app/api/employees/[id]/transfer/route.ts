import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/**
 * Moving an employee to a different unit, position or manager.
 *
 * One call, not three edits. A transfer is a single organizational fact, and
 * splitting it would leave someone briefly reporting to their old manager in
 * their new unit — a state nobody intended and every report would show.
 */
export async function POST(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform({
      path: `/api/v1/organization/employees/${encodeURIComponent(id)}/transfer`,
      method: 'POST',
      body,
    }),
  );
}
