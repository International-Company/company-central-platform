'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import { usePermission } from '@/lib/permissions';
import type { NotificationDto, PagedResult } from '@/types/platform';
import { EmptyValue } from '@/components/shared/empty-value';

/**
 * What was sent, and how it went.
 *
 * **The point of this screen is the failures.** A notification that could not be
 * delivered stays here rather than disappearing (ARCHITECTURE.md §17.4) — a
 * message nobody received and nobody can see was never sent, and the person who
 * needed it finds out some other way, usually badly.
 *
 * The last provider response is shown because it is what an operator acts on:
 * "mailbox unavailable" and "connection timed out" mean different things and
 * lead to different fixes, and a row saying only "failed" leads to neither.
 */
export function DeliveryLogPanel() {
  const t = useTranslations('notifications');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const canView = usePermission('platform.notifications.view');

  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const [result, setResult] = useState<PagedResult<NotificationDto> | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!canView) {
      return;
    }

    setError(null);

    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '25' });

      if (status) {
        params.set('status', status);
      }

      const response = await fetch(`/api/notifications?${params.toString()}`);

      if (!response.ok) {
        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      setResult((await response.json()) as PagedResult<NotificationDto>);
    } catch {
      setError(tErrors('network'));
    }
  }, [canView, page, status, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!canView) {
    return null;
  }

  function statusTone(value: string): StatusTone {
    switch (value) {
      case 'Delivered':
        return 'success';
      case 'Failed':
        return 'danger';
      default:
        return 'warning';
    }
  }

  function statusLabel(value: string): string {
    switch (value) {
      case 'Pending':
        return t('statusPending');
      case 'Delivered':
        return t('statusDelivered');
      case 'Failed':
        return t('statusFailed');
      default:
        return value;
    }
  }

  const columns: Column<NotificationDto>[] = [
    {
      key: 'subject',
      header: t('subject'),
      render: (notification) => (
        <span className="font-medium">{notification.subject}</span>
      ),
    },
    {
      key: 'channel',
      header: t('channel'),
      render: (notification) =>
        notification.channel === 'InApp' ? t('channelInApp') : t('channelEmail'),
      secondary: true,
    },
    {
      key: 'status',
      header: t('status'),
      render: (notification) => (
        <StatusBadge tone={statusTone(notification.status)}>
          {statusLabel(notification.status)}
        </StatusBadge>
      ),
    },
    {
      key: 'attempts',
      header: t('attempts'),
      render: (notification) => String(notification.deliveries.length),
      numeric: true,
      secondary: true,
    },
    {
      key: 'lastResponse',
      header: t('lastResponse'),
      render: (notification) => {
        const last = notification.deliveries.at(-1);

        // What an operator acts on. "Mailbox unavailable" and "connection timed
        // out" lead to different fixes; a row saying only "failed" leads to
        // neither.
        return last?.providerResponse ? (
          <span className="text-xs text-text-secondary">{last.providerResponse}</span>
        ) : (
          <EmptyValue />
        );
      },
      secondary: true,
    },
    {
      key: 'created',
      header: t('received'),
      render: (notification) =>
        format.dateTime(new Date(notification.createdAt), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }),
    },
  ];

  return (
    <section className="mt-8">
      <h2 className="text-base font-semibold text-text">{t('logTitle')}</h2>

      <p className="mb-3 mt-0.5 max-w-prose text-sm text-text-secondary">
        {t('logDescription')}
      </p>

      <div className="mb-3 flex flex-col gap-1.5" data-print-hidden>
        <label htmlFor="delivery-status" className="text-sm font-medium text-text">
          {t('status')}
        </label>

        <select
          id="delivery-status"
          value={status}
          onChange={(event) => {
            setPage(1);
            setStatus(event.target.value);
          }}
          className="h-10 max-w-56 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
        >
          <option value="">{tCommon('filter')}</option>
          <option value="Failed">{t('statusFailed')}</option>
          <option value="Pending">{t('statusPending')}</option>
          <option value="Delivered">{t('statusDelivered')}</option>
        </select>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {result ? (
        <>
          <DataTable
            columns={columns}
            rows={result.items}
            rowKey={(notification) => notification.id}
            caption={t('logTitle')}
            labels={{
              noResults: t('noLog'),
              noResultsDescription: t('noLogDescription'),
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
