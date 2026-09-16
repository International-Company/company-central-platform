'use client';

import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import Link from 'next/link';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import { IfPermitted, usePermission } from '@/lib/permissions';
import { EmployeeAttributes } from './employee-attributes';
import { EmployeeForm } from './employee-form';
import { EmployeeAccountDialog } from './employee-account-dialog';
import { EmployeeTransferDialog } from './employee-transfer-dialog';
import type {
  EmployeeDto,
  OrganizationUnitTreeDto,
  PagedResult,
  PositionDto,
  UserDto,
} from '@/types/platform';
import { EmptyValue } from '@/components/shared/empty-value';

/**
 * The Employees list.
 *
 * The one thing here that is not shared with the Users screen: names are
 * bilingual, and the reader sees the one in their own language. That is the
 * whole reason both are required on write — an optional second name becomes a
 * permanently empty column, and the Arabic interface then shows English names.
 */
export function EmployeesScreen() {
  const t = useTranslations('employees');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const locale = useLocale();

  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);

  const [result, setResult] = useState<PagedResult<EmployeeDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [creating, setCreating] = useState(false);

  // The structure, for the create form's unit picker. Loaded once here rather
  // than by the form, so the screen knows whether creating is possible at all
  // before it offers a button that could only fail.
  const [units, setUnits] = useState<OrganizationUnitTreeDto[]>([]);

  const [showingAttributes, setShowingAttributes] = useState<EmployeeDto | null>(null);
  const [transferring, setTransferring] = useState<EmployeeDto | null>(null);
  const [linking, setLinking] = useState<EmployeeDto | null>(null);

  // Reference data for the two dialogs: where someone can be moved to, what
  // they can be given, and which account they can be attached to.
  const [positions, setPositions] = useState<PositionDto[]>([]);
  const [users, setUsers] = useState<UserDto[]>([]);

  const canManage = usePermission('platform.employees.manage');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '25' });

      if (query) {
        params.set('search', query);
      }

      const response = await fetch(`/api/employees?${params.toString()}`);

      if (!response.ok) {
        if (response.status === 401) {
          // The session is gone, and the BFF has already dropped the cookie.
          // A full navigation rather than a router push, so the server renders
          // the sign-in page from scratch instead of reusing client state that
          // belongs to a session that no longer exists.
          window.location.href = `/${locale}/login`;

          return;
        }

        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      setResult((await response.json()) as PagedResult<EmployeeDto>);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [page, query, tErrors, locale]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!canManage) {
      return;
    }

    // Failure is silent on purpose. The structure is needed only to offer
    // creation; if it cannot be read, the button stays hidden and the list —
    // which is what this screen is for — still works.
    // Failures stay silent by design: this is reference data for actions, and
    // the list behind them — which is what the screen is for — does not depend
    // on it. A missing list means an emptier dropdown, not a broken page.
    void (async () => {
      const [unitResponse, positionResponse, userResponse] = await Promise.all([
        fetch('/api/organization/units').catch(() => null),
        fetch('/api/organization/positions').catch(() => null),
        fetch('/api/users?page=1&pageSize=200').catch(() => null),
      ]);

      if (unitResponse?.ok) {
        setUnits(flatten((await unitResponse.json()) as OrganizationUnitTreeDto[]));
      }

      if (positionResponse?.ok) {
        setPositions((await positionResponse.json()) as PositionDto[]);
      }

      if (userResponse?.ok) {
        setUsers(((await userResponse.json()) as PagedResult<UserDto>).items);
      }
    })();
  }, [canManage]);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    setPage(1);
    setQuery(search.trim());
  }

  const columns: Column<EmployeeDto>[] = [
    {
      key: 'employeeNumber',
      header: t('employeeNumber'),
      render: (employee) => employee.employeeNumber,
      numeric: true,
    },
    {
      key: 'fullName',
      header: t('fullName'),
      render: (employee) => (
        <span className="font-medium">
          {locale === 'ar' ? employee.fullName.ar : employee.fullName.en}
        </span>
      ),
    },
    {
      key: 'unit',
      header: t('unit'),
      render: (employee) => employee.unitCode,
    },
    {
      key: 'position',
      header: t('position'),
      render: (employee) => employee.positionCode ?? <EmptyValue />,
      secondary: true,
    },
    {
      key: 'linkedAccount',
      header: t('linkedAccount'),
      render: (employee) =>
        employee.userId ? (
          <StatusBadge tone="success">{tCommon('yes')}</StatusBadge>
        ) : (
          // Said in words, not shown as an absence. A blank cell reads as
          // missing data rather than as a deliberate state.
          <StatusBadge tone="neutral">{t('notLinked')}</StatusBadge>
        ),
    },
  ];

  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('description')}
        action={
          <IfPermitted permission="platform.employees.manage">
            <Button
              variant="primary"
              onClick={() => setCreating(true)}
              // Creating an employee with no unit to put them in is refused by
              // the Platform. Saying so before the attempt, and naming the
              // screen that fixes it, beats a rejection the person has to
              // decode.
              disabled={units.length === 0}
              title={units.length === 0 ? t('noUnitsDescription') : undefined}
            >
              {t('createEmployee')}
            </Button>
          </IfPermitted>
        }
      />

      {canManage && units.length === 0 ? (
        <div className="mb-4 rounded-md border border-border bg-surface-sunken p-3">
          <p className="text-sm font-medium text-text">{t('noUnitsTitle')}</p>

          <p className="mt-0.5 text-sm text-text-secondary">
            {t('noUnitsDescription')}
          </p>

          <Link
            href={`/${locale}/organization`}
            className="mt-2 inline-block text-sm text-primary-700 underline underline-offset-2"
          >
            {t('goToOrganization')}
          </Link>
        </div>
      ) : null}

      <form
        onSubmit={handleSearch}
        className="mb-4 flex flex-wrap items-end gap-2"
        role="search"
        data-print-hidden
      >
        <div className="flex w-full max-w-sm flex-col gap-1.5">
          <label
            htmlFor="employee-search"
            className="text-sm font-medium text-text"
          >
            {tCommon('search')}
          </label>

          <input
            id="employee-search"
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('searchPlaceholder')}
            className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm"
          />
        </div>

        <Button type="submit">{tCommon('search')}</Button>
      </form>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {loading && !result ? (
        <p role="status" className="text-sm text-text-secondary">
          {tCommon('loading')}
        </p>
      ) : result ? (
        <>
          <DataTable
            columns={columns}
            rows={result.items}
            rowKey={(employee) => employee.id}
            caption={t('title')}
            labels={{
              noResults: tTable('noResults'),
              noResultsDescription: tTable('noResultsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
            rowActions={(employee) => (
              <div className="flex flex-wrap justify-end gap-2">
                {/*
                  Outside the manage guard on purpose: reading what is held
                  about somebody is a view permission, and the panel guards its
                  own editing.
                */}
                <Button
                  variant="quiet"
                  size="sm"
                  onClick={() =>
                    setShowingAttributes(
                      showingAttributes?.id === employee.id ? null : employee)
                  }
                >
                  {t('attributes')}
                </Button>

                <IfPermitted permission="platform.employees.manage">
                  <div className="flex flex-wrap justify-end gap-2">
                  <Button
                    variant="quiet"
                    size="sm"
                    onClick={() => setTransferring(employee)}
                  >
                    {t('transfer')}
                  </Button>

                  <Button
                    variant="quiet"
                    size="sm"
                    onClick={() => setLinking(employee)}
                  >
                    {employee.userId ? t('account') : t('linkAccount')}
                  </Button>
                  </div>
                </IfPermitted>
              </div>
            )}
          />

          {showingAttributes ? (
            <EmployeeAttributes
              employee={showingAttributes}
              onClose={() => setShowingAttributes(null)}
            />
          ) : null}

          <Pagination
            page={result.page}
            pageSize={result.pageSize}
            totalItems={result.totalItems}
            onPageChange={setPage}
            labels={{
              showing: (values) => tTable('showing', values),
              previous: tTable('previous'),
              next: tTable('next'),
            }}
          />
        </>
      ) : null}

      <EmployeeForm
        open={creating}
        units={units}
        onClose={() => setCreating(false)}
        onSaved={() => {
          setCreating(false);
          setPage(1);
          void load();
        }}
      />

      <EmployeeTransferDialog
        employee={transferring}
        units={units}
        positions={positions}
        colleagues={result?.items ?? []}
        onClose={() => setTransferring(null)}
        onTransferred={() => {
          setTransferring(null);
          void load();
        }}
      />

      <EmployeeAccountDialog
        employee={linking}
        users={users}
        onClose={() => setLinking(null)}
        onLinked={() => {
          setLinking(null);
          void load();
        }}
      />
    </>
  );
}

/** The nested structure as a flat list, parents before their children. */
function flatten(
  units: readonly OrganizationUnitTreeDto[],
): OrganizationUnitTreeDto[] {
  return units.flatMap((unit) => [unit, ...flatten(unit.children)]);
}
