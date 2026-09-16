/**
 * A status, said in words.
 *
 * **Colour is never the only carrier of meaning** (ARCHITECTURE.md §9.8). The
 * badge always contains the status as text. What distinguishes one tone from
 * another is intensity along a single blue, not a change of hue:
 *
 *   neutral   a grey outline        inactive, archived, nothing to act on
 *   success   a light blue tint     active, delivered, in good order
 *   warning   a deep blue outline   waiting, pending, worth a look
 *   danger    solid deep blue       locked, failed, blocked
 *
 * These were grey, green, amber and red. A table whose last column is a row of
 * four colours is the signature of an interface assembled from a kit; the same
 * four states in one blue read as one designed system, still sort by urgency at
 * a glance, and survive a black-and-white printout and a colour-blind reader,
 * which the hues did not.
 *
 * The tone names stay as they were, because they describe meaning and every
 * screen already chooses by meaning.
 */
export type StatusTone = 'neutral' | 'success' | 'warning' | 'danger';

const tones: Record<StatusTone, string> = {
  // No box. A column where every row carries a bordered chip is a column of
  // boxes before it is a column of states, and the one state nobody has to act
  // on is the one that should not be drawing the eye. It keeps the padding, so
  // the words still line up with the boxed ones beside them.
  neutral: 'border-transparent bg-transparent text-text-secondary',
  success: 'border-primary-200 bg-positive-surface text-positive',
  warning: 'border-caution bg-caution-surface font-semibold text-caution',
  danger: 'border-attention bg-attention font-semibold text-text-on-primary',
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
      className={`inline-block whitespace-nowrap rounded-sm border px-2 py-0.5 text-xs ${tones[tone]}`}
    >
      {children}
    </span>
  );
}
