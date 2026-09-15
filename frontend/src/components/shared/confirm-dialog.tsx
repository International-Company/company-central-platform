'use client';

import { useEffect, useRef } from 'react';
import { Button } from '@/components/ui/button';

/**
 * A confirmation that says what is about to happen.
 *
 * **Never "Are you sure?"** — a question nobody can answer usefully, because it
 * does not say what to be sure about. The title names the subject and the body
 * states the consequence: *Unlock Amira Hassan? The account was locked by
 * repeated failed sign-ins. Unlocking lets them try again immediately.*
 *
 * A native `<dialog>` rather than a hand-built overlay. The browser then handles
 * focus trapping, Escape, the top layer and inertness of the page behind — all
 * of which a hand-rolled modal gets subtly wrong, usually for keyboard users
 * who cannot see that it went wrong.
 */
export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel,
  destructive = false,
  busy = false,
  busyLabel,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  cancelLabel: string;
  destructive?: boolean;
  busy?: boolean;
  busyLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;

    if (!dialog) {
      return;
    }

    if (open && !dialog.open) {
      // `showModal`, not `show`: only the modal form makes the rest of the page
      // inert and moves focus inside.
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  return (
    // m-auto restores the centring browsers give a modal dialog. Tailwind's
    // reset sets every margin to zero, so every dialog in the portal opened
    // pinned to the top corner of the screen, including in production;
    // nobody saw it until the design was checked by looking at screenshots.
    <dialog
      ref={ref}
      // Escape closes it. Without this the browser would dismiss the dialog
      // while the component still believed it was open.
      onCancel={(event) => {
        event.preventDefault();
        onCancel();
      }}
      className="w-[min(30rem,calc(100vw-2rem))] m-auto max-h-[calc(100dvh-2rem)] overflow-y-auto border border-border-strong bg-surface p-0 text-text shadow-overlay backdrop:bg-text/40"
    >
      <div className="border-b border-border px-6 py-4">
        <h2 className="text-lg font-semibold">{title}</h2>
      </div>

      <p className="px-6 py-5 text-sm leading-relaxed text-text-secondary">{description}</p>

      <div className="flex justify-end gap-3 border-t border-border bg-surface-sunken px-6 py-4">
        <Button onClick={onCancel} disabled={busy}>
          {cancelLabel}
        </Button>

        <Button
          variant={destructive ? 'danger' : 'primary'}
          onClick={onConfirm}
          busy={busy}
          {...(busyLabel ? { busyLabel } : {})}
        >
          {confirmLabel}
        </Button>
      </div>
    </dialog>
  );
}
