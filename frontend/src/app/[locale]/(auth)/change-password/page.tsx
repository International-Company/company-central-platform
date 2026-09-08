'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useTranslations } from 'next-intl';
import { useRouter } from '@/i18n/routing';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import type { ProblemResponse } from '@/types/platform';

/**
 * Changing your own password while signed in.
 *
 * **This is the screen a first sign-in lands on**, and it did not exist until it
 * was needed: an account created with `MustChangePassword` was routed to the
 * reset screen, which proves identity with an emailed token — and mail delivery
 * does not arrive until Phase 9. The first administrator would have signed in
 * successfully and then had nowhere to go.
 *
 * The two flows look similar and are not interchangeable. Reset is for someone
 * locked out, proving themselves by email. This is for someone already in,
 * proving themselves with the password they are replacing.
 *
 * As on the reset screen, the policy is not restated here. The Platform owns it
 * and names the rule that failed; a copy would drift.
 */
export default function ChangePasswordPage() {
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const router = useRouter();

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldError, setFieldError] = useState<string | null>(null);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    void submit();
  }

  async function submit() {
    setError(null);
    setFieldError(null);

    if (newPassword !== confirmation) {
      setFieldError(tErrors('passwordsDoNotMatch'));

      return;
    }

    setBusy(true);

    try {
      const response = await fetch('/api/auth/password/change', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword, newPassword }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;

        setError(
          response.status === 401
            ? tErrors('unauthorized')
            : (body.errors?.[0]?.message ?? tErrors('generic')),
        );

        return;
      }

      // Straight on to the work. Changing the password revoked the other
      // sessions but kept this one, so there is nothing to sign in to again.
      router.push('/dashboard');
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <div>
        <h2 className="text-sm font-medium text-text">
          {t('changeTitle')}
        </h2>

        <p className="mt-1 text-sm text-text-secondary">
          {t('changeDescription')}
        </p>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <Field
        label={t('currentPassword')}
        type="password"
        value={currentPassword}
        onChange={(event) => setCurrentPassword(event.target.value)}
        autoComplete="current-password"
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('newPassword')}
        type="password"
        value={newPassword}
        onChange={(event) => setNewPassword(event.target.value)}
        autoComplete="new-password"
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('confirmPassword')}
        type="password"
        value={confirmation}
        onChange={(event) => setConfirmation(event.target.value)}
        autoComplete="new-password"
        error={fieldError ?? undefined}
        required
        requiredLabel={tCommon('required')}
      />

      <Button
        type="submit"
        variant="primary"
        busy={busy}
        busyLabel={tCommon('loading')}
      >
        {tCommon('save')}
      </Button>
    </form>
  );
}
