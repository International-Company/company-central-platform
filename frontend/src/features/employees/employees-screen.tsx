'use client';

import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import type { EmployeeDto, PagedResult } from '@/types/platform';

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
      render: (employee) => employee.positionCode ?? '—',
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
      <PageHeader title={t('title')} description={t('description')} />

      <form
        onSubmit={handleSearch}
        className="mb-4 flex flex-wrap items-end gap-2"
        role="search"
        data-print-hidden
      >
        <div className="flex min-w-56 flex-1 flex-col gap-1.5">
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
            className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm"
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
          />

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
    </>
  );
}
