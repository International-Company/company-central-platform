import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { UserDto } from '@/types/platform';

/**
 * Records the caller's own choice of language.
 *
 * The portal already knows which language it is showing, from the URL. This is
 * for the messages that arrive when nobody is looking at a URL — an email is in
 * the right language only if the choice was written down.
 */
export async function PUT(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  return await relay(
    await callPlatform<UserDto>({
      path: '/api/v1/me/language',
      method: 'PUT',
      body,
    }),
  );
}
