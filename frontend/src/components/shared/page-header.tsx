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
  meta,
  action,
}: {
  title: string;
  description?: string;

  /**
   * A fact about the page rather than something to do with it: the time it was
   * read, a count, a range. It was being appended to the description, which
   * made one long run-on sentence out of two unrelated statements, and it
   * prints, which the action does not.
   */
  meta?: ReactNode;

  action?: ReactNode;
}) {
  return (
    <div className="mb-7 flex flex-wrap items-end justify-between gap-x-8 gap-y-3 border-b border-border pb-4">
      <div className="min-w-0">
        <h1 className="text-2xl font-semibold leading-tight tracking-tight text-text">{title}</h1>

        {description ? (
          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-text-secondary">
            {description}
          </p>
        ) : null}
      </div>

      <div className="flex shrink-0 items-baseline gap-6">
        {meta ? <p className="text-xs text-text-muted">{meta}</p> : null}

        {action ? <div data-print-hidden>{action}</div> : null}
      </div>
    </div>
  );
}
