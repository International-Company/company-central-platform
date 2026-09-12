import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';

interface Params {
  params: Promise<{ id: string }>;
}

/** Brings a suspended subscription back, and clears what suspended it. */
export async function POST(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/integrations/subscriptions/${id}/resume`,
      method: 'POST',
    }),
  );
}
