'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useTranslations } from 'next-intl';
import { useRouter } from '@/i18n/routing';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';

/**
 * The second-factor challenge.
 *
 * A recovery code is entered in the same field as a TOTP code, with a checkbox
 * saying which it is. Two separate forms would ask the user to categorise their
 * own credential before using it — and someone reaching for a recovery code has
 * usually just lost their phone, which is not the moment for a puzzle.
 */
export default function MfaPage() {
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const router = useRouter();

  const [code, setCode] = useState('');
  const [isRecoveryCode, setIsRecoveryCode] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    void submit();
  }

  async function submit() {
    setError(null);
    setBusy(true);

    try {
      const response = await fetch('/api/auth/mfa', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, isRecoveryCode }),
      });

      if (!response.ok) {
        setError(
          response.status === 429
            ? tErrors('tooManyRequests')
            : tErrors('unauthorized'),
        );

        return;
      }

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
        <h2 className="text-sm font-medium text-[--color-text]">
          {t('mfaTitle')}
        </h2>

        <p className="mt-1 text-sm text-[--color-text-secondary]">
          {t('mfaDescription')}
        </p>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <Field
        label={t('mfaCode')}
        value={code}
        onChange={(event) => setCode(event.target.value)}
        // The browser fills this from an SMS or an authenticator where it can.
        autoComplete="one-time-code"
        // A numeric keypad on a phone, without forbidding the letters a
        // recovery code contains.
        inputMode={isRecoveryCode ? 'text' : 'numeric'}
        autoFocus
        required
        requiredLabel={tCommon('required')}
      />

      <label className="flex items-center gap-2 text-sm text-[--color-text-secondary]">
        <input
          type="checkbox"
          checked={isRecoveryCode}
          onChange={(event) => setIsRecoveryCode(event.target.checked)}
        />
        {t('useRecoveryCode')}
      </label>

      <Button
        type="submit"
        variant="primary"
        busy={busy}
        busyLabel={tCommon('loading')}
      >
        {t('verify')}
      </Button>
    </form>
  );
}
