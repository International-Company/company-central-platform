import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { PagedResult, UserDto } from '@/types/platform';

/** The parameters the Users screen offers. Anything else is dropped. */
const AllowedQuery = ['page', 'pageSize', 'search', 'status', 'sort'] as const;

export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<PagedResult<UserDto>>({ path: `/api/v1/users${query}` }),
  );
}

export async function POST(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  // Passed through without reshaping. The Platform validates it and returns
  // field-level errors the form can attach to inputs; validating here as well
  // would create a second set of rules to keep in step with the first.
  return await relay(
    await callPlatform<UserDto>({
      path: '/api/v1/users',
      method: 'POST',
      body,
    }),
  );
}
