'use client';

import { Children, Fragment, isValidElement } from 'react';
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
  'flex flex-wrap items-center justify-end gap-x-4 gap-y-2 ' +
  '[&_button]:inline-flex [&_button]:min-h-6 [&_button]:min-w-6 [&_button]:items-center ' +
  '[&_a]:inline-flex [&_a]:min-h-6 [&_a]:min-w-6 [&_a]:items-center ' +
  // Every screen writes its own row links, and they had drifted: some
  // underlined flush against the text, some not at all. Set here so they
  // match without fourteen files having to agree.
  '[&_button]:underline-offset-4 [&_a]:underline-offset-4';

/**
 * Row actions, with a hairline between them.
 *
 * Three links separated only by a gap read as one phrase. In English the
 * capitals and the longer words carry it. In Arabic the words are shorter and
 * set tighter: the roles screen showed three across one cell, and because the
 * middle one is a noun they read as one instruction rather than as three
 * things a person may do. A rule is not a symbol standing in for a word;
 * separating is what a rule is for.
 *
 * Fragments are unwrapped first. Every screen returns its actions as one, and
 * `Children.toArray` counts a fragment as a single child, so without this the
 * separators would never appear anywhere.
 */
function Separated({ children }: { children: ReactNode }) {
  const actions = unwrap(children);

  if (actions.length < 2) {
    return <>{children}</>;
  }

  return (
    <>
      {actions.map((action, index) => (
        <Fragment key={index}>
          {index > 0 ? (
            <span aria-hidden="true" className="h-3.5 w-px shrink-0 bg-border-strong" />
          ) : null}

          {action}
        </Fragment>
      ))}
    </>
  );
}

/** Children, with fragments flattened and blanks dropped. */
function unwrap(node: ReactNode): ReactNode[] {
  return Children.toArray(node).flatMap((child) =>
    isValidElement(child) && child.type === Fragment
      ? unwrap((child.props as { children?: ReactNode }).children)
      : [child],
  );
}

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
      {/* No box and no tinted header band. A rule under the headings and a
          hairline between rows is what a table needs to be read; drawing a
          border around the whole thing and filling the header makes it a
          spreadsheet dropped into the page. */}
      <div
        className="hidden overflow-x-auto sm:block"
        // Focusable so the scroll region is reachable by keyboard — a scrollable
        // area that only a mouse can move is unusable without one.
        tabIndex={0}
        role="region"
        aria-label={caption}
      >
        <table className="w-full border-collapse text-sm">
          <caption className="sr-only">{caption}</caption>

          <thead>
            <tr className="border-b border-border-strong">
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
                    'whitespace-nowrap px-3 pb-2 pt-1 text-xs font-semibold text-text-secondary ' +
                    (column.numeric ? 'text-end' : 'text-start') +
                    (column.secondary ? ' hidden md:table-cell' : '')
                  }
                >
                  {column.sortable && onSortChange ? (
                    <button
                      type="button"
                      onClick={() => onSortChange(column.key)}
                      className={
                        'inline-flex items-center gap-2 hover:text-primary-900 ' +
                        (sort?.key === column.key ? 'text-primary-900' : '')
                      }
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
                <th scope="col" className="whitespace-nowrap px-3 pb-2 pt-1 text-end text-xs font-semibold text-text-secondary">
                  {labels.actions}
                </th>
              ) : null}
            </tr>
          </thead>

          <tbody>
            {rows.map((row) => (
              <tr
                key={rowKey(row)}
                className="border-b border-border hover:bg-primary-50"
              >
                {columns.map((column) => (
                  <td
                    key={column.key}
                    {...(column.numeric ? { 'data-numeric': true } : {})}
                    className={
                      'px-3 py-2.5 align-middle text-text ' +
                      (column.numeric ? 'text-end' : 'text-start') +
                      (column.secondary ? ' hidden md:table-cell' : '')
                    }
                  >
                    {column.render(row)}
                  </td>
                ))}

                {rowActions ? (
                  <td className="px-3 py-2.5 align-middle">
                    <div className={RowActions}>
                      <Separated>{rowActions(row)}</Separated>
                    </div>
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
            className="border border-border bg-surface p-4"
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
              <div className={`mt-3 border-t border-border pt-3 ${RowActions}`}>
                <Separated>{rowActions(row)}</Separated>
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

  // The direction in words, small and quiet, where a triangle used to be. A
  // glyph pointing up means ascending to some readers and "most recent first"
  // to others, and it is one more symbol on a screen that is meant to have
  // none; the word is unambiguous in both languages and a screen reader reads
  // the same text everybody else sees.
  // In brackets, like "(Required)" beside a field label. Set plainly beside the
  // heading it read as part of it: "Name Ascending" is a column called Name
  // Ascending until you have worked out that it is not.
  return (
    <span className="text-xs font-normal text-text-muted">
      ({ascending ? labels.sortAscending : labels.sortDescending})
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
    //
    // Centred between two rules rather than boxed and start-aligned: a bordered
    // block with its text against the start edge is the shape of an error
    // banner, and "no results" is not an error.
    <div className="border-y border-border px-6 py-12 text-center">
      <p className="text-sm font-semibold text-text">{title}</p>

      {description ? (
        <p className="mx-auto mt-1.5 max-w-md text-sm leading-relaxed text-text-secondary">
          {description}
        </p>
      ) : null}

      {action ? <div className="mt-5">{action}</div> : null}
    </div>
  );
}
