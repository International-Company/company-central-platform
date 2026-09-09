import Link from 'next/link';
import { getFormatter, getTranslations } from 'next-intl/server';
import { PageHeader } from '@/components/shared/page-header';
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

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {cards.map((card) => (
          <Link
            key={card.href}
            href={card.href}
            className="rounded-md border border-border bg-surface p-4 hover:border-border-strong"
          >
            <p className="text-sm text-text-secondary">{card.label}</p>

            {card.value === null ? (
              <>
                <p className="mt-1 text-lg font-semibold text-text-muted">
                  {t('unavailable')}
                </p>

                <p className="mt-0.5 text-xs text-text-secondary">
                  {t('unavailableHint')}
                </p>
              </>
            ) : (
              // Formatted through next-intl, so the numerals follow the
              // reader's locale rather than the server's.
              <p className="mt-1 text-2xl font-semibold text-text">
                {format.number(card.value)}
              </p>
            )}
          </Link>
        ))}
      </div>

      <section className="mt-6 rounded-md border border-border bg-surface p-4">
        <h2 className="text-sm font-semibold text-text">{t('nextSteps')}</h2>

        <ol className="mt-2 flex list-decimal flex-col gap-1.5 ps-5 text-sm">
          {steps.map((step) => (
            <li key={step.href} className="text-text-secondary">
              <Link
                href={step.href}
                className="text-primary-700 underline underline-offset-2"
              >
                {step.text}
              </Link>
            </li>
          ))}
        </ol>
      </section>
    </>
  );
}

/** Every unit in the structure, at every depth. */
function countUnits(units: readonly OrganizationUnitTreeDto[]): number {
  return units.reduce(
    (total, unit) => total + 1 + countUnits(unit.children),
    0,
  );
}
