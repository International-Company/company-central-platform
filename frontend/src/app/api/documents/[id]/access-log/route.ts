import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { DocumentAccessLogDto, PagedResult } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

/** Who opened this document, and who tried and could not. */
export async function GET(request: Request, { params }: Params) {
  const { id } = await params;
  const query = forwardQuery(request.url, ['page', 'pageSize']);

  return await relay(
    await callPlatform<PagedResult<DocumentAccessLogDto>>({
      path: `/api/v1/documents/${id}/access-log${query}`,
    }),
  );
}
