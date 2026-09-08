/**
 * A status, said in words.
 *
 * **Colour is never the only carrier of meaning** (ARCHITECTURE.md §9.8). The
 * badge always contains the status as text; the tint is a second, redundant
 * signal. A colour-only status fails for a colour-blind reader, disappears in a
 * printed report, and tells a screen reader nothing at all.
 *
 * The border is what carries the distinction when colour is unavailable — solid
 * for neutral, and the tinted background is genuinely decoration here.
 */
export type StatusTone = 'neutral' | 'success' | 'warning' | 'danger';

const tones: Record<StatusTone, string> = {
  neutral:
    'border-[--color-border-strong] bg-[--color-surface-sunken] text-[--color-text-secondary]',
  success:
    'border-[--color-success] bg-[--color-success-surface] text-[--color-success]',
  warning:
    'border-[--color-warning] bg-[--color-warning-surface] text-[--color-warning]',
  danger:
    'border-[--color-danger] bg-[--color-danger-surface] text-[--color-danger]',
};

export function StatusBadge({
  tone,
  children,
}: {
  tone: StatusTone;
  children: string;
}) {
  return (
    <span
      className={`inline-block whitespace-nowrap rounded-[--radius-sm] border px-2 py-0.5 text-xs font-medium ${tones[tone]}`}
    >
      {children}
    </span>
  );
}
