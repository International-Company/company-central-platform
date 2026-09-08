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
        className="text-sm font-medium text-[--color-text]"
      >
        {label}
        {required && requiredLabel ? (
          // The word, not a red asterisk. An asterisk means nothing to a
          // screen reader and little to a user who has not been told the
          // convention.
          <span className="ms-1 font-normal text-[--color-text-muted]">
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
          'h-10 rounded-[--radius-md] border bg-[--color-surface] px-3 text-sm ' +
          'text-[--color-text] placeholder:text-[--color-text-muted] ' +
          (error
            ? 'border-[--color-danger]'
            : 'border-[--color-border-strong]') +
          ` ${className}`
        }
        {...rest}
      />

      {hint ? (
        <p id={hintId} className="text-xs text-[--color-text-secondary]">
          {hint}
        </p>
      ) : null}

      {error ? (
        <p
          id={errorId}
          role="alert"
          className="text-xs font-medium text-[--color-danger]"
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
  const tones = {
    error:
      'border-[--color-danger] bg-[--color-danger-surface] text-[--color-danger]',
    success:
      'border-[--color-success] bg-[--color-success-surface] text-[--color-success]',
    info: 'border-[--color-border-strong] bg-[--color-surface-sunken] text-[--color-text-secondary]',
  } as const;

  return (
    <div
      // Errors interrupt; confirmations wait their turn. Both are announced.
      role={tone === 'error' ? 'alert' : 'status'}
      className={`rounded-[--radius-md] border px-3 py-2 text-sm ${tones[tone]}`}
    >
      {children}
    </div>
  );
}
