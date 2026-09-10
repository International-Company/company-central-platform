import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { JobSummaryDto } from '@/types/platform';

/** Every background job, when it last ran and how it went. */
export async function GET() {
  return await relay(
    await callPlatform<JobSummaryDto[]>({ path: '/api/v1/platform/jobs' }),
  );
}
