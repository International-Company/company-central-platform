'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { PageHeader } from '@/components/shared/page-header';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import type {
  PagedResult,
  WorkflowDefinitionDto,
  WorkflowInstanceDto,
} from '@/types/platform';
import { EmptyValue } from '@/components/shared/empty-value';

/**
 * What is running, and what processes exist to run.
 *
 * **An administrator's view, and read-only on purpose.** Approvals are decided
 * by the people they are assigned to, on their own inbox; a screen that let an
 * administrator approve anything would be a way around the entire assignment
 * model, and the audit trail would show a decision by somebody who was never
 * asked.
 *
 * The processes below are registered by applications through the API, not
 * authored here. That is the point of definitions-as-data: a new approval
 * process for a future system needs no Platform code and no Platform screen.
 */
export function WorkflowScreen() {
  const t = useTranslations('workflow');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();
  const locale = useLocale();

  const [page, setPage] = useState(1);
  const [instances, setInstances] = useState<PagedResult<WorkflowInstanceDto> | null>(null);
  const [definitions, setDefinitions] = useState<WorkflowDefinitionDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);

    try {
      const [instanceResponse, definitionResponse] = await Promise.all([
        fetch(`/api/workflow/instances?page=${page}&pageSize=25`),
        fetch('/api/workflow/definitions'),
      ]);

      if (!instanceResponse.ok) {
        setError(
          instanceResponse.status === 403
            ? tErrors('forbidden')
            : tErrors('generic'),
        );

        return;
      }

      setInstances(
        (await instanceResponse.json()) as PagedResult<WorkflowInstanceDto>,
      );

      if (definitionResponse.ok) {
        setDefinitions((await definitionResponse.json()) as WorkflowDefinitionDto[]);
      }
    } catch {
      setError(tErrors('network'));
    }
  }, [page, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  function statusTone(status: string): StatusTone {
    switch (status) {
      case 'Approved':
        return 'success';
      case 'Rejected':
        return 'danger';
      case 'Cancelled':
        return 'neutral';
      default:
        return 'warning';
    }
  }

  function statusLabel(status: string): string {
    switch (status) {
      case 'Running':
        return t('statusRunning');
      case 'Approved':
        return t('statusApproved');
      case 'Rejected':
        return t('statusRejected');
      case 'Cancelled':
        return t('statusCancelled');
      default:
        return status;
    }
  }

  function assigneeLabel(strategy: string): string {
    switch (strategy) {
      case 'User':
        return t('assigneeUser');
      case 'Role':
        return t('assigneeRole');
      case 'Position':
        return t('assigneePosition');
      case 'RequesterManager':
        return t('assigneeRequesterManager');
      case 'UnitHead':
        return t('assigneeUnitHead');
      case 'SuppliedByCaller':
        return t('assigneeSuppliedByCaller');
      default:
        return strategy;
    }
  }

  const instanceColumns: Column<WorkflowInstanceDto>[] = [
    {
      key: 'resource',
      header: t('resource'),
      render: (instance) => (
        <span className="font-medium">
          {instance.resourceType}
          <span className="ms-3 font-normal text-text-secondary">{instance.resourceId}</span>
        </span>
      ),
    },
    {
      key: 'process',
      header: t('process'),
      render: (instance) =>
        `${instance.applicationCode}/${instance.definitionCode} v${instance.definitionVersion}`,
      secondary: true,
    },
    {
      key: 'step',
      header: t('step'),
      render: (instance) => instance.currentStepKey ?? <EmptyValue />,
    },
    {
      key: 'status',
      header: t('status'),
      render: (instance) => (
        <StatusBadge tone={statusTone(instance.status)}>
          {statusLabel(instance.status)}
        </StatusBadge>
      ),
    },
    {
      key: 'startedAt',
      header: t('startedAt'),
      render: (instance) =>
        format.dateTime(new Date(instance.startedAt), { dateStyle: 'medium' }),
      secondary: true,
    },
  ];

  const definitionColumns: Column<WorkflowDefinitionDto>[] = [
    {
      key: 'name',
      header: t('process'),
      render: (definition) => (
        <span className="font-medium">
          {locale === 'ar' ? definition.nameAr : definition.nameEn}
        </span>
      ),
    },
    {
      key: 'code',
      header: 'code',
      render: (definition) =>
        `${definition.applicationCode}/${definition.code}`,
    },
    {
      key: 'version',
      header: t('version'),
      render: (definition) => String(definition.version),
      numeric: true,
    },
    {
      key: 'steps',
      header: t('steps'),
      render: (definition) => (
        // The shape of the process in one cell: the steps in order and who each
        // waits for. An administrator asking "why did this go to Amira?" gets
        // the answer here rather than by reading a JSON document.
        <span className="text-xs text-text-secondary">
          {definition.steps
            .map(
              (step) =>
                `${locale === 'ar' ? step.nameAr : step.nameEn} (${assigneeLabel(step.assigneeStrategy)})`,
            )
            // Joined in words, not with an arrow: the order reads the same
            // way in both languages, and there is no symbol to mirror.
            .join(locale === 'ar' ? '، ثم ' : ', then ')}
        </span>
      ),
      secondary: true,
    },
    {
      key: 'status',
      header: t('status'),
      render: (definition) => (
        <StatusBadge tone={definition.status === 'Published' ? 'success' : 'neutral'}>
          {definition.status}
        </StatusBadge>
      ),
    },
  ];

  return (
    <>
      <PageHeader title={t('instancesTitle')} description={t('instancesDescription')} />

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {instances ? (
        <>
          <DataTable
            columns={instanceColumns}
            rows={instances.items}
            rowKey={(instance) => instance.id}
            caption={t('instancesTitle')}
            labels={{
              noResults: t('noInstances'),
              noResultsDescription: t('noInstancesDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
          />

          <Pagination
            page={instances.page}
            pageSize={instances.pageSize}
            totalItems={instances.totalItems}
            onPageChange={setPage}
            labels={{
              showing: (values) => tTable('showing', values),
              previous: tTable('previous'),
              next: tTable('next'),
            }}
          />
        </>
      ) : null}

      <section className="mt-8">
        <h2 className="text-base font-semibold text-text">
          {t('definitionsTitle')}
        </h2>

        <p className="mb-3 mt-0.5 max-w-prose text-sm text-text-secondary">
          {t('definitionsDescription')}
        </p>

        {definitions ? (
          <DataTable
            columns={definitionColumns}
            rows={definitions}
            rowKey={(definition) => definition.id}
            caption={t('definitionsTitle')}
            labels={{
              noResults: t('noDefinitions'),
              noResultsDescription: t('noDefinitionsDescription'),
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
