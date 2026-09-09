import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/**
 * Marks one of the caller's own messages as read.
 *
 * The reader is never in the body — the Platform takes it from the token, and
 * the aggregate refuses a mismatch. The unread count is the only signal a
 * person has that something is waiting, so somebody else clearing it would
 * hide a message rather than merely be rude.
 */
export async function POST(
  _request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;

  return await relay(
    await callPlatform({
      path: `/api/v1/me/notifications/${encodeURIComponent(id)}/read`,
      method: 'POST',
    }),
  );
}
