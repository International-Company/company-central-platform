import type { ReactNode } from 'react';

/**
 * The heading of a screen: what this page is, and the one action it offers.
 *
 * A single `<h1>` per page, because a screen reader's heading navigation is only
 * useful when the headings mean something.
 */
export function PageHeader({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 className="text-lg font-semibold text-text">{title}</h1>

        {description ? (
          <p className="mt-0.5 text-sm text-text-secondary">
            {description}
          </p>
        ) : null}
      </div>

      {action ? <div data-print-hidden>{action}</div> : null}
    </div>
  );
}
