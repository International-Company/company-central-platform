import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { OrganizationUnitDto, OrganizationUnitTreeDto } from '@/types/platform';

/** The only parameter the structure screen offers. Anything else is dropped. */
const AllowedQuery = ['includeInactive'] as const;

export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<OrganizationUnitTreeDto[]>({
      path: `/api/v1/organization/units/tree${query}`,
    }),
  );
}

export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  // Passed through unreshaped, like every other write. The Platform owns the
  // rules — code uniqueness, which types may nest inside which — and answers
  // with the field that failed. A copy of those rules here would drift.
  return await relay(
    await callPlatform<OrganizationUnitDto>({
      path: '/api/v1/organization/units',
      method: 'POST',
      body,
    }),
  );
}
