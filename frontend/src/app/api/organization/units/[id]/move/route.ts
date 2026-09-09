import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

export async function POST(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  // Moving a unit carries every descendant with it, atomically. The Platform
  // refuses a move that would make a unit its own ancestor; nothing here needs
  // to know that rule, only to relay the refusal.
  return await relay(
    await callPlatform<null>({
      path: `/api/v1/organization/units/${encodeURIComponent(id)}/move`,
      method: 'POST',
      body,
    }),
  );
}
