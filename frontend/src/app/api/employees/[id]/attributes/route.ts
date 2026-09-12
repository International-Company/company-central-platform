import { callPlatform } from '@/lib/platform-client';
import { relay } from '@/lib/bff';
import type { EmployeeAttributeDto } from '@/types/platform';

interface Params {
  params: Promise<{ id: string }>;
}

/**
 * Everything every application keeps about this person.
 *
 * Whole, not filtered to one application: "what do you hold about me" is a
 * question that must be answerable in full.
 */
export async function GET(_request: Request, { params }: Params) {
  const { id } = await params;

  return await relay(
    await callPlatform<EmployeeAttributeDto[]>({
      path: `/api/v1/organization/employees/${id}/attributes`,
    }),
  );
}
