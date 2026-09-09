import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { ApplicationRoleDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

export async function GET(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<ApplicationRoleDto[]>({
      path: `/api/v1/applications/${id}/roles`,
    }),
  );
}

export async function POST(request: Request, { params }: Params) {
  const { id } = await params;
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<null>({
      path: `/api/v1/applications/${id}/roles`,
      method: 'POST',
      body,
    }),
  );
}
