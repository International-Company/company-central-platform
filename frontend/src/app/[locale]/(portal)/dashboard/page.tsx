import { getFormatter, getTranslations } from 'next-intl/server';
import { PageHeader } from '@/components/shared/page-header';
import type { StatusTone } from '@/components/shared/status-badge';
import { attentionItems, type AttentionLevel } from '@/features/dashboard/attention';
import {
  DashboardSummary,
  type AttentionLine,
  type OperationsFigure,
  type TaskLine,
} from '@/features/dashboard/dashboard-summary';
import { readMyPermissions } from '@/lib/my-permissions';
import { callPlatform, type PlatformResponse } from '@/lib/platform-client';
import type {
  EmployeeDto,
  JobSummaryDto,
  NotificationDto,
  MfaStatusDto,
  OrganizationUnitTreeDto,
  OutboxDepthDto,
  PagedResult,
  RoleDto,
  SecurityEventDto,
  UserDto,
  WebhookSubscriptionDto,
  WorkflowTaskDto,
} from '@/types/platform';

/**
 * What needs doing, what is waiting on this person, and how the Platform is.
 *
 * **It used to be four counts and four permanent instructions.** Those said the
 * same thing every day forever: the instructions never went away once followed,
 * and the counts described the size of the Platform rather than its state. A
 * screen that cannot change is one nobody opens twice.
 *
 * Now the top of the page is the set of conditions that are true right now, and
 * each disappears when it stops being true. The setup steps are among them, so
 * a Platform that has been configured stops being told how to configure itself.
 *
 * **Permissions decide what is read, not just what is shown.** The reads that
 * need a permission are not issued without it: an ordinary employee opening the
 * dashboard would otherwise generate a handful of refusals in the Platform's
 * own security log every time, which is noise in the one place noise is
 * expensive. What cannot be read is reported as unavailable rather than as
 * zero, because a zero nobody is allowed to see is a lie that looks like health.
 *
 * **Each figure is read with the caller's own permissions**, and every read is
 * marked `duringRender`: a page may not refresh the session, because rotating
 * the token here would spend it and then be unable to store the replacement.
 */
