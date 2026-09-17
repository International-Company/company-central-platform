import Link from 'next/link';
import type { ReactNode } from 'react';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';

/**
 * The dashboard, without the reads behind it.
 *
 * Separate from the page so the design preview can render exactly this with
 * sample figures; the page needs the Platform and a signed-in administrator,
 * which no developer machine here has.
 *
 * **Two columns, because a dashboard is two different things.** What needs a
 * decision is prose, and prose is read; what the Platform holds is reference,
 * and reference is looked up. Stacking them made one column down the middle of
 * a wide screen, four bands tall, each the same weight as the last, so the page
 * had no first thing to look at and took a scroll to finish. The decisions now
 * take the wide column and the reference stands beside them.
 *
 * **The biggest type on the page used to be the least useful.** The four counts
 * were set at three times the body size while the sentence saying event
 * delivery had stopped was set at the body size. A number that changes twice a
 * year does not earn that; they are a list of facts now, and the sentences are
 * the largest thing in the body.
 *
 * **Every condition says where it is dealt with.** The row ended in four
 * hundred pixels of nothing, and a reader could not tell where the link went
 * until they followed it.
 *
 * **Nothing here refreshes itself**, and the header says so with the time it
 * was read. A figure that silently ages is worse than one dated honestly.
 */

export interface DashboardFigure {
  href: string;
  label: string;

  /** Already formatted, or null when the caller may not read it. */
  value: string | null;
}

export interface AttentionLine {
  /** Stable across renders; the message key it was built from. */
  key: string;

  href: string;

  /** The condition as a sentence, with its number already in it. */
  text: string;

  /** The word on the badge: what kind of attention this asks for. */
  levelLabel: string;

  /** The screen this is dealt with on, named. */
  destination: string;

  tone: StatusTone;
}

export interface TaskLine {
  id: string;
  href: string;

  /** The step awaiting a decision, in the reader's language. */
  step: string;

  /** What it is about: the process, and the thing it concerns. */
  context: string;

  /** Already formatted, or null when the step has no deadline. */
  due: string | null;

  overdue: boolean;
}

export interface OperationsFigure {
  label: string;

  /** Already formatted. */
  value: string;
}

export interface DashboardLabels {
  attention: string;
  allClear: string;
  tasks: string;
  tasksAll: string;
  noTasks: string;
  noDue: string;
  overdue: string;
  figures: string;
  operations: string;
  operationsAll: string;
  unavailable: string;
  unavailableHint: string;
}

