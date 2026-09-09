import { callPlatform } from '@/lib/platform-client';
import { forwardQuery, relay } from '@/lib/bff';
import type { SettingDto } from '@/types/platform';

/**
 * Every declared setting, and what has been set for it.
 *
 * A sensitive setting's value is absent from the Platform's own response shape,
 * so nothing here has to remember to hide it.
 */
export async function GET(request: Request) {
  const query = forwardQuery(request.url, ['applicationCode']);

  return await relay(
    await callPlatform<SettingDto[]>({
      path: `/api/v1/configuration/settings${query}`,
    }),
  );
}
