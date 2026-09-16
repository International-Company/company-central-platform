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
 * **Ordered by what the reader can do about it.** What needs a decision, then
 * the decisions waiting on them personally, then what the Platform holds, then
 * how its machinery is running. The counts used to come first, which put the
 * least actionable thing at the top of the screen.
 *
 * **One panel of figures divided by rules, not four cards in a grid.** A row of
 * equal tiles, each a big number in a rounded box, is the first thing a
 * generated dashboard produces; a single summary reads as one statement about
 * the Platform, and the rules still separate the figures. Every figure stays a
 * link to the screen that explains it.
 *
 * **Nothing here refreshes itself**, and the header says so with the time it was
 * read. A figure that silently ages is worse than one dated honestly.
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
    <>
      <Section heading={labels.attention} id="attention">
        {attention.length === 0 ? (
          <p className="py-4 text-sm text-text-secondary">{labels.allClear}</p>
        ) : (
          <ul className="divide-y divide-border">
            {attention.map((line) => (
              <li key={line.key} className="flex flex-wrap items-baseline gap-x-4 gap-y-2 py-3.5">
                {/* A fixed column so the sentences start on one line rather
                    than stepping in and out with the length of each word. */}
                <span className="w-20 shrink-0">
                  <StatusBadge tone={line.tone}>{line.levelLabel}</StatusBadge>
                </span>

                <Link
                  href={line.href}
                  className="min-w-0 flex-1 text-sm leading-relaxed text-text hover:text-primary-900 hover:underline underline-offset-4"
                >
                  {line.text}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Section>

      {tasks === null ? null : (
        <Section heading={labels.tasks} id="tasks" action={{ href: tasksHref, label: labels.tasksAll }}>
          {tasks.length === 0 ? (
            <p className="py-4 text-sm text-text-secondary">{labels.noTasks}</p>
          ) : (
            <ul className="divide-y divide-border">
              {tasks.map((task) => (
                <li
                  key={task.id}
                  className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1 py-3.5"
                >
                  <div className="min-w-0">
                    <Link
                      href={task.href}
                      className="text-sm font-medium text-text hover:text-primary-900 hover:underline underline-offset-4"
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

      <Section heading={labels.figures} id="figures">
        <div className="mt-4 grid grid-cols-2 border border-border lg:grid-cols-4">
          {figures.map((figure, index) => (
            <Link
              key={figure.href}
              href={figure.href}
              className={
                'group block px-6 py-5 hover:bg-primary-50 ' +
                // Rules between figures, on the logical start edge so they
                // mirror in Arabic. Two columns on a narrow screen, four wide.
                (index % 2 === 1 ? 'border-s border-border ' : '') +
                (index === 2 ? 'lg:border-s ' : '') +
                (index >= 2 ? 'border-t border-border lg:border-t-0' : '')
              }
            >
              <p className="text-sm text-text-secondary">{figure.label}</p>

              {figure.value === null ? (
                <>
                  <p className="mt-2 text-base font-semibold text-text-muted">
                    {labels.unavailable}
                  </p>
                  <p className="mt-1 text-xs text-text-secondary">{labels.unavailableHint}</p>
                </>
              ) : (
                <p
                  className="mt-2 text-3xl font-semibold text-text group-hover:text-primary-900"
                  data-numeric
                >
                  {figure.value}
                </p>
              )}
            </Link>
          ))}
        </div>
      </Section>

      {operations === null ? null : (
        <Section
          heading={labels.operations}
          id="operations"
          action={{ href: operationsHref, label: labels.operationsAll }}
        >
          <dl className="grid grid-cols-2 gap-x-6 gap-y-4 py-4 lg:grid-cols-4">
            {operations.map((figure) => (
              <div key={figure.label}>
                <dt className="text-sm text-text-secondary">{figure.label}</dt>
                <dd className="mt-1 text-xl font-semibold text-text" data-numeric>
                  {figure.value}
                </dd>
              </div>
            ))}
          </dl>
        </Section>
      )}
    </>
  );
}

/**
 * A band of the dashboard: a heading, a rule, and whatever it holds.
 *
 * One shape for all four, so the screen reads as a sequence of statements
 * rather than as a collection of panels with their own ideas about spacing.
 */
function Section({
  heading,
  id,
  action,
  children,
}: {
  heading: string;
  id: string;
  action?: { href: string; label: string };
  children: ReactNode;
}) {
  return (
    <section aria-labelledby={`${id}-heading`} className="mt-8 first:mt-0">
      <div className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1 border-b border-border pb-3">
        <h2 id={`${id}-heading`} className="text-lg font-semibold text-text">
          {heading}
        </h2>

        {action ? (
          <Link
            href={action.href}
            className="text-sm text-primary-700 hover:text-primary-900 hover:underline underline-offset-4"
          >
            {action.label}
          </Link>
        ) : null}
      </div>

      {children}
    </section>
  );
}
