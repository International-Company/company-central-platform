import { NextResponse } from 'next/server';
import { callPlatformRaw } from '@/lib/platform-client';

interface Params {
  params: Promise<{ id: string }>;
}

/**
 * Hands the file to the browser.
 *
 * **Two shapes, and the browser sees only one of them.** When the Platform can
 * issue a short-lived storage URL it answers with a redirect, and this passes
 * the redirect on so the bytes go straight from storage to the person — never
 * through this process. When it cannot, the Platform streams the file and this
 * streams it onward.
 *
 * The response body is a stream in both cases. Buffering it here would hold a
 * whole file in memory for the length of a download, which is exactly what a
 * dozen simultaneous downloads would then do.
 */
export async function GET(request: Request, { params }: Params) {
  const { id } = await params;
  const version = new URL(request.url).searchParams.get('version');

  const { response } = await callPlatformRaw({
    path: `/api/v1/documents/${id}/content${version ? `?version=${encodeURIComponent(version)}` : ''}`,
  });

  if (!response) {
    return NextResponse.json({ code: 'PLATFORM.UNREACHABLE' }, { status: 503 });
  }

  // The client was told not to follow it, so the redirect arrives here intact.
  if (response.status >= 300 && response.status < 400) {
    const location = response.headers.get('location');

    if (location) {
      return NextResponse.redirect(location, 302);
    }
  }

  if (!response.ok) {
    return NextResponse.json(
      { code: 'DOCUMENTS.DOWNLOAD_FAILED' },
      { status: response.status },
    );
  }

  const headers = new Headers();

  copyHeader(response, headers, 'content-type');
  copyHeader(response, headers, 'content-length');
  copyHeader(response, headers, 'content-disposition');

  // Never rendered in the tab, whatever the file claims to be. An uploaded file
  // shown inline runs whatever it contains on this application's own origin,
  // with this application's cookies.
  if (!headers.has('content-disposition')) {
    headers.set('Content-Disposition', 'attachment');
  }

  return new NextResponse(response.body, { status: 200, headers });
}

function copyHeader(from: Response, to: Headers, name: string) {
  const value = from.headers.get(name);

  if (value) {
    to.set(name, value);
  }
}
