import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { MfaEnrolmentDto } from '@/types/platform';

/**
 * Begins enrolment: the Platform generates a secret and returns the URI an
 * authenticator app scans.
 *
 * Nothing is stored here. The secret exists on the Platform, encrypted, and in
 * the user's phone — never in this process beyond the life of the response, and
 * never in the browser's storage.
 */
export async function POST() {
  return await relay(
    await callPlatform<MfaEnrolmentDto>({
      path: '/api/v1/me/mfa/enrol',
      method: 'POST',
    }),
  );
}
