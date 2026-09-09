'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { FormDialog } from '@/components/shared/form-dialog';
import { PageHeader } from '@/components/shared/page-header';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge } from '@/components/shared/status-badge';
import type {
  PagedResult,
  ProblemResponse,
  WorkflowTaskDto,
} from '@/types/platform';

/**
 * The approval inbox.
 *
 * **The one screen in the Platform most people will ever use.** Everything else
 * here is administration; this is where an ordinary employee meets the system,
 * so it shows the four things a decision needs — what the request is, which step
 * it is at, when it arrived, and when it is due — and nothing else.
 *
 * Sorted by deadline, soonest first, with the undated last. An inbox ordered by
 * arrival buries the item that has been waiting longest, which is the one most
 * likely to be late.
 *
 * The buttons offered are the ones the process actually permits at this step,
 * sent with each row. Showing every verb the engine knows would mean offering a
 * "Reject" that the process does not allow — a refusal waiting to happen, and
 * the person would reasonably conclude the system is broken.
 */
export function TasksScreen() {
  const t = useTranslations('workflow');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();
  const locale = useLocale();

  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<WorkflowTaskDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [acting, setActing] = useState<WorkflowTaskDto | null>(null);
  const [action, setAction] = useState('Approve');
  const [comment, setComment] = useState('');
  const [delegateTo, setDelegateTo] = useState('');
  const [busy, setBusy] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`/api/me/tasks?page=${page}&pageSize=25`);

      if (!response.ok) {
        if (response.status === 401) {
          window.location.href = `/${locale}/login`;

          return;
        }

        setError(tErrors('generic'));

        return;
      }

      setResult((await response.json()) as PagedResult<WorkflowTaskDto>);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [page, locale, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (acting) {
      setAction(acting.allowedActions[0] ?? 'Comment');
      setComment('');
      setDelegateTo('');
      setDialogError(null);
    }
  }, [acting]);

  async function submit() {
    if (!acting) {
      return;
    }

    setBusy(true);
    setDialogError(null);

    try {
      const response = await fetch(`/api/me/tasks/${acting.id}/actions`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          action,
          comment: comment || null,
          delegateToUserId: action === 'Delegate' ? delegateTo || null : null,
        }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;

        // The Platform's own message, when it has one. "This task is not
        // assigned to you" and "somebody may have acted already" are the two
        // most likely refusals here, and both tell the person something a
        // generic banner would not.
        setDialogError(
          body.detail ?? body.errors?.[0]?.message ?? tErrors('generic'),
        );

        return;
      }

      setActing(null);
      await load();
    } catch {
      setDialogError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  function actionLabel(name: string): string {
    switch (name) {
      case 'Approve':
        return t('actionApprove');
      case 'Reject':
        return t('actionReject');
      case 'Return':
        return t('actionReturn');
      case 'Delegate':
        return t('actionDelegate');
      case 'Comment':
        return t('actionComment');
      case 'Cancel':
        return t('actionCancel');
      default:
        return name;
    }
  }

  const columns: Column<WorkflowTaskDto>[] = [
    {
      key: 'resource',
      header: t('resource'),
      render: (task) => (
        <span className="font-medium">
          {/* The type and id the application supplied. The Platform has no idea
              what they mean, so it shows them exactly as given rather than
              inventing a label it cannot justify. */}
          {task.resourceType} · {task.resourceId}
        </span>
      ),
    },
    {
      key: 'step',
      header: t('step'),
      render: (task) => (locale === 'ar' ? task.stepNameAr : task.stepNameEn),
    },
    {
      key: 'assignedAt',
      header: t('assignedAt'),
      render: (task) =>
        format.dateTime(new Date(task.assignedAt), { dateStyle: 'medium' }),
      secondary: true,
    },
    {
      key: 'dueAt',
      header: t('dueAt'),
      render: (task) =>
        task.dueAt === null ? (
          <span className="text-text-secondary">{t('noDueDate')}</span>
        ) : task.isOverdue ? (
          // The word as well as the colour, as everywhere else: colour alone
          // fails a colour-blind reader and disappears on paper.
          <StatusBadge tone="danger">{t('overdue')}</StatusBadge>
        ) : (
          format.dateTime(new Date(task.dueAt), { dateStyle: 'medium' })
        ),
    },
  ];

  return (
    <>
      <PageHeader title={t('title')} description={t('description')} />

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
            rowKey={(task) => task.id}
            caption={t('title')}
            labels={{
              noResults: t('noTasks'),
              noResultsDescription: t('noTasksDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
            rowActions={(task) => (
              <Button variant="quiet" size="sm" onClick={() => setActing(task)}>
                {t('act')}
              </Button>
            )}
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

      <FormDialog
        open={acting !== null}
        title={t('actTitle')}
        description={t('actDescription')}
        submitLabel={tCommon('save')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={dialogError}
        onSubmit={() => void submit()}
        onCancel={() => setActing(null)}
      >
        <div className="flex flex-col gap-1.5">
          <label htmlFor="task-action" className="text-sm font-medium text-text">
            {t('action')}
          </label>

          <select
            id="task-action"
            value={action}
            onChange={(event) => setAction(event.target.value)}
            className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
          >
            {(acting?.allowedActions ?? []).map((name) => (
              <option key={name} value={name}>
                {actionLabel(name)}
              </option>
            ))}
          </select>
        </div>

        {action === 'Delegate' ? (
          <Field
            label={t('delegateTo')}
            value={delegateTo}
            onChange={(event) => setDelegateTo(event.target.value)}
            dir="ltr"
            required
            requiredLabel={tCommon('required')}
          />
        ) : null}

        <Field
          label={t('comment')}
          value={comment}
          onChange={(event) => setComment(event.target.value)}
          hint={t('commentHint')}
        />
      </FormDialog>
    </>
  );
}
