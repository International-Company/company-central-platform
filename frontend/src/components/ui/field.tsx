'use client';

import { useId } from 'react';
import type { InputHTMLAttributes, ReactNode } from 'react';

/**
 * A labelled input, with its error attached to it.
 *
 * **The label is a real `<label>` bound by `htmlFor`**, not a floating
 * placeholder. A placeholder disappears the moment someone types, which leaves
 * a form of unlabelled boxes for anyone who looked away — and a placeholder is
 * not a label to a screen reader at all.
 *
 * The error is joined to the input by `aria-describedby` and announced through
 * `role="alert"`. An error message sitting visually near a field but not
 * associated with it does not exist for a screen-reader user.
 */

export interface FieldProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id' | 'aria-invalid'> {
  label: string;
  error?: string | undefined;
  hint?: string | undefined;

  /** The word for "required", from the catalogue. Never hardcoded here. */
  requiredLabel?: string | undefined;
}

export function Field({
  label,
  error,
  hint,
  requiredLabel,
  required,
  className = '',
  ...rest
}: FieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;

  const describedBy =
    [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ') ||
    undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <label
        htmlFor={id}
        className="text-sm font-medium text-text"
      >
        {label}
        {required && requiredLabel ? (
          // The word, not a red asterisk. An asterisk means nothing to a
          // screen reader and little to a user who has not been told the
          // convention.
          <span className="ms-1 font-normal text-text-muted">
            ({requiredLabel})
          </span>
        ) : null}
      </label>

      <input
        id={id}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        className={
          'h-9 rounded-sm bg-surface px-3 text-sm text-text ' +
          'placeholder:text-text-muted focus:border-primary-700 ' +
          // A field in error is marked by weight, not by a second colour: a
          // border twice as heavy in the deepest blue, and the message beneath
          // it in bold. Red was the one place a third hue survived.
          (error
            ? 'border-2 border-attention'
            : 'border border-border-strong') +
          ` ${className}`
        }
        {...rest}
      />

      {hint ? (
        <p id={hintId} className="text-xs text-text-secondary">
          {hint}
        </p>
      ) : null}

      {error ? (
        <p
          id={errorId}
          role="alert"
          className="text-sm font-semibold text-attention"
        >
          {error}
        </p>
      ) : null}
    </div>
  );
}

/**
 * A form-level message.
 *
 * Carries the correlation id on failure, so a user reporting a problem can
 * quote something an engineer can find in the logs — instead of "it said
 * something went wrong".
 */
export function FormMessage({
  tone,
  children,
}: {
  tone: 'error' | 'success' | 'info';
  children: ReactNode;
}) {
  // A rule on the start edge, heavier for an error, instead of three tinted
  // boxes in three colours. `border-s` is the start edge, so it moves to the
  // right in Arabic without a second rule.
  const tones = {
    error: 'border-s-4 border-attention bg-attention-surface font-medium text-text',
    success: 'border-s-4 border-primary-500 bg-positive-surface text-text',
    info: 'border-s-4 border-border-strong bg-surface-sunken text-text-secondary',
  } as const;

  return (
    <div
      // Errors interrupt; confirmations wait their turn. Both are announced.
      role={tone === 'error' ? 'alert' : 'status'}
      className={`px-4 py-3 text-sm ${tones[tone]}`}
    >
      {children}
    </div>
  );
}
