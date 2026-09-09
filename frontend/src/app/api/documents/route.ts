import { NextResponse } from 'next/server';
import { callPlatform, callPlatformRaw } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { DocumentDto, PagedResult } from '@/types/platform';

const AllowedQuery = [
  'term',
  'category',
  'organizationUnitId',
  'includeDeleted',
  'page',
  'pageSize',
] as const;

/** Documents the caller may see. */
export async function GET(request: Request) {
  const query = forwardQuery(request.url, AllowedQuery);

  return await relay(
    await callPlatform<PagedResult<DocumentDto>>({
      path: `/api/v1/documents${query}`,
    }),
  );
}

/**
 * Passes an upload through, untouched.
 *
 * **The multipart body is forwarded byte for byte, including its boundary.**
 * Reading the form here and rebuilding it would re-encode the file, and the
 * Platform decides what a file is by reading its first bytes — so a re-encoding
 * that changed anything at all would change the answer.
 */
export async function POST(request: Request) {
  const contentType = request.headers.get('content-type');

  if (!contentType?.startsWith('multipart/form-data')) {
    return NextResponse.json(
      { code: 'DOCUMENTS.NOT_MULTIPART' },
      { status: 400 },
    );
  }

  const { response, sessionExpired } = await callPlatformRaw({
    path: '/api/v1/documents',
    method: 'POST',
    body: await request.arrayBuffer(),
    contentType,
  });

  if (!response) {
    return NextResponse.json({ code: 'PLATFORM.UNREACHABLE' }, { status: 503 });
  }

  if (sessionExpired) {
    return NextResponse.json({ code: 'PLATFORM.SESSION_EXPIRED' }, { status: 401 });
  }

  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { 'Content-Type': 'application/json' },
  });
}
