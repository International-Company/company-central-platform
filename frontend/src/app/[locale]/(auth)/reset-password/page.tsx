'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { useRouter } from '@/i18n/routing';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import type { ProblemResponse } from '@/types/platform';

/**
 * Choosing a new password.
 *
 * **The policy is not restated here.** The Platform owns it — length, history,
 * breach screening — and it answers with the specific rule that was broken. A
 * copy of the rules on this screen would be a second source of truth that drifts
 * the first time the policy changes, and the drift would show as a form that
 * accepts what the server then refuses.
 *
 * Only the one check the server cannot make is done locally: whether the two
 * boxes match.
 */
export default function ResetPasswordPage() {
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const router = useRouter();
  const searchParams = useSearchParams();

  const [password, setPassword] = useState('');
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

    if (password !== confirmation) {
      setFieldError(tErrors('passwordsDoNotMatch'));

      return;
    }

    setBusy(true);

    try {
      const response = await fetch('/api/auth/password/reset', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          token: searchParams.get('token') ?? '',
          newPassword: password,
        }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;

        // The Platform's own message, shown as it was written. It names the
        // rule that failed — too short, previously used, found in a breach —
        // and a generic "invalid password" would leave the user guessing which.
        setError(body.errors?.[0]?.message ?? tErrors('generic'));

        return;
      }

      router.push('/login');
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <h2 className="text-2xl font-semibold text-text">
        {t('resetTitle')}
      </h2>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <Field
        label={t('newPassword')}
        type="password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
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
