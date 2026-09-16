'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, EmptyState, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import type { AuditEventDto, PagedResult } from '@/types/platform';
import { EmptyValue } from '@/components/shared/empty-value';

/**
 * Audit search.
 *
 * **Nothing is fetched until somebody asks**, and no search is ever unbounded.
 * That mirrors the Platform, which requires a range: an unbounded query over a
 * table designed to grow forever is an outage caused by someone opening a
 * screen, and an investigator who omitted the dates would otherwise believe
 * they had searched everything — which is worse than an error.
 *
 * **The fields start on the last seven days** rather than empty. The reason
 * for the empty screen was that an implicit range is a lie; a range written
 * into the two boxes in front of the reader is not implicit, and it can be
 * changed before anything is searched. What it replaces is a screen that
 * opened as two blank boxes in a format the browser chooses and this
 * application cannot set, which is not a question most people can answer.
 */
function daysAgo(days: number): Date {
  const when = new Date();

  when.setDate(when.getDate() - days);

  return when;
}

/**
 * A local date and time in the form `datetime-local` accepts.
 *
 * Not `toISOString`, which converts to UTC: in a country three hours ahead
 * that would put the default range three hours out, and the reader would have
 * no way of telling from looking at it.
 */
function localInput(when: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');

  return `${when.getFullYear()}-${pad(when.getMonth() + 1)}-${pad(when.getDate())}`
    + `T${pad(when.getHours())}:${pad(when.getMinutes())}`;
}

export function AuditScreen() {
  const t = useTranslations('audit');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  // Seven days back to now, in the format the control takes. Computed once on
  // mount rather than at module load, which would freeze the range at whenever
  // the bundle was first evaluated.
  const [from, setFrom] = useState(() => localInput(daysAgo(7)));
  const [to, setTo] = useState(() => localInput(new Date()));

  const [result, setResult] = useState<PagedResult<AuditEventDto> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    void search(1);
  }

  async function search(targetPage: number) {
    if (!from || !to) {
      setError(t('dateRangeRequired'));

      return;
    }

    setLoading(true);
    setError(null);

    try {
      const params = new URLSearchParams({
        from: new Date(from).toISOString(),
        to: new Date(to).toISOString(),
        page: String(targetPage),
        pageSize: '25',
      });

      const response = await fetch(`/api/audit/events?${params.toString()}`);

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as {
          code?: string;
        };

        // The Platform's own reason, translated. A generic message here would
        // leave a user narrowing a range they had no way to know was too wide.
        setError(
          body.code === 'AUDIT.DATE_RANGE_TOO_WIDE'
            ? t('dateRangeTooWide')
            : response.status === 403
              ? tErrors('forbidden')
              : tErrors('generic'),
        );

        return;
      }

      setResult((await response.json()) as PagedResult<AuditEventDto>);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }

  const columns: Column<AuditEventDto>[] = [
    {
      key: 'occurredAt',
      header: t('occurredAt'),
      render: (event) =>
        format.dateTime(new Date(event.occurredAt), {
          dateStyle: 'short',
          timeStyle: 'medium',
        }),
    },
    {
      key: 'actor',
      header: t('actor'),
      // The username, which is stored beside the id precisely so this column
      // stays readable after someone's record changes.
      render: (event) => event.actorUsername ?? <EmptyValue />,
    },
    {
      key: 'action',
      header: t('action'),
      render: (event) => (
        <span className="font-medium">{event.action}</span>
      ),
    },
    {
      key: 'module',
      header: t('module'),
      render: (event) => `${event.application} / ${event.module}`,
      secondary: true,
    },
    {
      key: 'resource',
      header: t('resource'),
      render: (event) =>
        event.resourceType ? `${event.resourceType} ${event.resourceId ?? ''}` : <EmptyValue />,
      secondary: true,
    },
    {
      key: 'result',
      header: t('result'),
      render: (event) => (
        <StatusBadge tone={resultTone(event.result)}>
          {resultLabel(event.result)}
        </StatusBadge>
      ),
    },
  ];

  function resultTone(result: string): StatusTone {
    switch (result) {
      case 'Success':
        return 'success';
      case 'Denied':
        return 'danger';
      case 'Failure':
        return 'warning';
      default:
        return 'neutral';
    }
  }

  function resultLabel(result: string): string {
    switch (result) {
      case 'Success':
        return t('resultSuccess');
      case 'Denied':
        return t('resultDenied');
      case 'Failure':
        return t('resultFailure');
      default:
        return result;
    }
  }

  return (
    <>
      <PageHeader title={t('title')} description={t('description')} />

      <form
        onSubmit={handleSubmit}
        className="mb-4 flex flex-wrap items-end gap-3"
        role="search"
        data-print-hidden
      >
        <div className="w-full max-w-[13rem]">
          <Field
            label={t('from')}
            type="datetime-local"
            value={from}
            onChange={(event) => setFrom(event.target.value)}
            required
            requiredLabel={tCommon('required')}
          />
        </div>

        <div className="w-full max-w-[13rem]">
          <Field
            label={t('to')}
            type="datetime-local"
            value={to}
            onChange={(event) => setTo(event.target.value)}
            required
            requiredLabel={tCommon('required')}
          />
        </div>

        <Button
          type="submit"
          variant="primary"
          busy={loading}
          busyLabel={tCommon('loading')}
        >
          {tCommon('search')}
        </Button>
      </form>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {result ? (
        <>
          <DataTable
            columns={columns}
            rows={result.items}
            rowKey={(event) => event.id}
            caption={t('title')}
            labels={{
              noResults: tTable('noResults'),
              noResultsDescription: tTable('noResultsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
          />

          <Pagination
            page={result.page}
            pageSize={result.pageSize}
            totalItems={result.totalItems}
            onPageChange={(next) => void search(next)}
            labels={{
              showing: (values) => tTable('showing', values),
              previous: tTable('previous'),
              next: tTable('next'),
            }}
          />
        </>
      ) : (
        !error && (
          <EmptyState
            title={t('notSearchedYet')}
            description={t('notSearchedYetDescription')}
          />
        )
      )}
    </>
  );
}
