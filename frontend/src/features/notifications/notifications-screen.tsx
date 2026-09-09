'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { PageHeader } from '@/components/shared/page-header';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge } from '@/components/shared/status-badge';
import { PreferencesPanel } from './preferences-panel';
import type { NotificationDto, PagedResult } from '@/types/platform';

/**
 * The person's own messages.
 *
 * **In-app only.** An email that was sent is not an item in an inbox, and
 * listing one would show the same message twice to everybody who receives both.
 *
 * The body is rendered as text rather than as markup. The Platform escapes every
 * substituted value before storing it, so the stored body is safe — but rendering
 * it as HTML here would mean one screen trusting an escaping decision made in
 * another module, and the day somebody adds a template that skips it, this is
 * where the script runs.
 */
export function NotificationsScreen() {
  const t = useTranslations('notifications');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [result, setResult] = useState<PagedResult<NotificationDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(
        `/api/me/notifications?page=${page}&pageSize=25&unreadOnly=${unreadOnly}`,
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setResult((await response.json()) as PagedResult<NotificationDto>);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [page, unreadOnly, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function markRead(notification: NotificationDto) {
    try {
      const response = await fetch(`/api/me/notifications/${notification.id}/read`, {
        method: 'POST',
      });

      if (response.ok) {
        await load();
      }
    } catch {
      setError(tErrors('network'));
    }
  }

  const columns: Column<NotificationDto>[] = [
    {
      key: 'subject',
      header: t('subject'),
      render: (notification) => (
        <div>
          <p
            className={
              notification.readAt === null
                ? 'font-semibold text-text'
                : 'font-medium text-text-secondary'
            }
          >
            {notification.subject}
          </p>

          {/* Text, not markup — see the note on this component. Truncated
              because an inbox is a list of what arrived, not a reading pane. */}
          <p className="mt-0.5 line-clamp-2 text-sm text-text-secondary">
            {notification.body.replace(/<[^>]*>/g, ' ').trim()}
          </p>
        </div>
      ),
    },
    {
      key: 'received',
      header: t('received'),
      render: (notification) =>
        format.dateTime(new Date(notification.createdAt), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }),
      secondary: true,
    },
    {
      key: 'read',
      header: t('status'),
      render: (notification) => (
        <StatusBadge tone={notification.readAt === null ? 'warning' : 'neutral'}>
          {notification.readAt === null ? t('unread') : t('read')}
        </StatusBadge>
      ),
    },
  ];

  return (
    <>
      <PageHeader title={t('title')} description={t('description')} />

      <div className="mb-4 flex items-center gap-2" data-print-hidden>
        <input
          id="unread-only"
          type="checkbox"
          checked={unreadOnly}
          onChange={(event) => {
            setPage(1);
            setUnreadOnly(event.target.checked);
          }}
          className="size-4 rounded border-border-strong"
        />

        <label htmlFor="unread-only" className="text-sm text-text">
          {t('unreadOnly')}
        </label>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {loading && !result ? (
        <p role="status" className="text-sm text-text-secondary">
          {tCommon('loading')}
        </p>
      ) : result ? (
        <>
          <DataTable
            columns={columns}
            rows={result.items}
            rowKey={(notification) => notification.id}
            caption={t('title')}
            labels={{
              noResults: t('noNotifications'),
              noResultsDescription: t('noNotificationsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
            rowActions={(notification) =>
              notification.readAt === null ? (
                <Button
                  variant="quiet"
                  size="sm"
                  onClick={() => void markRead(notification)}
                >
                  {t('markRead')}
                </Button>
              ) : null
            }
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

      <PreferencesPanel />
    </>
  );
}
