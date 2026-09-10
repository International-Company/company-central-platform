'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import type { JobRunDto } from '@/types/platform';

/**
 * Every recent run of one background job.
 *
 * **The summary column is the reason this is worth storing at all.** A history
 * that only recorded that a job ran would not tell a working sweep from one
 * whose query has quietly stopped matching anything — both look like a green
 * row every hour. "Removed 412 entries" and "removed 0" are different facts.
 *
 * The instance column matters as soon as there is more than one process: a job
 * failing on one instance and succeeding on the other is a fifty-per-cent
 * failure rate that averages away to nothing unless the rows say which machine
 * they came from.
 */
export function JobRunHistory({
  job,
  onClose,
}: {
  job: string;
  onClose: () => void;
}) {
  const t = useTranslations('operations');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [runs, setRuns] = useState<JobRunDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);

    try {
      const response = await fetch(
        `/api/operations/jobs/${encodeURIComponent(job)}/runs`,
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setRuns((await response.json()) as JobRunDto[]);
    } catch {
      setError(tErrors('network'));
    }
  }, [job, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  function toneFor(outcome: string): StatusTone {
    if (outcome === 'Succeeded') {
      return 'success';
    }

    return outcome === 'Failed' ? 'danger' : 'neutral';
  }

  const columns: Column<JobRunDto>[] = [
    {
      key: 'when',
      header: t('when'),
      render: (run) =>
        format.dateTime(new Date(run.startedAt), {
          dateStyle: 'short',
          timeStyle: 'medium',
        }),
    },
    {
      key: 'outcome',
      header: t('outcome'),
      render: (run) => (
        <StatusBadge tone={toneFor(run.outcome)}>
          {t(`outcomes.${run.outcome}` as never)}
        </StatusBadge>
      ),
    },
    {
      key: 'did',
      header: t('did'),
      render: (run) => (
        <span className="text-sm text-text-secondary">
          {run.error ?? run.summary ?? t('nothingToDo')}
        </span>
      ),
    },
    {
      key: 'duration',
      header: t('duration'),
      render: (run) => `${Math.round(Number(run.durationMs))} ms`,
      numeric: true,
      secondary: true,
    },
    {
      key: 'instance',
      header: t('instance'),
      render: (run) => (
        <span className="font-mono text-xs text-text-secondary">{run.instance}</span>
      ),
      secondary: true,
    },
  ];

  return (
    <section className="flex flex-col gap-4 rounded-md border border-border bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-text">{t('history')}</h2>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{job}</p>
        </div>

        <Button type="button" variant="secondary" onClick={onClose}>
          {tCommon('close')}
        </Button>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <DataTable
        columns={columns}
        rows={runs}
        rowKey={(run) => run.id}
        caption={t('history')}
        labels={{
          noResults: t('noRuns'),
          noResultsDescription: t('noRunsDescription'),
          sortAscending: tTable('sortAscending'),
          sortDescending: tTable('sortDescending'),
          actions: tCommon('actions'),
        }}
      />
    </section>
  );
}
