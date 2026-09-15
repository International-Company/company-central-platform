'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import { usePermission } from '@/lib/permissions';
import type { PagedResult, SecurityEventDto } from '@/types/platform';
import { EmptyValue } from '@/components/shared/empty-value';

/**
 * What the Platform recorded about security.
 *
 * **Not the audit trail, and the difference is worth stating.** The audit trail
 * answers "who changed what"; this answers "what happened that security cares
 * about" — failed sign-ins, lockouts, second factors enrolled, elevations
 * confirmed and refused. They are separate logs because they have separate
 * readers and separate retention, and merging them would bury each in the other.
 *
 * Rendered only for someone holding `platform.security.view`. Hidden rather than
 * shown-and-refused, because a panel that always reports a failure teaches
 * people that failures are normal.
 */
export function SecurityEventsPanel() {
  const t = useTranslations('security');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const canView = usePermission('platform.security.view');

  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<SecurityEventDto> | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!canView) {
      return;
    }

    setError(null);

    try {
      const response = await fetch(
        `/api/security/events?page=${page}&pageSize=25`,
      );

      if (!response.ok) {
        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      setResult((await response.json()) as PagedResult<SecurityEventDto>);
    } catch {
      setError(tErrors('network'));
    }
  }, [canView, page, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!canView) {
    return null;
  }

  function severityTone(severity: string): StatusTone {
    switch (severity) {
      case 'Critical':
      case 'High':
        return 'danger';
      case 'Medium':
        return 'warning';
      case 'Low':
        return 'neutral';
      default:
        return 'neutral';
    }
  }

  const columns: Column<SecurityEventDto>[] = [
    {
      key: 'eventType',
      header: t('eventType'),
      render: (event) => <span className="font-medium">{event.eventType}</span>,
    },
    {
      key: 'severity',
      header: t('severity'),
      render: (event) => (
        // The word as well as the colour. Colour alone fails a colour-blind
        // reader and disappears entirely on paper (§9.8).
        <StatusBadge tone={severityTone(event.severity)}>
          {event.severity}
        </StatusBadge>
      ),
    },
    {
      key: 'user',
      header: t('eventUser'),
      render: (event) => event.username ?? <EmptyValue />,
    },
    {
      key: 'address',
      header: t('sessionAddress'),
      render: (event) => event.ipAddress ?? <EmptyValue />,
      secondary: true,
    },
    {
      key: 'when',
      header: t('eventWhen'),
      render: (event) =>
        format.dateTime(new Date(event.occurredAt), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }),
    },
  ];

  return (
    <section className="mt-6">
      <h2 className="text-base font-semibold text-text">{t('eventsTitle')}</h2>

      <p className="mb-3 mt-0.5 max-w-prose text-sm text-text-secondary">
        {t('eventsDescription')}
      </p>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {result ? (
        <>
          <DataTable
            columns={columns}
            rows={result.items}
            rowKey={(event) => event.id}
            caption={t('eventsTitle')}
            labels={{
              noResults: t('noEvents'),
              noResultsDescription: t('noEventsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
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
    </section>
  );
}
