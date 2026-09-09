'use client';

import { useEffect, useRef } from 'react';
import type { FormEvent, ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';

/**
 * A form in a dialog: create this, edit that.
 *
 * A dialog rather than a separate page because these forms are short and the
 * list behind them is the context. Sending someone to `/users/new` and back
 * loses their filters and their place in the table for the sake of four fields.
 *
 * Native `<dialog>`, for the same reasons as the confirmation: focus trapping,
 * Escape and page inertness are the browser's job, and a hand-built modal gets
 * them subtly wrong for exactly the people who cannot see that it did.
 */
export function FormDialog({
  open,
  title,
  description,
  submitLabel,
  cancelLabel,
  busy = false,
  busyLabel,
  error,
  children,
  onSubmit,
  onCancel,
}: {
  open: boolean;
  title: string;
  description?: string;
  submitLabel: string;
  cancelLabel: string;
  busy?: boolean;
  busyLabel?: string;

  /** A failure that belongs to the form as a whole, not to one field. */
  error?: string | null;

  children: ReactNode;
  onSubmit: () => void;
  onCancel: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;

    if (!dialog) {
      return;
    }

    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSubmit();
  }

  return (
    <dialog
      ref={ref}
      onCancel={(event) => {
        event.preventDefault();

        // Escape does nothing while a request is in flight. Closing the dialog
        // then would leave the user unsure whether the thing was created.
        if (!busy) {
          onCancel();
        }
      }}
      className="w-[min(32rem,calc(100vw-2rem))] rounded-lg border border-border bg-surface p-5 text-text backdrop:bg-text/30"
    >
      <form onSubmit={handleSubmit} noValidate>
        <h2 className="text-sm font-semibold">{title}</h2>

        {description ? (
          <p className="mt-1 text-sm text-text-secondary">{description}</p>
        ) : null}

        {error ? (
          <div className="mt-3">
            <FormMessage tone="error">{error}</FormMessage>
          </div>
        ) : null}

        <div className="mt-4 flex flex-col gap-3">{children}</div>

        <div className="mt-5 flex justify-end gap-2">
          <Button type="button" onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </Button>

          <Button
            type="submit"
            variant="primary"
            busy={busy}
            {...(busyLabel ? { busyLabel } : {})}
          >
            {submitLabel}
          </Button>
        </div>
      </form>
    </dialog>
  );
}

/**
 * Maps the Platform's field errors onto inputs.
 *
 * The API answers with a `field` on each error precisely so a form can put the
 * message where the mistake is. Showing them all in one banner at the top would
 * make the user hunt for which box is wrong.
 */
export function fieldErrors(
  errors: { code: string; message: string; field?: string }[] | undefined,
): Record<string, string> {
  const mapped: Record<string, string> = {};

  for (const error of errors ?? []) {
    if (error.field && !mapped[error.field]) {
      mapped[error.field] = error.message;
    }
  }

  return mapped;
}
