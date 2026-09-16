import 'server-only';

import { cache } from 'react';
import { callPlatform } from './platform-client';
import type { MyPermissionsDto } from '@/types/platform';

/**
 * What the signed-in person may do, read once per request.
 *
 * `cache` deduplicates within a single render: the portal layout needs this to
 * decide which controls to show, and a page needs it to decide which reads are
 * worth issuing at all. Without it the same request would ask the Platform the
 * same question twice, a few milliseconds apart, on every navigation.
 *
 * **For deciding what to show, never for deciding what is allowed**
 * (ARCHITECTURE.md §14). The Platform makes every one of these decisions again
 * on the request itself, and its answer is the only one that counts.
 *
 * A failure yields an empty set, which hides things rather than showing
 * controls the Platform would refuse.
 */
export const readMyPermissions = cache(async (): Promise<readonly string[]> => {
  const response = await callPlatform<MyPermissionsDto>({
    path: '/api/v1/me/permissions',

    // Rendering, so no refresh may be attempted: rotating the token here would
    // spend it and then be unable to store what it got back.
    duringRender: true,
  });

  return response.data?.permissions ?? [];
});
