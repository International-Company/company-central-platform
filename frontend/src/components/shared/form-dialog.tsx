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
    // m-auto restores the centring browsers give a modal dialog. Tailwind's
    // reset sets every margin to zero, so every dialog in the portal opened
    // pinned to the top corner of the screen, including in production;
    // nobody saw it until the design was checked by looking at screenshots.
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
      className="w-[min(34rem,calc(100vw-2rem))] m-auto max-h-[calc(100dvh-2rem)] overflow-y-auto border border-border-strong bg-surface p-0 text-text shadow-overlay backdrop:bg-text/40"
    >
      <form onSubmit={handleSubmit} noValidate>
        <div className="border-b border-border px-6 py-4">
          <h2 className="text-lg font-semibold">{title}</h2>

          {description ? (
            <p className="mt-1 text-sm leading-relaxed text-text-secondary">{description}</p>
          ) : null}
        </div>

        <div className="flex flex-col gap-4 px-6 py-5">
          {error ? <FormMessage tone="error">{error}</FormMessage> : null}

          {children}
        </div>

        <div className="flex justify-end gap-3 border-t border-border bg-surface-sunken px-6 py-4">
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
