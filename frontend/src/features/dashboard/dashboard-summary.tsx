import Link from 'next/link';

/**
 * The dashboard's figures and next steps, without the reads behind them.
 *
 * Separate from the page so the design preview can render exactly this with
 * sample figures; the page needs the Platform and a signed-in administrator,
 * which no developer machine here has.
 *
 * **One panel with four figures divided by rules, not four cards in a grid.**
 * A row of equal tiles, each a big number in a rounded box, is the first thing
 * a generated dashboard produces; a single summary reads as one statement about
 * the Platform, and the rules still separate the figures. Every figure stays a
 * link to the screen that explains it.
 */
export interface DashboardFigure {
  href: string;
  label: string;

  /** Already formatted, or null when the caller may not read it. */
  value: string | null;
}

export interface DashboardStep {
  href: string;
  text: string;

  /** Already formatted for the locale. */
  number: string;
}

export function DashboardSummary({
  heading,
  nextStepsHeading,
  unavailable,
  unavailableHint,
  figures,
  steps,
}: {
  heading: string;
  nextStepsHeading: string;
  unavailable: string;
  unavailableHint: string;
  figures: DashboardFigure[];
  steps: DashboardStep[];
}) {
  return (
    <>
      <section aria-labelledby="summary-heading" className="border border-border">
        <h2 id="summary-heading" className="sr-only">
          {heading}
        </h2>

        <div className="grid grid-cols-2 lg:grid-cols-4">
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
                  <p className="mt-2 text-base font-semibold text-text-muted">{unavailable}</p>
                  <p className="mt-1 text-xs text-text-secondary">{unavailableHint}</p>
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
      </section>

      <section aria-labelledby="next-steps-heading" className="mt-10">
        <h2
          id="next-steps-heading"
          className="border-b border-border pb-3 text-lg font-semibold text-text"
        >
          {nextStepsHeading}
        </h2>

        <ol className="divide-y divide-border">
          {steps.map((step) => (
            <li key={step.href} className="flex gap-5 py-4">
              {/* The number as text in its own column, so the list reads as an
                  ordered procedure without relying on list markers. */}
              <span className="w-6 shrink-0 text-sm font-semibold text-primary-700" data-numeric>
                {step.number}
              </span>

              <Link
                href={step.href}
                className="text-sm text-text hover:text-primary-900 hover:underline underline-offset-4"
              >
                {step.text}
              </Link>
            </li>
          ))}
        </ol>
      </section>
    </>
  );
}
