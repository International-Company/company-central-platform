import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { CompanyDto } from '@/types/platform';

export async function PUT(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<CompanyDto>({
      path: '/api/v1/organization/company/name',
      method: 'PUT',
      body,
    }),
  );
}
