import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { SettingChangeDto } from '@/types/platform';

interface Params {
  params: Promise<{ key: string }>;
}

/** What this setting was, what it became, who changed it and when. */
export async function GET(_request: Request, { params }: Params) {
  const { key } = await params;

  return await relay(
    await callPlatform<SettingChangeDto[]>({
      path: `/api/v1/configuration/settings/${encodeURIComponent(key)}/history`,
    }),
  );
}
