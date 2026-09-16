'use client';

import { useCallback, useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { FormDialog, fieldErrors } from '@/components/shared/form-dialog';
import { StatusBadge } from '@/components/shared/status-badge';
import { IfPermitted } from '@/lib/permissions';
import type { PositionDto, ProblemResponse } from '@/types/platform';
import { EmptyValue } from '@/components/shared/empty-value';

/**
 * The company's job titles.
 *
 * **On the same screen as the structure, and not the same thing as a role.** A
 * position says what someone does in the organization; a role says what they may
 * do in the software. They live under different permissions and different
 * screens on purpose — merging them would mean a job title change silently
 * altering access, and every permission grant needing an HR justification.
 *
 * It sits beneath the unit tree rather than on a page of its own because the two
 * are read together: you assign an employee to a unit *and* a position, and
 * having to navigate between them to compare would be the interface getting in
 * the way.
 */
export function PositionsPanel({ enabled }: { enabled: boolean }) {
  const t = useTranslations('organization');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const locale = useLocale();

  const [positions, setPositions] = useState<PositionDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [form, setForm] = useState<{ editing: PositionDto | null } | null>(null);
  const [code, setCode] = useState('');
  const [titleAr, setTitleAr] = useState('');
  const [titleEn, setTitleEn] = useState('');
  const [level, setLevel] = useState('');
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});

  const load = useCallback(async () => {
    setError(null);

    try {
      const response = await fetch('/api/organization/positions?includeInactive=true');

      if (!response.ok) {
        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      setPositions((await response.json()) as PositionDto[]);
    } catch {
      setError(tErrors('network'));
    }
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!form) {
      return;
    }

    setCode(form.editing?.code ?? '');
    setTitleAr(form.editing?.title.ar ?? '');
    setTitleEn(form.editing?.title.en ?? '');
    setLevel(form.editing?.level === null ? '' : String(form.editing?.level ?? ''));
    setFormError(null);
    setFields({});
  }, [form]);

  async function submit() {
    if (!form) {
      return;
    }

    setBusy(true);
    setFormError(null);
    setFields({});

    try {
      const editing = form.editing;

      const response = await fetch(
        editing
          ? `/api/organization/positions/${editing.id}/title`
          : '/api/organization/positions',
        {
          method: editing ? 'PUT' : 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(
            editing
              ? { titleAr, titleEn }
              : {
                  code,
                  titleAr,
                  titleEn,

                  // Absent rather than zero. An empty box means "no level", and
                  // zero is a level.
                  level: level === '' ? null : Number(level),
                },
          ),
        },
      );

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;
        const mapped = fieldErrors(body.errors);

        setFields(mapped);

        if (Object.keys(mapped).length === 0) {
          setFormError(
            response.status === 409
              ? (body.errors?.[0]?.message ?? tErrors('generic'))
              : response.status === 403
                ? tErrors('forbidden')
                : tErrors('generic'),
          );
        }

        return;
      }

      setForm(null);
      await load();
    } catch {
      setFormError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function setActive(position: PositionDto, isActive: boolean) {
    setError(null);

    try {
      const response = await fetch(`/api/organization/positions/${position.id}/status`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isActive }),
      });

      if (!response.ok) {
        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      await load();
    } catch {
      setError(tErrors('network'));
    }
  }

  const columns: Column<PositionDto>[] = [
    {
      key: 'title',
      header: t('positionsTitle'),
      render: (position) => (
        <span className="font-medium">
          {locale === 'ar' ? position.title.ar : position.title.en}
        </span>
      ),
    },
    { key: 'code', header: t('code'), render: (position) => position.code },
    {
      key: 'level',
      header: t('level'),
      render: (position) => (position.level === null ? <EmptyValue /> : String(position.level)),
      numeric: true,
      secondary: true,
    },
    {
      key: 'status',
      header: t('status'),
      render: (position) => (
        <StatusBadge tone={position.isActive ? 'success' : 'neutral'}>
          {position.isActive ? t('statusActive') : t('statusInactive')}
        </StatusBadge>
      ),
    },
  ];

  return (
    <section className="mt-8">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-text">
            {t('positionsTitle')}
          </h2>

          <p className="mt-0.5 max-w-prose text-sm text-text-secondary">
            {t('positionsDescription')}
          </p>
        </div>

        <IfPermitted permission="platform.organization.manage">
          <Button
            onClick={() => setForm({ editing: null })}
            // A position belongs to the company. Offering to create one before
            // the company exists is offering something the Platform refuses.
            disabled={!enabled}
          >
            {t('createPosition')}
          </Button>
        </IfPermitted>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {positions ? (
        <DataTable
          columns={columns}
          rows={positions}
          rowKey={(position) => position.id}
          caption={t('positionsTitle')}
          labels={{
            noResults: t('noPositions'),
            noResultsDescription: t('noPositionsDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
          rowActions={(position) => (
            <IfPermitted permission="platform.organization.manage">
              <>
              <Button
                variant="quiet"
                size="sm"
                onClick={() => setForm({ editing: position })}
              >
                {tCommon('edit')}
              </Button>

              <Button
                variant="quiet"
                size="sm"
                onClick={() => void setActive(position, !position.isActive)}
              >
                {position.isActive ? t('deactivate') : t('activate')}
              </Button>
            </>
            </IfPermitted>
          )}
        />
      ) : null}

      <FormDialog
        open={form !== null}
        title={form?.editing ? t('editPosition') : t('createPosition')}
        submitLabel={tCommon('save')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={formError}
        onSubmit={() => void submit()}
        onCancel={() => setForm(null)}
      >
        {form?.editing ? null : (
          <Field
            label={t('positionCode')}
            value={code}
            onChange={(event) => setCode(event.target.value.toUpperCase())}
            error={fields['code']}
            autoComplete="off"
            dir="ltr"
            required
            requiredLabel={tCommon('required')}
          />
        )}

        <Field
          label={t('positionTitleAr')}
          value={titleAr}
          onChange={(event) => setTitleAr(event.target.value)}
          error={fields['titleAr'] ?? fields['title']}
          lang="ar"
          dir="rtl"
          required
          requiredLabel={tCommon('required')}
        />

        <Field
          label={t('positionTitleEn')}
          value={titleEn}
          onChange={(event) => setTitleEn(event.target.value)}
          error={fields['titleEn']}
          lang="en"
          dir="ltr"
          required
          requiredLabel={tCommon('required')}
        />

        {form?.editing ? null : (
          <Field
            label={t('level')}
            type="number"
            value={level}
            onChange={(event) => setLevel(event.target.value)}
            error={fields['level']}
            hint={t('levelHint')}
            dir="ltr"
          />
        )}
      </FormDialog>
    </section>
  );
}
