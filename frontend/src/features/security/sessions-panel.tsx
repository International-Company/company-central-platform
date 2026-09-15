'use client';

import { useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { StatusBadge } from '@/components/shared/status-badge';
import type { LoginAttemptDto, SessionDto } from '@/types/platform';
import { EmptyValue } from '@/components/shared/empty-value';

/**
 * Where this account is signed in, and what has been tried against it.
 *
 * **Both of these are the account holder's own security tools, not an
 * administrator's.** They need no permission because they show one account —
 * the caller's — and they reach the person who would notice something wrong
 * long before anyone else would.
 *
 * The failed attempts matter more than the successful ones. Someone seeing
 * sign-ins they did not make is the earliest signal available that their
 * account is being tried, and it arrives without anybody having to be watching
 * a log.
 *
 * There is no "end this session" control, because the Platform has no endpoint
 * for ending one session by id — only signing out, which ends the current one,
 * and changing the password, which ends all the others. So the panel says that
 * instead of offering a button that does not exist.
 */
export function SessionsPanel() {
  const t = useTranslations('security');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [sessions, setSessions] = useState<SessionDto[] | null>(null);
  const [attempts, setAttempts] = useState<LoginAttemptDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        const [sessionResponse, historyResponse] = await Promise.all([
          fetch('/api/me/sessions'),
          fetch('/api/me/login-history?count=20'),
        ]);

        if (!sessionResponse.ok || !historyResponse.ok) {
          setError(tErrors('generic'));

          return;
        }

        setSessions((await sessionResponse.json()) as SessionDto[]);
        setAttempts((await historyResponse.json()) as LoginAttemptDto[]);
      } catch {
        setError(tErrors('network'));
      }
    })();
  }, [tErrors]);

  const sessionColumns: Column<SessionDto>[] = [
    {
      key: 'device',
      header: t('sessionDevice'),
      render: (session) => (
        <span className="font-medium">
          {/* The raw user agent, truncated. Parsing it into "Chrome on Windows"
              means a table of guesses that are wrong for anyone on something
              unusual — and the person recognising their own device does not
              need it prettified. */}
          {session.userAgent ? session.userAgent.slice(0, 60) : <EmptyValue />}
          {session.isCurrent ? (
            <StatusBadge tone="success">{t('sessionCurrent')}</StatusBadge>
          ) : null}
        </span>
      ),
    },
    {
      key: 'address',
      header: t('sessionAddress'),
      render: (session) => session.ipAddress ?? <EmptyValue />,
      secondary: true,
    },
    {
      key: 'started',
      header: t('sessionStarted'),
      render: (session) =>
        format.dateTime(new Date(session.createdAt), { dateStyle: 'medium' }),
      secondary: true,
    },
    {
      key: 'lastSeen',
      header: t('sessionLastSeen'),
      render: (session) =>
        format.dateTime(new Date(session.lastActivityAt), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }),
    },
  ];

  const attemptColumns: Column<LoginAttemptDto>[] = [
    {
      key: 'result',
      header: t('historyResult'),
      render: (attempt) => (
        <StatusBadge tone={attempt.succeeded ? 'success' : 'danger'}>
          {attempt.succeeded ? t('historySucceeded') : t('historyFailed')}
        </StatusBadge>
      ),
    },
    {
      key: 'address',
      header: t('sessionAddress'),
      render: (attempt) => attempt.ipAddress ?? <EmptyValue />,
      secondary: true,
    },
    {
      key: 'when',
      header: t('historyWhen'),
      render: (attempt) =>
        format.dateTime(new Date(attempt.occurredAt), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }),
    },
  ];

  return (
    <>
      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <section className="mt-6">
        <h2 className="text-base font-semibold text-text">
          {t('sessionsTitle')}
        </h2>

        <p className="mb-3 mt-0.5 max-w-prose text-sm text-text-secondary">
          {t('sessionsDescription')}
        </p>

        {sessions ? (
          <DataTable
            columns={sessionColumns}
            rows={sessions}
            rowKey={(session) => session.id}
            caption={t('sessionsTitle')}
            labels={{
              noResults: t('noSessions'),
              noResultsDescription: t('noSessionsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
          />
        ) : null}
      </section>

      <section className="mt-6">
        <h2 className="text-base font-semibold text-text">
          {t('historyTitle')}
        </h2>

        <p className="mb-3 mt-0.5 max-w-prose text-sm text-text-secondary">
          {t('historyDescription')}
        </p>

        {attempts ? (
          <DataTable
            columns={attemptColumns}
            rows={attempts}
            rowKey={(attempt) =>
              `${attempt.occurredAt}-${attempt.ipAddress ?? 'none'}`
            }
            caption={t('historyTitle')}
            labels={{
              noResults: t('noHistory'),
              noResultsDescription: t('noHistoryDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
          />
        ) : null}
      </section>
    </>
  );
}
