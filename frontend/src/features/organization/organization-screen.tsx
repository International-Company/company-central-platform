'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { ConfirmDialog } from '@/components/shared/confirm-dialog';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import { IfPermitted } from '@/lib/permissions';
import { CompanyForm } from './company-form';
import { UnitForm } from './unit-form';
import { MoveUnitDialog } from './move-unit-dialog';
import { PositionsPanel } from './positions-panel';
import type { CompanyDto, OrganizationUnitTreeDto } from '@/types/platform';

/**
 * The company structure.
 *
 * **A table, not a drawn org chart** (ARCHITECTURE.md §9.5). The hierarchy is
 * carried by indentation in the name column and by an explicit level, which
 * stays readable when it is deep, when it is printed, and when it is read
 * aloud — none of which is true of a box-and-line diagram. The tree arrives
 * nested and is flattened here in the order a reader walks it.
 *
 * This screen has to exist before Employees can do anything: an employee
 * belongs to a unit, so with no units there is nobody to create.
 */
export function OrganizationScreen() {
  const t = useTranslations('organization');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const locale = useLocale();

  // Undefined until read, then either the company or null — and null is a real
  // answer, not a failure. Everything below hangs off it, so this screen is
  // where a new Platform is set up.
  const [company, setCompany] = useState<CompanyDto | null | undefined>(undefined);
  const [companyForm, setCompanyForm] = useState<{ editing: CompanyDto | null } | null>(null);

  const [tree, setTree] = useState<OrganizationUnitTreeDto[] | null>(null);
  const [includeInactive, setIncludeInactive] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [creatingUnder, setCreatingUnder] = useState<OrganizationUnitTreeDto | null>(null);
  const [creating, setCreating] = useState(false);
  const [renaming, setRenaming] = useState<OrganizationUnitTreeDto | null>(null);
  const [moving, setMoving] = useState<OrganizationUnitTreeDto | null>(null);
  const [deactivating, setDeactivating] = useState<OrganizationUnitTreeDto | null>(null);
  const [applying, setApplying] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const params = new URLSearchParams();

      if (includeInactive) {
        params.set('includeInactive', 'true');
      }

      const query = params.toString();

      const [companyResponse, response] = await Promise.all([
        fetch('/api/organization/company'),
        fetch(`/api/organization/units${query ? `?${query}` : ''}`),
      ]);

      if (companyResponse.ok) {
        // A 204 when there is none: an empty body is how the Platform says
        // "not set up yet", which is a state rather than an error.
        const text = await companyResponse.text();

        setCompany(text ? (JSON.parse(text) as CompanyDto) : null);
      }

      if (!response.ok) {
        if (response.status === 401) {
          // A full navigation, so the sign-in page renders fresh on the server
          // rather than reusing client state from a session that is gone.
          window.location.href = `/${locale}/login`;

          return;
        }

        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      setTree((await response.json()) as OrganizationUnitTreeDto[]);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [includeInactive, locale, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function deactivate() {
    if (!deactivating) {
      return;
    }

    setApplying(true);
    setError(null);

    try {
      const response = await fetch(
        `/api/organization/units/${deactivating.id}/deactivate`,
        { method: 'POST' },
      );

      if (!response.ok) {
        // The Platform refuses a unit that still has active children or active
        // employees, and says which. That message is the useful one — it names
        // the thing standing in the way — so it is shown rather than replaced.
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

  /** The nested tree as rows, in reading order, each keeping its depth. */
  const rows = useMemo(() => flatten(tree ?? []), [tree]);

  function unitTypeLabel(unitType: string): string {
    switch (unitType) {
      case 'Division':
        return t('typeDivision');
      case 'Department':
        return t('typeDepartment');
      case 'Section':
        return t('typeSection');
      case 'Center':
        return t('typeCenter');
      case 'Branch':
        return t('typeBranch');
      case 'Team':
        return t('typeTeam');
      default:
        return unitType;
    }
  }

  const columns: Column<OrganizationUnitTreeDto>[] = [
    {
      key: 'name',
      header: t('title'),
      render: (unit) => (
        <span
          className="inline-flex items-center font-medium"
          // Indentation as a logical inline start, so the tree reads inwards
          // from the right in Arabic and from the left in English.
          style={{ paddingInlineStart: `${Number(unit.depth) * 1.25}rem` }}
        >
          {locale === 'ar' ? unit.name.ar : unit.name.en}
        </span>
      ),
    },
    {
      key: 'code',
      header: t('code'),
      render: (unit) => unit.code,
    },
    {
      key: 'unitType',
      header: t('unitType'),
      render: (unit) => unitTypeLabel(unit.unitType),
      secondary: true,
    },
    {
      key: 'depth',
      header: t('depth'),
      render: (unit) => String(Number(unit.depth) + 1),
      numeric: true,
      secondary: true,
    },
    {
      key: 'status',
      header: t('status'),
      render: (unit) =>
        unit.isActive ? (
          <StatusBadge tone="success">{t('statusActive')}</StatusBadge>
        ) : (
          <StatusBadge tone="neutral">{t('statusInactive')}</StatusBadge>
        ),
    },
  ];

  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('description')}
        action={
          <IfPermitted permission="platform.organization.manage">
            {company ? (
              <div className="flex flex-wrap gap-2">
                <Button
                  onClick={() => setCompanyForm({ editing: company })}
                >
                  {t('renameCompany')}
                </Button>

                <Button
                  variant="primary"
                  onClick={() => {
                    setCreatingUnder(null);
                    setCreating(true);
                  }}
                >
                  {t('createUnit')}
                </Button>
              </div>
            ) : (
              // Creating a unit before the company exists is refused by the
              // Platform, so it is not offered. The one action that moves this
              // Platform forward is the one on screen.
              <Button
                variant="primary"
                onClick={() => setCompanyForm({ editing: null })}
              >
                {t('createCompany')}
              </Button>
            )}
          </IfPermitted>
        }
      />

      {company === null ? (
        <div className="mb-4 rounded-md border border-border bg-surface-sunken p-3">
          <p className="text-sm font-medium text-text">
            {t('companyMissingTitle')}
          </p>

          <p className="mt-0.5 text-sm text-text-secondary">
            {t('companyMissingDescription')}
          </p>
        </div>
      ) : company ? (
        <p className="mb-4 text-sm text-text-secondary">
          {t('companyTitle')}:{' '}
          <span className="font-medium text-text">
            {locale === 'ar' ? company.name.ar : company.name.en} ({company.code})
          </span>
        </p>
      ) : null}

      <div className="mb-4 flex items-center gap-2" data-print-hidden>
        <input
          id="include-inactive"
          type="checkbox"
          checked={includeInactive}
          onChange={(event) => setIncludeInactive(event.target.checked)}
          className="size-4 rounded border-border-strong"
        />

        <label htmlFor="include-inactive" className="text-sm text-text">
          {t('showInactive')}
        </label>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {loading && !tree ? (
        <p role="status" className="text-sm text-text-secondary">
          {tCommon('loading')}
        </p>
      ) : (
        <DataTable
          columns={columns}
          rows={rows}
          rowKey={(unit) => unit.id}
          caption={t('title')}
          labels={{
            // The empty state says what to do next. On a new Platform this
            // screen is empty by definition, and "no results" would read as a
            // failure rather than as a starting point.
            noResults: t('emptyTitle'),
            noResultsDescription: t('emptyDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
          rowActions={(unit) => (
            <IfPermitted permission="platform.organization.manage">
              <Button
                variant="quiet"
                size="sm"
                onClick={() => {
                  setCreatingUnder(unit);
                  setCreating(true);
                }}
              >
                {t('addChild')}
              </Button>

              <Button variant="quiet" size="sm" onClick={() => setRenaming(unit)}>
                {t('rename')}
              </Button>

              <Button variant="quiet" size="sm" onClick={() => setMoving(unit)}>
                {t('move')}
              </Button>

              {unit.isActive ? (
                <Button
                  variant="quiet"
                  size="sm"
                  onClick={() => setDeactivating(unit)}
                >
                  {t('deactivate')}
                </Button>
              ) : null}
            </IfPermitted>
          )}
        />
      )}

      {/* Beneath the tree, because the two are read together: an employee is
          assigned to a unit *and* a position, and navigating between two pages
          to compare them would be the interface getting in the way. */}
      <PositionsPanel enabled={company !== null && company !== undefined} />

      <CompanyForm
        open={companyForm !== null}
        editing={companyForm?.editing ?? null}
        onClose={() => setCompanyForm(null)}
        onSaved={() => {
          setCompanyForm(null);
          void load();
        }}
      />

      <UnitForm
        open={creating || renaming !== null}
        parent={creatingUnder}
        editing={renaming}
        onClose={() => {
          setCreating(false);
          setRenaming(null);
        }}
        onSaved={() => {
          setCreating(false);
          setRenaming(null);
          void load();
        }}
      />

      <MoveUnitDialog
        unit={moving}
        tree={tree ?? []}
        onClose={() => setMoving(null)}
        onMoved={() => {
          setMoving(null);
          void load();
        }}
      />

      <ConfirmDialog
        open={deactivating !== null}
        title={t('deactivateConfirm')}
        description={t('deactivateDescription')}
        confirmLabel={t('deactivate')}
        cancelLabel={tCommon('cancel')}
        destructive
        busy={applying}
        busyLabel={tCommon('loading')}
        onConfirm={() => void deactivate()}
        onCancel={() => setDeactivating(null)}
      />
    </>
  );
}

/**
 * The nested tree as a flat list, parents before their children.
 *
 * Depth comes from the Platform rather than from the recursion, so a row's
 * indentation is the structure's own answer and not this function's opinion
 * about where it happened to find the row.
 */
function flatten(
  units: readonly OrganizationUnitTreeDto[],
): OrganizationUnitTreeDto[] {
  return units.flatMap((unit) => [unit, ...flatten(unit.children)]);
}
