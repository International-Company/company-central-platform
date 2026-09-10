'use client';

import type { ReactNode } from 'react';

/**
 * The table every list screen uses (ARCHITECTURE.md §9.5, §9.6, §9.7).
 *
 * **Tables are the primary interface for enterprise data**, so this is written
 * once and shared. A second table implementation in a feature folder is a review
 * failure — it is how two screens end up sorting differently, paginating
 * differently, and disagreeing about what "empty" looks like.
 *
 * **Responsive strategy** (§9.7): dense tables scroll horizontally inside a
 * bounded container rather than collapsing into something unusable, and the
 * *page* never scrolls sideways. Below the small breakpoint the same rows render
 * as a stacked list, because a six-column table on a phone is a table nobody
 * reads.
 *
 * **No icon buttons in rows.** Actions are text links. A row of glyphs is
 * unreadable at a glance and unlabelled to a screen reader.
 */

export interface Column<TRow> {
  /** Stable key. Also the sort key when the column is sortable. */
  key: string;

  /** Already translated by the caller. This component holds no strings. */
  header: string;

  render: (row: TRow) => ReactNode;

  sortable?: boolean;

  /** Right-aligns in LTR and left-aligns in RTL, via a logical property. */
  numeric?: boolean;

  /** Hidden on the narrowest screens, where space has to be spent on what matters. */
  secondary?: boolean;
}

export interface DataTableLabels {
  noResults: string;
  noResultsDescription: string;
  sortAscending: string;
  sortDescending: string;
  actions: string;
}

export interface DataTableProps<TRow> {
  columns: Column<TRow>[];
  rows: TRow[];
  rowKey: (row: TRow) => string;
  labels: DataTableLabels;

  /** A caption for screen readers, describing what this table lists. */
  caption: string;

  sort?: { key: string; direction: 'asc' | 'desc' } | undefined;
  onSortChange?: ((key: string) => void) | undefined;

  /** Text actions for a row. Rendered as links, never as icons. */
  rowActions?: ((row: TRow) => ReactNode) | undefined;
}

/**
 * The row-action container.
 *
 * **WCAG 2.2 adds Target Size (Minimum), and a table of text buttons is exactly
 * what it was written about.** An "Edit" link in a dense row is around twenty
 * pixels tall, which is comfortable with a mouse and a real problem with a
 * thumb or a tremor. The criterion asks for 24 by 24.
 *
 * Applied here rather than at each call site, because every screen writes its
 * own buttons and a rule that has to be repeated fourteen times is a rule that
 * is missing from at least one of them. The child selector reaches whatever a
 * screen chose to render, including buttons added next year.
 *
 * The design does not change: they are still text, still blue, still no icons.
 * They are simply large enough to hit.
 */
const RowActions =
  'flex flex-wrap items-center justify-end gap-3 ' +
  '[&_button]:inline-flex [&_button]:min-h-6 [&_button]:min-w-6 [&_button]:items-center ' +
  '[&_a]:inline-flex [&_a]:min-h-6 [&_a]:min-w-6 [&_a]:items-center';

