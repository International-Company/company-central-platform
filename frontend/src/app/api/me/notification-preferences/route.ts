import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { NotificationPreferenceDto } from '@/types/platform';

/** What the caller has turned off. Absence means everything is on. */
export async function GET() {
  return await relay(
    await callPlatform<NotificationPreferenceDto[]>({
      path: '/api/v1/me/notification-preferences',
    }),
  );
}

export async function PUT(request: Request) {
  const body: unknown = await request.json().catch(() => null);

  // Security notifications are refused by the Platform. The screen does not
  // offer them either, but the refusal lives where it cannot be bypassed.
  return await relay(
    await callPlatform({
      path: '/api/v1/me/notification-preferences',
      method: 'PUT',
      body,
    }),
  );
}
