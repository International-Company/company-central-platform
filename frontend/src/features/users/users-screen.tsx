'use client';

import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import { IfPermitted } from '@/lib/permissions';
import type { PagedResult, UserDto } from '@/types/platform';

/**
 * The Users list.
 *
 * A table, a search box and paging — the shape every list screen in the Platform
 * takes (ARCHITECTURE.md §9.5). Nothing is invented here: the table, the paging
 * and the empty state are the shared components, so the next list screen looks
 * and behaves identically without anyone having to remember to make it so.
 */
export function UsersScreen() {
  const t = useTranslations('users');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [search, setSearch] = useState('');

  // Separate from the input on purpose. The query only changes when the user
  // submits, so typing does not fire a request per keystroke — and the request
  // that lands is the one they asked for, not whichever raced last.
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);

  const [result, setResult] = useState<PagedResult<UserDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const params = new URLSearchParams({
        page: String(page),
        pageSize: '25',
      });

      if (query) {
        params.set('search', query);
      }

      const response = await fetch(`/api/users?${params.toString()}`);

      if (!response.ok) {
        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      setResult((await response.json()) as PagedResult<UserDto>);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [page, query, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    // Back to the first page. Staying on page 4 of the previous result while
    // showing a new one is how a user concludes the search is broken.
    setPage(1);
    setQuery(search.trim());
  }

  const columns: Column<UserDto>[] = [
    {
      key: 'displayName',
      header: t('displayName'),
      render: (user) => (
        <span className="font-medium">{user.displayName}</span>
      ),
    },
    {
      key: 'username',
      header: t('username'),
      render: (user) => user.username,
    },
    {
      key: 'email',
      header: t('email'),
      render: (user) => user.email,
      secondary: true,
    },
    {
      key: 'status',
      header: t('status'),
      render: (user) => (
        <StatusBadge tone={statusTone(user.status)}>
          {statusLabel(user.status)}
        </StatusBadge>
      ),
    },
    {
      key: 'createdAt',
      header: t('createdAt'),
      render: (user) =>
        // Formatted through next-intl, so the calendar and numerals follow the
        // reader's locale rather than the server's.
        format.dateTime(new Date(user.createdAt), {
          dateStyle: 'medium',
        }),
      secondary: true,
    },
  ];

  function statusTone(status: string): StatusTone {
    switch (status) {
      case 'Active':
        return 'success';
      case 'Locked':
        return 'danger';
      case 'Disabled':
        return 'warning';
      default:
        return 'neutral';
    }
  }

  function statusLabel(status: string): string {
    switch (status) {
      case 'Active':
        return t('statusActive');
      case 'Locked':
        return t('statusLocked');
      case 'Disabled':
        return t('statusDisabled');
      default:
        return status;
    }
  }

  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('description')}
        action={
          // Hidden when the caller cannot create users, so they are not offered
          // an action that would be refused. The Platform refuses it anyway —
          // this only spares them the surprise.
          <IfPermitted permission="platform.users.create">
            <Button variant="primary">{t('createUser')}</Button>
          </IfPermitted>
        }
      />

      <form
        onSubmit={handleSearch}
        className="mb-4 flex flex-wrap items-end gap-2"
        role="search"
        data-print-hidden
      >
        <div className="flex min-w-56 flex-1 flex-col gap-1.5">
          <label
            htmlFor="user-search"
            className="text-sm font-medium text-text"
          >
            {tCommon('search')}
          </label>

          <input
            id="user-search"
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('searchPlaceholder')}
            className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm"
          />
        </div>

        <Button type="submit">{tCommon('search')}</Button>

        {query ? (
          <Button
            variant="quiet"
            onClick={() => {
              setSearch('');
              setQuery('');
              setPage(1);
            }}
          >
            {tCommon('clearFilters')}
          </Button>
        ) : null}
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
            rowKey={(user) => user.id}
            caption={t('title')}
            labels={{
              noResults: tTable('noResults'),
              noResultsDescription: tTable('noResultsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
            rowActions={() => (
              // A text link, not a row of glyphs. A row of icon buttons is
              // unreadable at a glance and unlabelled to a screen reader.
              <Button variant="quiet" size="sm">
                {tCommon('viewDetails')}
              </Button>
            )}
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
