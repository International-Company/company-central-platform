'use client';

import { useCallback, useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { ConfirmDialog } from '@/components/shared/confirm-dialog';
import { DataTable, type Column } from '@/components/shared/data-table';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import { IfPermitted } from '@/lib/permissions';
import { RoleForm } from './role-form';
import { RolePermissionsDialog } from './role-permissions-dialog';
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

  const [form, setForm] = useState<{ editing: RoleDto | null } | null>(null);
  const [permissionsFor, setPermissionsFor] = useState<RoleDto | null>(null);
  const [deactivating, setDeactivating] = useState<RoleDto | null>(null);
  const [applying, setApplying] = useState(false);

  const load = useCallback(async () => {
    setError(null);

    try {
      // Inactive roles included: this is the screen where one is brought back,
      // and a role that disappears when deactivated cannot be.
      const response = await fetch('/api/roles?includeInactive=true');

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
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function setActive(role: RoleDto, isActive: boolean) {
    setApplying(true);
    setError(null);

    try {
      const response = await fetch(`/api/roles/${role.id}/status`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isActive }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as {
          errors?: { message: string }[];
        };

        setError(
          body.errors?.[0]?.message ??
            (response.status === 403 ? tErrors('forbidden') : tErrors('generic')),
        );

        return;
      }

      setDeactivating(null);
      await load();
    } catch {
      setError(tErrors('network'));
    } finally {
      setApplying(false);
    }
  }

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
    {
      key: 'status',
      header: t('scope'),
      render: (role) => (
        <StatusBadge tone={role.isActive ? 'success' : 'warning'}>
          {role.isActive ? t('statusActive') : t('statusInactive')}
        </StatusBadge>
      ),
      secondary: true,
    },
  ];

  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('description')}
        action={
          <IfPermitted permission="platform.roles.manage">
            <Button variant="primary" onClick={() => setForm({ editing: null })}>
              {t('createRole')}
            </Button>
          </IfPermitted>
        }
      />

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {roles ? (
        <DataTable
          columns={columns}
          rows={roles}
          rowKey={(role) => role.id}
          caption={t('title')}
          labels={{
            noResults: t('noRoles'),
            noResultsDescription: t('noRolesDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
          rowActions={(role) => (
            <IfPermitted permission="platform.roles.manage">
              <div className="flex flex-wrap justify-end gap-2">
                {/* A system role is maintained by the Platform from its own
                    endpoints. Editing it would be overwritten on the next
                    startup, so the controls are not offered. */}
                {role.isSystem ? null : (
                  <>
                    <Button
                      variant="quiet"
                      size="sm"
                      onClick={() => setForm({ editing: role })}
                    >
                      {tCommon('edit')}
                    </Button>

                    <Button
                      variant="quiet"
                      size="sm"
                      onClick={() => setPermissionsFor(role)}
                    >
                      {t('editPermissions')}
                    </Button>

                    {role.isActive ? (
                      <Button
                        variant="quiet"
                        size="sm"
                        onClick={() => setDeactivating(role)}
                      >
                        {t('deactivateRole')}
                      </Button>
                    ) : (
                      <Button
                        variant="quiet"
                        size="sm"
                        onClick={() => void setActive(role, true)}
                      >
                        {t('activateRole')}
                      </Button>
                    )}
                  </>
                )}
              </div>
            </IfPermitted>
          )}
        />
      ) : !error ? (
        <p role="status" className="text-sm text-text-secondary">
          {tCommon('loading')}
        </p>
      ) : null}

      <RoleForm
        open={form !== null}
        editing={form?.editing ?? null}
        onClose={() => setForm(null)}
        onSaved={() => {
          setForm(null);
          void load();
        }}
      />

      <RolePermissionsDialog
        role={permissionsFor}
        onClose={() => setPermissionsFor(null)}
        onSaved={() => {
          setPermissionsFor(null);
          void load();
        }}
      />

      <ConfirmDialog
        open={deactivating !== null}
        title={t('deactivateConfirm')}
        description={t('deactivateDescription')}
        confirmLabel={t('deactivateRole')}
        cancelLabel={tCommon('cancel')}
        destructive
        busy={applying}
        busyLabel={tCommon('loading')}
        onConfirm={() => {
          if (deactivating) {
            void setActive(deactivating, false);
          }
        }}
        onCancel={() => setDeactivating(null)}
      />
    </>
  );
}
