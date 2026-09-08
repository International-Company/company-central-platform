import 'server-only';

import { readSession, writeSession, type Session } from './session';

/**
 * The only place the browser-facing application talks to the Platform API.
 *
 * **Server-only.** Marked so by the `server-only` import, which turns a mistake
 * into a build error rather than a token in a browser bundle. Every feature
 * calls the Platform through a BFF route handler, and every route handler calls
 * the Platform through here.
 */

const baseUrl = process.env.PLATFORM_API_URL ?? 'http://localhost:5080';

/** The correlation header the Platform reads and echoes (ARCHITECTURE.md §11). */
const CorrelationHeader = 'X-Correlation-Id';

export interface PlatformResponse<T> {
  status: number;
  data: T | null;

  /** RFC 9457 body when the request failed. */
  problem: ProblemDetails | null;

  /** Ties this call to the Platform's logs. Shown to the user on failure. */
  correlationId: string | null;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  correlationId?: string;
  errors?: { code: string; message: string; field?: string }[];
}

export interface PlatformRequest {
  path: string;
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';
  body?: unknown;
  correlationId?: string;

  /**
   * Whether to attach the caller's access token. Off for sign-in and password
   * recovery, which are reached before a session exists.
   */
  authenticated?: boolean;
}

export async function callPlatform<T>(
  request: PlatformRequest,
): Promise<PlatformResponse<T>> {
  const session = request.authenticated === false ? null : await readSession();

  const response = await send<T>(request, session);

  // One retry, and only for the one condition a retry can fix. The access token
  // is short-lived by design, so an expired one during ordinary use is normal
  // rather than exceptional — and making the user sign in again for it would be
  // the wrong answer to a problem the refresh token exists to solve.
  if (response.status === 401 && session && request.authenticated !== false) {
    const refreshed = await refresh(session);

    if (refreshed) {
      return send<T>(request, refreshed);
    }
  }

  return response;
}

async function send<T>(
  request: PlatformRequest,
  session: Session | null,
): Promise<PlatformResponse<T>> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
  };

  if (request.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (session) {
    headers['Authorization'] = `Bearer ${session.accessToken}`;
  }

  if (request.correlationId) {
    headers[CorrelationHeader] = request.correlationId;
  }

  let raw: Response;

  try {
    raw = await fetch(`${baseUrl}${request.path}`, {
      method: request.method ?? 'GET',
      headers,
      ...(request.body === undefined
        ? {}
        : { body: JSON.stringify(request.body) }),

      // Never cached. This is a private API returning per-user data, and a
      // cached response here would be one user's data served to another.
      cache: 'no-store',
    });
  } catch {
    // The Platform is unreachable. Reported as a 503 rather than thrown, so
    // every caller handles a failure the same way instead of some of them
    // remembering to try/catch.
    return {
      status: 503,
      data: null,
      problem: { code: 'PLATFORM.UNREACHABLE', status: 503 },
      correlationId: request.correlationId ?? null,
    };
  }

  const correlationId = raw.headers.get(CorrelationHeader);
  const text = await raw.text();
  const parsed: unknown = text ? safeParse(text) : null;

  if (raw.ok) {
    return {
      status: raw.status,
      data: parsed as T,
      problem: null,
      correlationId,
    };
  }

  return {
    status: raw.status,
    data: null,
    problem: (parsed as ProblemDetails) ?? null,
    correlationId,
  };
}

/**
 * Exchanges the refresh token for a new pair and stores it.
 *
 * The rotation is the Platform's; this only carries it. If the Platform refuses
 * — a revoked family, a reused token — the session is not repaired, and the
 * caller receives the 401 it already had.
 */
async function refresh(session: Session): Promise<Session | null> {
  const raw = await fetch(`${baseUrl}/api/v1/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ refreshToken: session.refreshToken }),
    cache: 'no-store',
  });

  if (!raw.ok) {
    return null;
  }

  const body = (await raw.json()) as {
    accessToken: string;
    refreshToken: string;
    expiresIn: number;
  };

  const next: Session = {
    accessToken: body.accessToken,
    refreshToken: body.refreshToken,
    expiresAt: Date.now() + body.expiresIn * 1000,
  };

  await writeSession(next);

  return next;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}
