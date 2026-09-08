'use client';

import { useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import type { RoleDto } from '@/types/platform';

/**
 * The role catalogue.
 *
 * No paging and no search: this is a short, human-maintained list, and a
 * pagination control under twelve rows is furniture. The shared DataTable is
 * still used, so the list looks and behaves like every other one.
 */
export function RolesScreen() {
  const t = useTranslations('roles');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const locale = useLocale();

  const [roles, setRoles] = useState<RoleDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function load() {
      try {
        const response = await fetch('/api/roles');

        if (!response.ok) {
          setError(
            response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
          );

          return;
        }

        setRoles((await response.json()) as RoleDto[]);
      } catch {
        setError(tErrors('network'));
      }
    }

    void load();
  }, [tErrors]);

  const columns: Column<RoleDto>[] = [
    {
      key: 'name',
      header: t('name'),
      render: (role) => (
        <span className="font-medium">
          {locale === 'ar' ? role.nameAr : role.nameEn}
        </span>
      ),
    },
    { key: 'code', header: t('code'), render: (role) => role.code },
    {
      key: 'permissionCount',
      header: t('permissionCount'),
      render: (role) => String(role.permissionCount),
      numeric: true,
    },
    {
      key: 'system',
      header: t('system'),
      render: (role) => (
        // A system role cannot be edited, and saying so here saves someone
        // opening it to find out.
        <StatusBadge tone={role.isSystem ? 'neutral' : 'success'}>
          {role.isSystem ? t('systemRole') : t('customRole')}
        </StatusBadge>
      ),
    },
  ];

  return (
    <>
      <PageHeader title={t('title')} description={t('description')} />

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {roles ? (
        <DataTable
          columns={columns}
          rows={roles}
          rowKey={(role) => role.id}
          caption={t('title')}
          labels={{
            noResults: tTable('noResults'),
            noResultsDescription: tTable('noResultsDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
        />
      ) : !error ? (
        <p role="status" className="text-sm text-text-secondary">
          {tCommon('loading')}
        </p>
      ) : null}
    </>
  );
}
