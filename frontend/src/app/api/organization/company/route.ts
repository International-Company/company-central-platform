import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { CompanyDto } from '@/types/platform';

/**
 * The company the Platform serves — or nothing, before it is set up.
 *
 * A null body is a real answer here, not a failure. Everything in the
 * organization module hangs off the company, so the screen that reads this is
 * the one that offers to create it.
 */
export async function GET() {
  return await relay(
    await callPlatform<CompanyDto | null>({
      path: '/api/v1/organization/company',
    }),
  );
}

export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<CompanyDto>({
      path: '/api/v1/organization/company',
      method: 'POST',
      body,
    }),
  );
}
