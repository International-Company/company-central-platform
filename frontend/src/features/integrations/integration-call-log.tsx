'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import type { IntegrationCallDto, PagedResult } from '@/types/platform';

/**
 * What the Platform sent to one provider, and what came back.
 *
 * **The payloads shown here were redacted before they were stored**, so what an
 * administrator reads is what the database holds. There is no unredacted copy
 * anywhere for the next export to find, which is why this screen can show the
 * bodies at all.
 */
export function IntegrationCallLog({
  providerCode,
  onClose,
}: {
  providerCode: string;
  onClose: () => void;
}) {
  const t = useTranslations('integrations');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [page, setPage] = useState(1);
  const [outcome, setOutcome] = useState('');
  const [result, setResult] = useState<PagedResult<IntegrationCallDto> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);

    try {
      const query = new URLSearchParams({
        providerCode,
        page: String(page),
        pageSize: '25',
      });

      if (outcome !== '') {
        query.set('outcome', outcome);
      }

      const response = await fetch(`/api/integrations/calls?${query.toString()}`);

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setResult((await response.json()) as PagedResult<IntegrationCallDto>);
    } catch {
      setError(tErrors('network'));
    }
  }, [providerCode, page, outcome, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  function toneFor(value: string): StatusTone {
    if (value === 'Succeeded') {
      return 'success';
    }

    if (value === 'Refused' || value === 'Blocked') {
      return 'warning';
    }

    return value === 'Pending' ? 'neutral' : 'danger';
  }

  const columns: Column<IntegrationCallDto>[] = [
    {
      key: 'when',
      header: t('when'),
      render: (call) =>
        format.dateTime(new Date(call.startedAt), {
          dateStyle: 'short',
          timeStyle: 'medium',
        }),
    },
    {
      key: 'endpoint',
      header: t('endpoint'),
      render: (call) => (
        <div>
          <p className="font-medium text-text">{call.endpointKey}</p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">
            {call.method} {call.path}
          </p>
        </div>
      ),
    },
    {
      key: 'outcome',
      header: t('outcome'),
      render: (call) => (
        <StatusBadge tone={toneFor(call.outcome)}>
          {t(`outcomes.${call.outcome}` as never)}
        </StatusBadge>
      ),
    },
    {
      key: 'status',
      header: t('httpStatus'),
      render: (call) =>
        call.statusCode === null || call.statusCode === undefined
          ? tCommon('none')
          : String(call.statusCode),
      numeric: true,
      secondary: true,
    },
    {
      key: 'attempts',
      header: t('attempts'),
      render: (call) => String(call.attempts),
      numeric: true,
      secondary: true,
    },
    {
      key: 'duration',
      header: t('duration'),
      render: (call) => `${call.durationMs} ms`,
      numeric: true,
      secondary: true,
    },
  ];

  return (
    <section className="flex flex-col gap-4 rounded-md border border-border bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-text">{t('callsFor')}</h2>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{providerCode}</p>
        </div>

        <Button type="button" variant="secondary" onClick={onClose}>
          {tCommon('close')}
        </Button>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <label className="flex max-w-64 flex-col gap-1.5 text-sm font-medium text-text">
        {t('outcome')}
        <select
          value={outcome}
          onChange={(event) => {
            setPage(1);
            setOutcome(event.target.value);
          }}
          className="rounded-md border border-border-strong bg-surface px-3 py-2 text-sm text-text"
        >
          <option value="">{t('allOutcomes')}</option>
          <option value="Succeeded">{t('outcomes.Succeeded')}</option>
          <option value="Refused">{t('outcomes.Refused')}</option>
          <option value="TimedOut">{t('outcomes.TimedOut')}</option>
          <option value="CircuitOpen">{t('outcomes.CircuitOpen')}</option>
          <option value="Blocked">{t('outcomes.Blocked')}</option>
          <option value="Failed">{t('outcomes.Failed')}</option>
        </select>
      </label>

      <DataTable
        columns={columns}
        rows={result?.items ?? []}
        rowKey={(call) => call.id}
        caption={t('callsFor')}
        labels={{
          noResults: t('noCalls'),
          noResultsDescription: t('noCallsDescription'),
          sortAscending: tTable('sortAscending'),
          sortDescending: tTable('sortDescending'),
          actions: tCommon('actions'),
        }}
        rowActions={(call) => (
          <button
            type="button"
            className="text-sm font-medium text-primary-700 hover:underline"
            onClick={() => setExpanded(expanded === call.id ? null : call.id)}
          >
            {expanded === call.id ? tCommon('close') : tCommon('viewDetails')}
          </button>
        )}
      />

      {expanded && result ? <CallDetail call={result.items.find((c) => c.id === expanded)} /> : null}

      {result ? (
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
      ) : null}
    </section>
  );
}

/**
 * One call in full.
 *
 * The correlation id is first, because it is the field an investigation starts
 * from — it retrieves the log line, the trace and the audit record for the
 * request that caused this call.
 */
function CallDetail({ call }: { call: IntegrationCallDto | undefined }) {
  const t = useTranslations('integrations');

  if (!call) {
    return null;
  }

  return (
    <dl className="flex flex-col gap-3 rounded-md border border-border bg-surface-sunken p-4 text-sm">
      <div>
        <dt className="font-medium text-text">{t('correlationId')}</dt>
        <dd className="mt-0.5 break-all font-mono text-xs text-text">{call.correlationId}</dd>
      </div>

      {call.failureReason ? (
        <div>
          <dt className="font-medium text-text">{t('failureReason')}</dt>
          <dd className="mt-0.5 text-text">{call.failureReason}</dd>
        </div>
      ) : null}

      <div>
        <dt className="font-medium text-text">{t('requestPayload')}</dt>
        <dd className="mt-0.5 overflow-x-auto rounded border border-border bg-surface p-2">
          <pre className="font-mono text-xs text-text">{call.requestPayload}</pre>
        </dd>
      </div>

      <div>
        <dt className="font-medium text-text">{t('responsePayload')}</dt>
        <dd className="mt-0.5 overflow-x-auto rounded border border-border bg-surface p-2">
          <pre className="font-mono text-xs text-text">{call.responsePayload}</pre>
        </dd>
      </div>
    </dl>
  );
}
