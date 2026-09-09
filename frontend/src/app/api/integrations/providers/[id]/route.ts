import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { IntegrationProviderDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

/**
 * Sets resilience, redaction and the credential reference.
 *
 * The reference is the *name* of a secret. There is no field in this request or
 * in any response that carries a value — the Platform stores references and the
 * value lives in the secret store.
 */
export async function PUT(request: Request, { params }: Params) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<IntegrationProviderDto>({
      path: `/api/v1/integrations/providers/${id}`,
      method: 'PUT',
      body,
    }),
  );
}
