import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { ApplicationCredentialDto, IssuedCredentialDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

export async function GET(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<ApplicationCredentialDto[]>({
      path: `/api/v1/applications/${id}/credentials`,
    }),
  );
}

/**
 * Mints a secret.
 *
 * The response carries the plaintext secret exactly once. It is relayed to the
 * browser and never stored here — this route holds nothing, logs nothing, and
 * the screen shows it until the person navigates away.
 */
export async function POST(request: Request, { params }: Params) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<IssuedCredentialDto>({
      path: `/api/v1/applications/${id}/credentials`,
      method: 'POST',
      body,
    }),
  );
}
