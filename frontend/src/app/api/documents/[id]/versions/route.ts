import { NextResponse } from 'next/server';
import { callPlatformRaw } from '@/lib/platform-client';

interface Params {
  params: Promise<{ id: string }>;
}

/** A replacement for what is already there. The old version stays. */
export async function POST(request: Request, { params }: Params) {
  const { id } = await params;
  const contentType = request.headers.get('content-type');

  if (!contentType?.startsWith('multipart/form-data')) {
    return NextResponse.json({ code: 'DOCUMENTS.NOT_MULTIPART' }, { status: 400 });
  }

  const { response, sessionExpired } = await callPlatformRaw({
    path: `/api/v1/documents/${id}/versions`,
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