export function DashboardSummary({
  labels,
  attention,
  tasks,
  tasksHref,
  figures,
  operations,
  operationsHref,
}: {
  labels: DashboardLabels;
  attention: AttentionLine[];

  /** The first few only. Null when the reader has no approval inbox at all. */
  tasks: TaskLine[] | null;

  tasksHref: string;
  figures: DashboardFigure[];

  /** Null when the caller may not read the Platform's own machinery. */
  operations: OperationsFigure[] | null;

  operationsHref: string;
}) {
  return (
    // Two thirds and one third above the large breakpoint, one column below it.
    // The order in the document is the order on a phone, and it is already the
    // order of usefulness: what needs doing, what is waiting, then the counts.
    <div className="grid gap-x-12 gap-y-10 lg:grid-cols-3">
      <div className="lg:col-span-2">
        <Section heading={labels.attention} id="attention">
          {attention.length === 0 ? (
            <p className="py-4 text-sm text-text-secondary">{labels.allClear}</p>
          ) : (
            <ul className="divide-y divide-border border-b border-border">
              {attention.map((line) => (
                <li key={line.key}>
                  <Link
                    href={line.href}
                    className="group flex flex-wrap items-baseline gap-x-4 gap-y-1 py-3.5"
                  >
                    {/* A fixed column so the sentences start on one line rather
                        than stepping in and out with the length of each word. */}
                    <span className="w-20 shrink-0">
                      <StatusBadge tone={line.tone}>{line.levelLabel}</StatusBadge>
                    </span>

                    <span className="text-[0.9375rem] leading-relaxed text-text group-hover:text-primary-900 group-hover:underline underline-offset-4">
                      {line.text}
                    </span>

                    {/* Where this is dealt with. The row used to end in nothing,
                        and nothing is what it told the reader about where the
                        link would take them.

                        Directly after the sentence rather than aligned to the
                        end of the row: the sentences are of very different
                        lengths, and a column of destinations left a different
                        sized hole in every row. In brackets, like the
                        "(Required)" beside a field label, so it reads as a note
                        about the sentence rather than as part of it. */}
                    <span className="text-xs text-text-muted">({line.destination})</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Section>

        {tasks === null ? null : (
          <Section
            heading={labels.tasks}
            id="tasks"
            action={{ href: tasksHref, label: labels.tasksAll }}
            className="mt-10"
          >
            {tasks.length === 0 ? (
              <p className="py-4 text-sm text-text-secondary">{labels.noTasks}</p>
            ) : (
              <ul className="divide-y divide-border border-b border-border">
                {tasks.map((task) => (
                  <li
                    key={task.id}
                    className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1 py-3.5"
                  >
                    <div className="min-w-0">
                      <Link
                        href={task.href}
                        className="text-[0.9375rem] font-medium text-text hover:text-primary-900 hover:underline underline-offset-4"
                      >
                        {task.step}
                      </Link>

                      <p className="mt-0.5 text-xs text-text-secondary">{task.context}</p>
                    </div>

                    <div className="flex shrink-0 items-baseline gap-3 text-xs text-text-secondary">
                      <span data-numeric>{task.due ?? labels.noDue}</span>

                      {task.overdue ? (
                        <StatusBadge tone="danger">{labels.overdue}</StatusBadge>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </Section>
        )}
      </div>

      {/* Reference, beside the decisions rather than below them. */}
      <div>
        <Section heading={labels.figures} id="figures" quiet>
          <ul className="divide-y divide-border border-b border-border">
            {figures.map((figure) => (
              <li key={figure.href}>
                <Link
                  href={figure.href}
                  className="flex items-baseline justify-between gap-4 py-2.5 hover:text-primary-900"
                >
                  <span className="text-sm text-text-secondary">{figure.label}</span>

                  {figure.value === null ? (
                    <span className="text-sm text-text-muted" title={labels.unavailableHint}>
                      {labels.unavailable}
                    </span>
                  ) : (
                    <span className="text-base font-semibold text-text" data-numeric>
                      {figure.value}
                    </span>
                  )}
                </Link>
              </li>
            ))}
          </ul>
        </Section>

        {operations === null ? null : (
          <Section
            heading={labels.operations}
            id="operations"
            action={{ href: operationsHref, label: labels.operationsAll }}
            className="mt-10"
            quiet
          >
            <ul className="divide-y divide-border border-b border-border">
              {operations.map((figure) => (
                <li
                  key={figure.label}
                  className="flex items-baseline justify-between gap-4 py-2.5"
                >
                  <span className="text-sm text-text-secondary">{figure.label}</span>

                  <span className="text-base font-semibold text-text" data-numeric>
                    {figure.value}
                  </span>
                </li>
              ))}
            </ul>
          </Section>
        )}
      </div>
    </div>
  );
}

/**
 * A band of the dashboard: a heading, a rule, and whatever it holds.
 *
 * One shape for all four, so the screen reads as a sequence of statements
 * rather than as a collection of panels with their own ideas about spacing.
 * `quiet` is the reference column, whose headings should not compete with the
 * ones over the decisions.
 */
function Section({
  heading,
  id,
  action,
  className = '',
  quiet = false,
  children,
}: {
  heading: string;
  id: string;
  action?: { href: string; label: string };
  className?: string;
  quiet?: boolean;
  children: ReactNode;
}) {
  return (
    <section aria-labelledby={`${id}-heading`} className={className}>
      <div className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1 border-b border-border pb-2.5">
        <h2
          id={`${id}-heading`}
          className={
            quiet
              ? 'text-sm font-semibold text-text-secondary'
              : 'text-base font-semibold text-text'
          }
        >
          {heading}
        </h2>

        {action ? (
          <Link
            href={action.href}
            className="text-xs text-primary-700 hover:text-primary-900 hover:underline underline-offset-4"
          >
            {action.label}
          </Link>
        ) : null}
      </div>

      {children}
    </section>
  );
}
