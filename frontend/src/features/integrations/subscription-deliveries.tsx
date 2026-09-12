'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import type { PagedResult, WebhookDeliveryDto } from '@/types/platform';

/**
 * What happened to the events one subscription was meant to receive.
 *
 * **The answer to "did you send it?"**, which is the first thing asked when a
 * business system's state disagrees with the Platform's. A delivery mechanism
 * that kept no record could only answer with an opinion.
 *
 * An abandoned row is kept rather than swept: "we tried six times over an hour
 * and your endpoint refused every one" is the sentence somebody needs, and it
 * cannot be said by a table that deletes its own failures.
 */
export function SubscriptionDeliveries({
  subscriptionId,
  subscriptionName,
  onClose,
}: {
  subscriptionId: string;
  subscriptionName: string;
  onClose: () => void;
}) {
  const t = useTranslations('integrations');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<WebhookDeliveryDto> | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);

    try {
      const query = new URLSearchParams({ page: String(page), pageSize: '25' });

      const response = await fetch(
        `/api/integrations/subscriptions/${subscriptionId}/deliveries?${query.toString()}`,
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setResult((await response.json()) as PagedResult<WebhookDeliveryDto>);
    } catch {
      setError(tErrors('network'));
    }
  }, [subscriptionId, page, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  /**
   * Pending is neutral rather than amber.
   *
   * A delivery waiting for its next attempt is the system working, not the
   * system worrying — and painting every queued row amber teaches people to
   * ignore the colour.
   */
  function toneFor(status: string): StatusTone {
    if (status === 'Delivered') {
      return 'success';
    }

    return status === 'Abandoned' ? 'danger' : 'neutral';
  }

  const columns: Column<WebhookDeliveryDto>[] = [
    {
      key: 'event',
      header: t('deliveryEvent'),
      render: (delivery) => (
        <div>
          <p className="font-mono text-sm text-text">{delivery.eventType}</p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{delivery.eventId}</p>
        </div>
      ),
    },
    {
      key: 'status',
      header: t('statusHeading'),
      render: (delivery) => (
        <StatusBadge tone={toneFor(delivery.status)}>
          {t(`deliveryStatuses.${delivery.status}` as never)}
        </StatusBadge>
      ),
    },
    {
      key: 'attempts',
      header: t('attempts'),
      render: (delivery) => String(delivery.attempts),
      numeric: true,
      secondary: true,
    },
    {
      key: 'response',
      header: t('deliveryResponse'),
      render: (delivery) => (
        <span className="text-sm text-text-secondary">
          {delivery.lastError ?? (delivery.responseStatusCode ?? tCommon('none'))}
        </span>
      ),
    },
    {
      key: 'when',
      header: t('when'),
      render: (delivery) =>
        format.dateTime(new Date(delivery.deliveredAt ?? delivery.createdAt), {
          dateStyle: 'short',
          timeStyle: 'short',
        }),
      secondary: true,
    },
  ];

  return (
    <section className="rounded-lg border border-border bg-surface p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-text">
          {t('deliveriesFor', { name: subscriptionName })}
        </h2>

        <Button variant="secondary" onClick={onClose}>
          {tCommon('close')}
        </Button>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <DataTable
        columns={columns}
        rows={result?.items ?? []}
        rowKey={(delivery) => delivery.id}
        caption={t('deliveriesFor', { name: subscriptionName })}
        labels={{
          noResults: t('noDeliveries'),
          noResultsDescription: t('noDeliveriesDescription'),
          sortAscending: tTable('sortAscending'),
          sortDescending: tTable('sortDescending'),
          actions: tCommon('actions'),
        }}
      />

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
