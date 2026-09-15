import { getFormatter, getTranslations } from 'next-intl/server';
import { PageHeader } from '@/components/shared/page-header';
import { DashboardSummary } from '@/features/dashboard/dashboard-summary';
import { callPlatform } from '@/lib/platform-client';
import type {
  EmployeeDto,
  OrganizationUnitTreeDto,
  PagedResult,
  RoleDto,
  UserDto,
} from '@/types/platform';

/**
 * What the Platform currently holds, and what to do next.
 *
 * **Server-rendered, and each figure is read with the caller's own
 * permissions.** A count the person may not see comes back refused and is shown
 * as unavailable rather than as zero — which would be a lie, and the kind that
 * looks like a working system.
 *
 * Every panel is a link. A number nobody can act on is decoration; this is the
 * way in to the screen that explains it.
 *
 * The next steps are ordered by what actually blocks what: units before
 * employees, because an employee needs a unit; a second factor before roles,
 * because granting one demands recent proof of identity. On a Platform that is
 * already set up they read as a summary of how it fits together.
 */
export default async function DashboardPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  const t = await getTranslations('dashboard');
  const tNav = await getTranslations('nav');
  const format = await getFormatter();

  // In parallel: four independent reads, and the slowest decides the page.
  // In sequence this would be four round trips to the Platform for a screen
  // that shows four numbers.
  //
  // `duringRender` on every one: a page may not refresh the session. Rotating
  // the token during render spends it and then cannot store the replacement,
  // which would sign the person out for loading a page.
  const [users, employees, units, roles] = await Promise.all([
    callPlatform<PagedResult<UserDto>>({
      path: '/api/v1/users?page=1&pageSize=1',
      duringRender: true,
    }),
    callPlatform<PagedResult<EmployeeDto>>({
      path: '/api/v1/organization/employees?page=1&pageSize=1',
      duringRender: true,
    }),
    callPlatform<OrganizationUnitTreeDto[]>({
      path: '/api/v1/organization/units/tree',
      duringRender: true,
    }),
    callPlatform<RoleDto[]>({ path: '/api/v1/roles', duringRender: true }),
  ]);

  const cards = [
    {
      href: `/${locale}/users`,
      label: t('users'),
      value: users.data ? Number(users.data.totalItems) : null,
    },
    {
      href: `/${locale}/employees`,
      label: t('employees'),
      value: employees.data ? Number(employees.data.totalItems) : null,
    },
    {
      href: `/${locale}/organization`,
      label: t('units'),
      value: units.data ? countUnits(units.data) : null,
    },
    {
      href: `/${locale}/roles`,
      label: t('roles'),
      value: roles.data ? roles.data.length : null,
    },
  ];

  const steps = [
    { href: `/${locale}/organization`, text: t('stepOrganization') },
    { href: `/${locale}/employees`, text: t('stepEmployees') },
    { href: `/${locale}/security`, text: t('stepSecurity') },
    { href: `/${locale}/users`, text: t('stepUsers') },
  ];

  return (
    <>
      <PageHeader title={tNav('dashboard')} description={t('description')} />

      <DashboardSummary
        heading={tNav('dashboard')}
        nextStepsHeading={t('nextSteps')}
        unavailable={t('unavailable')}
        unavailableHint={t('unavailableHint')}
        figures={cards.map((card) => ({
          href: card.href,
          label: card.label,
          value: card.value === null ? null : format.number(card.value),
        }))}
        steps={steps.map((step, index) => ({ ...step, number: format.number(index + 1) }))}
      />
    </>
  );
}

function countUnits(units: readonly OrganizationUnitTreeDto[]): number {
  return units.reduce(
    (total, unit) => total + 1 + countUnits(unit.children),
    0,
  );
}
