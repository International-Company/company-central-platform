import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { JobRunDto } from '@/types/platform';

interface Params {
  params: Promise<{ job: string }>;
}

/** The recent runs of one job, newest first. */
export async function GET(_request: Request, { params }: Params) {
  const { job } = await params;

  return await relay(
    await callPlatform<JobRunDto[]>({
      path: `/api/v1/platform/jobs/${encodeURIComponent(job)}/runs`,
    }),
  );
}
