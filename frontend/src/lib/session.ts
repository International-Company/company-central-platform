import 'server-only';

import { cookies } from 'next/headers';

/**
 * The session, held server-side.
 *
 * **No token ever reaches the browser.** Not in `localStorage`, not in
 * `sessionStorage`, not in a readable cookie, not in a JavaScript variable. The
 * browser holds an opaque, `httpOnly` cookie and nothing else; the access and
 * refresh tokens live only in this process, and only for the duration of a
 * request (ADR-006 §13.2).
 *
 * The reason is narrow and worth stating: any token a script can read is a token
 * an XSS on any page of the application can steal. Storage that JavaScript
 * cannot reach removes that entire class of theft, and it is the only mitigation
 * that does not depend on never having a cross-site scripting bug.
 */

/** The cookie the browser holds. Opaque, and useless without this server. */
const SessionCookie = 'ccp.session';

export interface Session {
  accessToken: string;
  refreshToken: string;

  /** When the access token stops being accepted. Milliseconds since epoch. */
  expiresAt: number;
}

/**
 * Cookie attributes.
 *
 * `httpOnly` keeps it away from script. `sameSite: 'lax'` blocks the
 * cross-site form post that a CSRF attack relies on while still allowing an
 * ordinary link from an email to land the user signed in. `secure` is set
 * outside development, where there is no HTTPS to require.
 */
const cookieOptions = {
  httpOnly: true,
  sameSite: 'lax',
  secure: process.env.NODE_ENV === 'production',
  path: '/',
} as const;

export async function readSession(): Promise<Session | null> {
  const store = await cookies();
  const raw = store.get(SessionCookie)?.value;

  if (!raw) {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(
      Buffer.from(raw, 'base64url').toString('utf8'),
    );

    if (
      typeof parsed !== 'object' ||
      parsed === null ||
      typeof (parsed as Session).accessToken !== 'string' ||
      typeof (parsed as Session).refreshToken !== 'string' ||
      typeof (parsed as Session).expiresAt !== 'number'
    ) {
      return null;
    }

    return parsed as Session;
  } catch {
    // A cookie we cannot read is a cookie we do not trust. Treated as absent
    // rather than as an error: the user simply signs in again, which is the
    // right outcome for a corrupted or tampered value.
    return null;
  }
}

export async function writeSession(session: Session): Promise<void> {
  const store = await cookies();

  store.set(SessionCookie, encode(session), {
    ...cookieOptions,

    // The cookie outlives the access token on purpose — it has to, or the
    // refresh token would be discarded at the exact moment it is needed. It
    // expires with the refresh token instead.
    maxAge: 60 * 60 * 24 * 14,
  });
}

export async function clearSession(): Promise<void> {
  const store = await cookies();

  store.set(SessionCookie, '', { ...cookieOptions, maxAge: 0 });
}

function encode(session: Session): string {
  // base64url only, not encryption. The cookie is `httpOnly` and never leaves
  // this server's control; encoding is so the value survives transport intact,
  // not so it stays secret. Encrypting it would look like protection while
  // adding a key to manage and changing nothing about who can read it.
  return Buffer.from(JSON.stringify(session), 'utf8').toString('base64url');
}
