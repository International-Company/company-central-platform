'use client';

import { Button } from '@/components/ui/button';

/**
 * Page controls.
 *
 * **Text buttons, not arrows.** *Previous* and *Next* say what they do and, more
 * to the point, they mirror correctly: an arrow glyph pointing right means
 * "forward" in English and "backward" in Arabic, and nothing in the layout
 * mirror fixes that. A word is right in both directions.
 *
 * The range is stated as well as the page — *Showing 26–50 of 312* — because
 * "page 2 of 13" answers a question nobody asked. What people want to know is
 * how much they are looking at and how much there is.
 */
export interface PaginationLabels {
  showing: (values: { from: number; to: number; total: number }) => string;
  previous: string;
  next: string;
}

export function Pagination({
  page,
  pageSize,
  totalItems,
  onPageChange,
  labels,
}: {
  page: number;
  pageSize: number;
  totalItems: number;
  onPageChange: (page: number) => void;
  labels: PaginationLabels;
}) {
  if (totalItems === 0) {
    return null;
  }

  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalItems);
  const lastPage = Math.max(1, Math.ceil(totalItems / pageSize));

  return (
    <div
      className="mt-4 flex flex-wrap items-center justify-between gap-3"
      data-print-hidden
    >
      {/* Announced politely: a screen-reader user who pages through a table
          needs to know the range changed, without being interrupted. */}
      <p
        aria-live="polite"
        className="text-sm text-text-secondary"
        data-numeric
      >
        {labels.showing({ from, to, total: totalItems })}
      </p>

      <div className="flex items-center gap-2">
        <Button
          size="sm"
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
        >
          {labels.previous}
        </Button>

        <Button
          size="sm"
          onClick={() => onPageChange(page + 1)}
          disabled={page >= lastPage}
        >
          {labels.next}
        </Button>
      </div>
    </div>
  );
}
