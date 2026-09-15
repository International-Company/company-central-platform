import type { ReactNode } from 'react';

/**
 * The heading of a screen: what this page is, and the one action it offers.
 *
 * A single `<h1>` per page, because a screen reader's heading navigation is only
 * useful when the headings mean something.
 *
 * Set large and closed off with a rule. The title used to be the size of a
 * table header, so nothing on a screen said where the page began and every
 * screen looked like the middle of another one.
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
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4 border-b border-border pb-5">
      <div className="min-w-0">
        <h1 className="text-2xl font-semibold leading-tight text-text">{title}</h1>

        {description ? (
          <p className="mt-1.5 max-w-3xl text-sm leading-relaxed text-text-secondary">
            {description}
          </p>
        ) : null}
      </div>

      {action ? <div data-print-hidden>{action}</div> : null}
    </div>
  );
}
