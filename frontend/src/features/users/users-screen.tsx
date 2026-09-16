'use client';

import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import { ConfirmDialog } from '@/components/shared/confirm-dialog';
import { UserForm } from './user-form';
import { UserRolesDialog } from './user-roles-dialog';
import { IfPermitted, usePermission } from '@/lib/permissions';
import type {
  OrganizationUnitTreeDto,
  PagedResult,
  RoleDto,
  UserDto,
} from '@/types/platform';

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
  const tRoles = useTranslations('roles');
  const format = useFormatter();
  const locale = useLocale();

  const [search, setSearch] = useState('');

  // Separate from the input on purpose. The query only changes when the user
  // submits, so typing does not fire a request per keystroke — and the request
  // that lands is the one they asked for, not whichever raced last.
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);

  const [result, setResult] = useState<PagedResult<UserDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // The action a confirmation is currently asking about. One piece of state
  // rather than a boolean per action: only one dialog can be open, and modelling
  // it as one value makes that structurally true instead of merely intended.
  const [pending, setPending] = useState<{
    user: UserDto;
    action: 'enable' | 'disable' | 'unlock';
  } | null>(null);
  const [applying, setApplying] = useState(false);

  // Whose roles are being managed, and the reference data the dialog needs to
  // name what it is granting. Loaded once here rather than by the dialog, so
  // opening it is instant and closing it does not throw the lists away.
  const [managingRoles, setManagingRoles] = useState<UserDto | null>(null);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [units, setUnits] = useState<OrganizationUnitTreeDto[]>([]);

  const canAssign = usePermission('platform.roles.assign');

  // `null` means closed; `{ editing: null }` means creating. Modelled as one
  // value so "which form is open, and on what" cannot disagree with itself.
  const [form, setForm] = useState<{ editing: UserDto | null } | null>(null);

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

      setResult((await response.json()) as PagedResult<UserDto>);
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
    if (!canAssign) {
      return;
    }

    // Failures are silent: this is reference data for a dialog, and the list
    // behind it — which is what the screen is for — does not depend on it.
    void (async () => {
      const [roleResponse, unitResponse] = await Promise.all([
        fetch('/api/roles').catch(() => null),
        fetch('/api/organization/units').catch(() => null),
      ]);

      if (roleResponse?.ok) {
        const body = (await roleResponse.json()) as PagedResult<RoleDto> | RoleDto[];

        setRoles(Array.isArray(body) ? body : body.items);
      }

      if (unitResponse?.ok) {
        setUnits(flattenUnits((await unitResponse.json()) as OrganizationUnitTreeDto[]));
      }
    })();
  }, [canAssign]);

  async function applyStatus() {
    if (!pending) {
      return;
    }

    setApplying(true);
    setError(null);

    try {
      const response = await fetch(`/api/users/${pending.user.id}/status`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action: pending.action }),
      });

      if (!response.ok) {
        // 403 here usually means step-up, not a missing permission: creating
        // and restoring access are the actions that demand a recent second
        // factor. Saying "you do not have permission" would send an
        // administrator to ask for a permission they already hold.
        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      setPending(null);
      await load();
    } catch {
      setError(tErrors('network'));
    } finally {
      setApplying(false);
    }
  }

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
            <Button
              variant="primary"
              onClick={() => setForm({ editing: null })}
            >
              {t('createUser')}
            </Button>
          </IfPermitted>
        }
      />

      <form
        onSubmit={handleSearch}
        className="mb-4 flex flex-wrap items-end gap-2"
        role="search"
        data-print-hidden
      >
        <div className="flex w-full max-w-sm flex-col gap-1.5">
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
            className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm"
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
            rowActions={(user) => (
              // Text links, never a row of glyphs. Which action is offered
              // depends on the state the account is actually in: unlocking an
              // account that is not locked is a button that does nothing, and a
              // button that does nothing teaches people to distrust the others.
              <IfPermitted permission="platform.users.edit">
                <div className="flex flex-wrap justify-end gap-2">
                  <Button
                    variant="quiet"
                    size="sm"
                    onClick={() => setForm({ editing: user })}
                  >
                    {tCommon('edit')}
                  </Button>

                  <IfPermitted permission="platform.roles.assign">
                    <Button
                      variant="quiet"
                      size="sm"
                      onClick={() => setManagingRoles(user)}
                    >
                      {tRoles('manageRoles')}
                    </Button>
                  </IfPermitted>

                  {user.status === 'Locked' ? (
                    <Button
                      variant="quiet"
                      size="sm"
                      onClick={() => setPending({ user, action: 'unlock' })}
                    >
                      {t('unlock')}
                    </Button>
                  ) : null}

                  {user.status === 'Disabled' ? (
                    <Button
                      variant="quiet"
                      size="sm"
                      onClick={() => setPending({ user, action: 'enable' })}
                    >
                      {t('enable')}
                    </Button>
                  ) : (
                    <Button
                      variant="quiet"
                      size="sm"
                      onClick={() => setPending({ user, action: 'disable' })}
                    >
                      {t('disable')}
                    </Button>
                  )}
                </div>
              </IfPermitted>
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

      <ConfirmDialog
        open={pending !== null}
        title={
          pending
            ? t(`${pending.action}Confirm`, { name: pending.user.displayName })
            : ''
        }
        description={pending ? t(`${pending.action}Description`) : ''}
        confirmLabel={pending ? t(pending.action) : ''}
        cancelLabel={tCommon('cancel')}
        destructive={pending?.action === 'disable'}
        busy={applying}
        busyLabel={tCommon('loading')}
        onConfirm={() => void applyStatus()}
        onCancel={() => setPending(null)}
      />

      <UserForm
        open={form !== null}
        editing={form?.editing ?? null}
        onClose={() => setForm(null)}
        onSaved={() => {
          setForm(null);

          // Reload rather than patch the row in place. The Platform decides
          // what the saved record looks like — a normalised username, a status
          // — and guessing here would show something subtly different from what
          // was stored.
          void load();
        }}
      />

      <UserRolesDialog
        user={managingRoles}
        roles={roles}
        units={units}
        onClose={() => setManagingRoles(null)}
      />
    </>
  );
}

/** The nested structure as a flat list, for naming a grant's scope. */
function flattenUnits(
  units: readonly OrganizationUnitTreeDto[],
): OrganizationUnitTreeDto[] {
  return units.flatMap((unit) => [unit, ...flattenUnits(unit.children)]);
}
