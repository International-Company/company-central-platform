'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import type { SettingChangeDto } from '@/types/platform';

/**
 * What one setting was, what it became, who changed it and why.
 *
 * **"What is it now" is never the question being asked** when a behaviour
 * changed and nobody remembers doing it. This screen exists so that the answer
 * is a page somebody opens rather than a query somebody writes during the
 * incident.
 *
 * For a sensitive setting both values read as `[sensitive]` — the Platform
 * records that it changed and not what to, because a value that cannot be read
 * back through the API but sits in plain sight in its own history has not been
 * protected.
 */
export function SettingHistory({
  settingKey,
  onClose,
}: {
  settingKey: string;
  onClose: () => void;
}) {
  const t = useTranslations('configuration');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [changes, setChanges] = useState<SettingChangeDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);

    try {
      const response = await fetch(
        `/api/configuration/settings/${encodeURIComponent(settingKey)}/history`,
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setChanges((await response.json()) as SettingChangeDto[]);
    } catch {
      setError(tErrors('network'));
    }
  }, [settingKey, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: Column<SettingChangeDto>[] = [
    {
      key: 'when',
      header: t('when'),
      render: (change) =>
        format.dateTime(new Date(change.changedAt), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }),
    },
    {
      key: 'from',
      header: t('from'),
      render: (change) => (
        <span className="font-mono text-sm text-text">
          {/* Null means the setting was previously unset, which is a different
              fact from "it was empty" and worth telling apart. */}
          {change.oldValue ?? t('wasUnset')}
        </span>
      ),
    },
    {
      key: 'to',
      header: t('to'),
      render: (change) => (
        <span className="font-mono text-sm text-text">
          {change.newValue ?? t('cleared')}
        </span>
      ),
    },
    {
      key: 'scope',
      header: t('scope'),
      render: (change) => change.scope,
      secondary: true,
    },
    {
      key: 'reason',
      header: t('reason'),
      render: (change) => change.reason ?? '',
      secondary: true,
    },
  ];

  return (
    <section className="flex flex-col gap-4 rounded-md border border-border bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-text">{t('history')}</h2>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{settingKey}</p>
        </div>

        <Button type="button" variant="secondary" onClick={onClose}>
          {tCommon('close')}
        </Button>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <DataTable
        columns={columns}
        rows={changes}
        rowKey={(change) => `${change.changedAt}-${change.scope}`}
        caption={t('history')}
        labels={{
          noResults: t('neverChanged'),
          noResultsDescription: t('neverChangedDescription'),
          sortAscending: tTable('sortAscending'),
          sortDescending: tTable('sortDescending'),
          actions: tCommon('actions'),
        }}
      />
    </section>
  );
}
