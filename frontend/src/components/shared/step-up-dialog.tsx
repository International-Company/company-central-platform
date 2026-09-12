'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog } from '@/components/shared/form-dialog';

/**
 * The code the Platform asks for before it will do something consequential.
 *
 * **The refusal and the remedy are the same conversation.** Granting a role
 * comes back 403 when the caller's elevation has lapsed — the same status as
 * "you do not hold this permission", which is deliberate: a prober learns
 * nothing either way. The Platform distinguishes them in the body, and the
 * point of this dialog is that a person who holds the permission is asked for a
 * code instead of being told to go and request access they already have.
 *
 * The verification is not carried in a token this application holds. It is
 * recorded against the session on the Platform and expires there; nothing here
 * decides how long it lasts, and nothing here can extend it.
 */

/** The code the Platform returns when the caller must confirm a second factor. */
export const StepUpRequiredCode = 'SECURITY.STEP_UP_REQUIRED';

/**
 * The code the Platform returns when the caller has no second factor at all.
 *
 * **A different refusal, and it has to be handled differently.** Opening the
 * dialog for somebody with no enrolment asks them for a code they cannot
 * produce; they would try, fail, and try again. The remedy is a screen, not a
 * field.
 */
export const MfaEnrolmentRequiredCode = 'SECURITY.MFA_REQUIRED_BY_POLICY';

/** Whether a failed response is asking for a second factor rather than refusing. */
export function needsStepUp(status: number, code: string | undefined): boolean {
  return status === 403 && code === StepUpRequiredCode;
}

/** Whether the caller has nothing to confirm with and must enrol first. */
export function needsMfaEnrolment(status: number, code: string | undefined): boolean {
  return status === 403 && code === MfaEnrolmentRequiredCode;
}

export function StepUpDialog({
  open,
  onClose,
  onConfirmed,
}: {
  open: boolean;
  onClose: () => void;

  /** Called once the Platform has accepted the code. Retry the action here. */
  onConfirmed: () => void;
}) {
  const t = useTranslations('security');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [code, setCode] = useState('');
  const [isRecoveryCode, setIsRecoveryCode] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setCode('');
      setIsRecoveryCode(false);
      setError(null);
    }
  }, [open]);

  async function submit() {
    setBusy(true);
    setError(null);

    try {
      const response = await fetch('/api/auth/mfa', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, isRecoveryCode }),
      });

      if (!response.ok) {
        // A wrong code is a 400 from the Platform, not a 401 — which matters,
        // because a BFF that treats 401 as an expired session would sign the
        // person out over a single typo.
        setError(
          response.status === 400 || response.status === 422
            ? t('invalidCode')
            : response.status === 404
              ? t('stepUpNotEnrolled')
              : tErrors('generic'),
        );

        return;
      }

      onConfirmed();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog
      open={open}
      title={t('stepUpTitle')}
      description={t('stepUpDescription')}
      submitLabel={t('confirm')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void submit()}
      onCancel={onClose}
    >
      <Field
        label={t('code')}
        value={code}
        onChange={(event) => setCode(event.target.value)}
        // A one-time code, never remembered, and typed on a numeric keypad on a
        // phone — where most people read it from.
        autoComplete="one-time-code"
        inputMode={isRecoveryCode ? 'text' : 'numeric'}
        dir="ltr"
        autoFocus
        required
        requiredLabel={tCommon('required')}
      />

      <button
        type="button"
        onClick={() => setIsRecoveryCode((current) => !current)}
        className="self-start text-sm text-primary-700 underline underline-offset-2"
      >
        {isRecoveryCode ? t('code') : t('useRecoveryCode')}
      </button>
    </FormDialog>
  );
}