export default async function DashboardPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  const t = await getTranslations('dashboard');
  const tNav = await getTranslations('nav');
  const tOperations = await getTranslations('operations');
  const format = await getFormatter();

  const permissions = await readMyPermissions();
  const may = (permission: string) => permissions.includes(permission);

  const mayOperations = may('platform.operations.view');
  const mayIntegrations = may('platform.integrations.view');

  // The window for the security question. Everything older belongs on the
  // security screen, where somebody is looking on purpose.
  const since = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();

  // All in parallel: the slowest read decides the page. In sequence this would
  // be a dozen round trips to the Platform for one screen.
  const [
    tasks,
    unread,
    mfa,
    users,
    employees,
    units,
    roles,
    outbox,
    jobs,
    subscriptions,
    security,
  ] = await Promise.all([
    read<PagedResult<WorkflowTaskDto>>('/api/v1/me/tasks?page=1&pageSize=5'),
    read<PagedResult<NotificationDto>>('/api/v1/me/notifications?unreadOnly=true&page=1&pageSize=1'),
    read<MfaStatusDto>('/api/v1/me/mfa'),

    when(may('platform.users.view'), () =>
      read<PagedResult<UserDto>>('/api/v1/users?page=1&pageSize=1'),
    ),
    when(may('platform.organization.view'), () =>
      read<PagedResult<EmployeeDto>>('/api/v1/organization/employees?page=1&pageSize=1'),
    ),
    when(may('platform.organization.view'), () =>
      read<OrganizationUnitTreeDto[]>('/api/v1/organization/units/tree'),
    ),
    when(may('platform.roles.view'), () => read<RoleDto[]>('/api/v1/roles')),

    when(mayOperations, () => read<OutboxDepthDto>('/api/v1/platform/outbox')),
    when(mayOperations, () => read<JobSummaryDto[]>('/api/v1/platform/jobs')),
    when(mayIntegrations, () =>
      read<WebhookSubscriptionDto[]>('/api/v1/integrations/subscriptions'),
    ),
    when(may('platform.security.view'), () =>
      read<PagedResult<SecurityEventDto>>(
        `/api/v1/security/events?minimumSeverity=High&from=${encodeURIComponent(since)}&page=1&pageSize=1`,
      ),
    ),
  ]);

  const userCount = total(users);
  const employeeCount = total(employees);
  const unitCount = units?.data ? countUnits(units.data) : null;
  const failingJobs = jobs?.data ?? null;

  const attention: AttentionLine[] = attentionItems({
    unreadNotifications: total(unread),
    mfaActive: mfa?.data ? mfa.data.isActive : null,
    outbox: outbox?.data ?? null,
    jobs: failingJobs,
    subscriptions: subscriptions?.data ?? null,
    highSeverityEvents: total(security),
    users: userCount,
    employees: employeeCount,
    units: unitCount,
  }).map((item) => ({
    key: item.key,
    href: `/${locale}${item.path}`,
    text: t(`attention.${item.key}` as never, item.values as never),
    levelLabel: t(`level.${item.level}` as never),
    tone: tones[item.level],
  }));

  const taskLines: TaskLine[] | null = tasks.data
    ? tasks.data.items.map((task) => ({
        id: task.id,
        href: `/${locale}/tasks`,
        step: locale === 'ar' ? task.stepNameAr : task.stepNameEn,
        context: t('taskContext', {
          application: task.applicationCode,
          process: task.definitionCode,
        }),
        due: task.dueAt
          ? t('due', { date: format.dateTime(new Date(task.dueAt), { dateStyle: 'medium' }) })
          : null,
        overdue: task.isOverdue,
      }))
    : null;

  const operations: OperationsFigure[] | null = mayOperations
    ? [
        {
          label: tOperations('pending'),
          value: format.number(Number(outbox?.data?.pending ?? 0)),
        },
        {
          label: tOperations('deadLettered'),
          value: format.number(Number(outbox?.data?.deadLettered ?? 0)),
        },
        {
          label: t('failingJobsFigure'),
          value: format.number(
            (failingJobs ?? []).filter(
              (job) => job.lastOutcome === 'Failed' || Number(job.failingInstances) > 0,
            ).length,
          ),
        },
        ...(mayIntegrations
          ? [
              {
                label: t('suspendedFigure'),
                value: format.number(
                  (subscriptions?.data ?? []).filter(
                    (subscription) => subscription.suspendedAt !== null
                      && subscription.suspendedAt !== undefined,
                  ).length,
                ),
              },
            ]
          : []),
      ]
    : null;

  return (
    <>
      <PageHeader
        title={tNav('dashboard')}
        description={`${t('description')} ${t('readAt', {
          time: format.dateTime(new Date(), { timeStyle: 'short' }),
        })}`}
      />

      <DashboardSummary
        labels={{
          attention: t('attention.heading'),
          allClear: t('allClear'),
          tasks: t('tasks'),
          tasksAll: t('tasksAll'),
          noTasks: t('noTasks'),
          noDue: t('noDue'),
          overdue: t('overdue'),
          figures: t('figures'),
          operations: t('operations'),
          operationsAll: t('operationsAll'),
          unavailable: t('unavailable'),
          unavailableHint: t('unavailableHint'),
        }}
        attention={attention}
        tasks={taskLines}
        tasksHref={`/${locale}/tasks`}
        figures={[
          { href: `/${locale}/users`, label: t('users'), value: formatted(userCount, format) },
          {
            href: `/${locale}/employees`,
            label: t('employees'),
            value: formatted(employeeCount, format),
          },
          {
            href: `/${locale}/organization`,
            label: t('units'),
            value: formatted(unitCount, format),
          },
          {
            href: `/${locale}/roles`,
            label: t('roles'),
            value: formatted(roles?.data ? roles.data.length : null, format),
          },
        ]}
        operations={operations}
        operationsHref={`/${locale}/operations`}
      />
    </>
  );
}

const tones: Record<AttentionLevel, StatusTone> = {
  act: 'danger',
  watch: 'warning',
  setup: 'neutral',
};

function read<T>(path: string): Promise<PlatformResponse<T>> {
  return callPlatform<T>({ path, duringRender: true });
}

/**
 * The read, or nothing at all when the caller may not take it.
 *
 * Deliberately not "issue it and ignore the refusal": a refusal is recorded as
 * a security event, and a page that generates several on every load teaches
 * whoever reads that log to ignore it.
 */
function when<T>(
  allowed: boolean,
  take: () => Promise<PlatformResponse<T>>,
): Promise<PlatformResponse<T> | null> {
  return allowed ? take() : Promise.resolve(null);
}

/** A page's total, or null when the page was never read. */
function total(response: PlatformResponse<PagedResult<unknown>> | null): number | null {
  return response?.data ? Number(response.data.totalItems) : null;
}

function formatted(
  value: number | null,
  format: Awaited<ReturnType<typeof getFormatter>>,
): string | null {
  return value === null ? null : format.number(value);
}

function countUnits(units: readonly OrganizationUnitTreeDto[]): number {
  return units.reduce((sum, unit) => sum + 1 + countUnits(unit.children), 0);
}
