'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import { JobRunHistory } from './job-run-history';
import type { JobSummaryDto, OutboxDepthDto } from '@/types/platform';

/**
 * Whether the Platform's own machinery is working.
 *
 * **This is the page that replaces a query written during an incident.** Until
 * now a background sweep emitted a metric and stored nothing, so "did last
 * night's purge run?" was answered by somebody with database access at the
 * moment they could least afford to be writing SQL.
 *
 * **It is not a business dashboard.** Nothing here counts anything a company
 * does; it counts sweeps, failures and undelivered events. Business reporting is
 * outside the Platform by design.
 */
export function OperationsScreen() {
  const t = useTranslations('operations');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [jobs, setJobs] = useState<JobSummaryDto[]>([]);
  const [outbox, setOutbox] = useState<OutboxDepthDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);

  // When this was read, not when the component last rendered. Nothing on the
  // page refreshes itself, and an operator watching during an incident needs
  // the stamp to go stale in front of them rather than track the clock.
  const [readAt, setReadAt] = useState<Date | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const [jobsResponse, outboxResponse] = await Promise.all([
        fetch('/api/operations/jobs'),
        fetch('/api/operations/outbox'),
      ]);

      if (!jobsResponse.ok) {
        setError(tErrors('generic'));

        return;
      }

      setJobs((await jobsResponse.json()) as JobSummaryDto[]);

      if (outboxResponse.ok) {
        setOutbox((await outboxResponse.json()) as OutboxDepthDto);
      }

      setReadAt(new Date());
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  /**
   * The tone for a job's last run.
   *
   * A cancelled run is neutral, not red. It means the host was stopping, which
   * is what a deployment looks like from in here — painting every release red
   * teaches people to ignore the colour.
   */
  function toneFor(outcome: string): StatusTone {
    if (outcome === 'Succeeded') {
      return 'success';
    }

    return outcome === 'Failed' ? 'danger' : 'neutral';
  }

  const columns: Column<JobSummaryDto>[] = [
    {
      key: 'job',
      header: t('job'),
      render: (job) => (
        <div>
          <p className="font-mono text-sm font-medium text-text">{job.job}</p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{job.lastInstance}</p>
        </div>
      ),
    },
    {
      key: 'outcome',
      header: t('lastRun'),
      render: (job) => (
        <StatusBadge tone={toneFor(job.lastOutcome)}>
          {t(`outcomes.${job.lastOutcome}` as never)}
        </StatusBadge>
      ),
    },
    {
      key: 'when',
      header: t('when'),
      render: (job) =>
        format.dateTime(new Date(job.lastStartedAt), {
          dateStyle: 'short',
          timeStyle: 'short',
        }),
    },
    {
      key: 'did',
      header: t('did'),
      render: (job) => (
        // What the pass actually did, in the job's own words. A history that
        // only says a job ran cannot tell a working sweep from one finding
        // nothing because its query is wrong.
        <span className="text-sm text-text-secondary">
          {job.lastError ?? job.lastSummary ?? t('nothingToDo')}
        </span>
      ),
    },
    {
      key: 'recent',
      header: t('recent'),
      render: (job) => `${Number(job.recentFailures)} / ${Number(job.recentRuns)}`,
      numeric: true,
      secondary: true,
    },
    {
      key: 'duration',
      header: t('duration'),
      // The average over the window rather than the last run: one slow pass is
      // weather, and a job that has doubled since Tuesday is the thing to see.
      render: (job) => `${Math.round(Number(job.averageDurationMs))} ms`,
      numeric: true,
      secondary: true,
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('title')} description={t('description')} />

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {outbox ? <OutboxPanel depth={outbox} /> : null}

      <section>
        <h2 className="mb-3 text-base font-semibold text-text">{t('jobs')}</h2>

        {loading && jobs.length === 0 ? (
          <p className="text-sm text-text-secondary">{tCommon('loading')}</p>
        ) : (
          <DataTable
            columns={columns}
            rows={jobs}
            rowKey={(job) => job.job}
            caption={t('jobs')}
            labels={{
              noResults: t('noJobs'),
              noResultsDescription: t('noJobsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
            rowActions={(job) => (
              <button
                type="button"
                className="text-sm font-medium text-primary-700 hover:underline"
                onClick={() => setSelected(selected === job.job ? null : job.job)}
              >
                {t('history')}
              </button>
            )}
          />
        )}
      </section>

      {selected ? (
        <JobRunHistory job={selected} onClose={() => setSelected(null)} />
      ) : null}

      {readAt ? (
        <p className="text-sm text-text-secondary">
          {t('readAt', {
            time: format.dateTime(readAt, { timeStyle: 'medium' }),
          })}
        </p>
      ) : null}
    </div>
  );
}

/**
 * How far behind event delivery is.
 *
 * **The age of the oldest undelivered message is the figure that matters**, and
 * it is the one given the most room. A depth of four hundred is either a busy
 * minute or a relay that stopped on Sunday, and only the age says which.
 */
function OutboxPanel({ depth }: { depth: OutboxDepthDto }) {
  const t = useTranslations('operations');
  const tCommon = useTranslations('common');
  const format = useFormatter();

  // The generated contract types every numeric as `number | string`, because
  // JSON can carry a non-finite double as a word. Coerced once here rather than
  // at each of the four places it is read.
  const seconds =
    depth.oldestPendingAgeSeconds === null || depth.oldestPendingAgeSeconds === undefined
      ? null
      : Number(depth.oldestPendingAgeSeconds);

  // Ten minutes. Below that a backlog is the relay working through a burst;
  // above it, something is not being delivered.
  const behind = seconds !== null && seconds > 600;

  return (
    <section className="rounded-md border border-border bg-surface p-4">
      <h2 className="text-base font-semibold text-text">{t('outbox')}</h2>

      <p className="mt-1 text-sm text-text-secondary">{t('outboxHint')}</p>

      <dl className="mt-3 grid gap-4 sm:grid-cols-3">
        <div>
          <dt className="text-sm text-text-secondary">{t('pending')}</dt>
          <dd className="mt-0.5 text-2xl font-semibold text-text">
            {format.number(Number(depth.pending))}
          </dd>
        </div>

        <div>
          <dt className="text-sm text-text-secondary">{t('oldestPending')}</dt>
          <dd className="mt-0.5 text-2xl font-semibold text-text">
            {seconds === null
              ? tCommon('none')
              : t('secondsOld', { seconds: Math.round(seconds) })}
          </dd>

          {behind ? (
            <p className="mt-1">
              <StatusBadge tone="warning">{t('behind')}</StatusBadge>
            </p>
          ) : null}
        </div>

        <div>
          <dt className="text-sm text-text-secondary">{t('deadLettered')}</dt>
          <dd className="mt-0.5 text-2xl font-semibold text-text">
            {format.number(Number(depth.deadLettered))}
          </dd>

          {Number(depth.deadLettered) > 0 ? (
            <p className="mt-1 text-xs text-text-secondary">{t('deadLetteredHint')}</p>
          ) : null}
        </div>
      </dl>
    </section>
  );
}
