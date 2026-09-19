import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

/**
 * Removes one of this person's passkeys.
 *
 * Always available, and deliberately not behind a second factor: this is what
 * somebody does the moment a laptop is lost, and a control you cannot reach in
 * a hurry is a control you do not have.
 */
export async function DELETE(
  _request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;

  return await relay(
    await callPlatform({
      path: `/api/v1/me/passkeys/${encodeURIComponent(id)}`,
      method: 'DELETE',
    }),
  );
}