export function DataTable<TRow>({
  columns,
  rows,
  rowKey,
  labels,
  caption,
  sort,
  onSortChange,
  rowActions,
}: DataTableProps<TRow>) {
  if (rows.length === 0) {
    return (
      <EmptyState
        title={labels.noResults}
        description={labels.noResultsDescription}
      />
    );
  }

  return (
    <>
      {/* Wide screens: a real table. Bounded and scrollable on its own so the
          page never scrolls sideways. */}
      <div
        className="hidden overflow-x-auto rounded-md border border-border bg-surface sm:block"
        // Focusable so the scroll region is reachable by keyboard — a scrollable
        // area that only a mouse can move is unusable without one.
        tabIndex={0}
        role="region"
        aria-label={caption}
      >
        <table className="w-full border-collapse text-sm">
          <caption className="sr-only">{caption}</caption>

          <thead>
            <tr className="border-b border-border bg-surface-sunken">
              {columns.map((column) => (
                <th
                  key={column.key}
                  scope="col"
                  // Announces the current sort to assistive technology, which
                  // otherwise cannot tell the column is ordered at all.
                  aria-sort={
                    sort?.key === column.key
                      ? sort.direction === 'asc'
                        ? 'ascending'
                        : 'descending'
                      : undefined
                  }
                  className={
                    'px-3 py-2.5 font-medium text-text-secondary ' +
                    (column.numeric ? 'text-end' : 'text-start') +
                    (column.secondary ? ' hidden md:table-cell' : '')
                  }
                >
                  {column.sortable && onSortChange ? (
                    <button
                      type="button"
                      onClick={() => onSortChange(column.key)}
                      className="inline-flex items-center gap-1 hover:text-text"
                    >
                      {column.header}
                      <SortIndicator
                        active={sort?.key === column.key}
                        direction={sort?.direction}
                        labels={labels}
                      />
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              ))}

              {rowActions ? (
                <th scope="col" className="px-3 py-2.5 text-end font-medium text-text-secondary">
                  {labels.actions}
                </th>
              ) : null}
            </tr>
          </thead>

          <tbody>
            {rows.map((row) => (
              <tr
                key={rowKey(row)}
                className="border-b border-border last:border-b-0 hover:bg-surface-sunken"
              >
                {columns.map((column) => (
                  <td
                    key={column.key}
                    {...(column.numeric ? { 'data-numeric': true } : {})}
                    className={
                      'px-3 py-2.5 text-text ' +
                      (column.numeric ? 'text-end' : 'text-start') +
                      (column.secondary ? ' hidden md:table-cell' : '')
                    }
                  >
                    {column.render(row)}
                  </td>
                ))}

                {rowActions ? (
                  <td className="px-3 py-2.5">
                    <div className={RowActions}>{rowActions(row)}</div>
                  </td>
                ) : null}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Narrow screens: the same rows, stacked. A dense table on a phone is a
          table nobody reads, and shrinking the text is not an answer. */}
      <ul className="flex flex-col gap-2 sm:hidden">
        {rows.map((row) => (
          <li
            key={rowKey(row)}
            className="rounded-md border border-border bg-surface p-3"
          >
            <dl className="flex flex-col gap-1.5">
              {columns.map((column) => (
                <div
                  key={column.key}
                  className="flex items-baseline justify-between gap-3"
                >
                  <dt className="text-xs text-text-secondary">
                    {column.header}
                  </dt>
                  <dd className="text-sm text-text">
                    {column.render(row)}
                  </dd>
                </div>
              ))}
            </dl>

            {rowActions ? (
              <div className={`mt-2 border-t border-border pt-2 ${RowActions}`}>
                {rowActions(row)}
              </div>
            ) : null}
          </li>
        ))}
      </ul>
    </>
  );
}

/**
 * The sort direction.
 *
 * One of the narrow exceptions to the no-icons rule (§9.5): a direction is
 * genuinely shape, and a word here would crowd every sortable header. The
 * accessible name still carries the meaning in text.
 */
function SortIndicator({
  active,
  direction,
  labels,
}: {
  active: boolean;
  direction?: 'asc' | 'desc' | undefined;
  labels: DataTableLabels;
}) {
  if (!active) {
    return null;
  }

  const ascending = direction === 'asc';

  return (
    <span aria-hidden="true" className="text-xs">
      {ascending ? '▲' : '▼'}
      <span className="sr-only">
        {ascending ? labels.sortAscending : labels.sortDescending}
      </span>
    </span>
  );
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    // No illustration. An empty state is a sentence explaining what is missing
    // and, where useful, the button that fixes it (§9.5).
    <div className="rounded-md border border-border bg-surface px-6 py-10 text-center">
      <p className="text-sm font-medium text-text">{title}</p>

      {description ? (
        <p className="mt-1 text-sm text-text-secondary">
          {description}
        </p>
      ) : null}

      {action ? <div className="mt-4">{action}</div> : null}
    </div>
  );
}
