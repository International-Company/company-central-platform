import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { PagedResult } from '@/types/platform';
import type { EmployeeDto } from '@/types/platform';

const AllowedQuery = ['page', 'pageSize', 'search', 'unitId', 'status'] as const;

export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return relay(
    await callPlatform<PagedResult<EmployeeDto>>({
      path: `/api/v1/employees${query}`,
    }),
  );
}
