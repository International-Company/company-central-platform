'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';

/**
 * Password recovery.
 *
 * **The same confirmation appears whatever was typed**, including a username
 * that does not exist. The Platform answers 202 either way; this screen must not
 * be more helpful than that, because "no such account" on an anonymous endpoint
 * is a tool for discovering who works here.
 *
 * The cost is real and accepted: someone who mistypes their username waits for
 * an email that never arrives. The alternative is handing an attacker a
 * directory.
 */
export default function ForgotPasswordPage() {
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [username, setUsername] = useState('');
  const [busy, setBusy] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    void submit();
  }

  async function submit() {
    setError(null);
    setBusy(true);

    try {
      const response = await fetch('/api/auth/password/forgot', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username }),
      });

      // Only a rate limit is reported differently, because that is about the
      // request rather than about the account, and telling someone to wait is
      // more useful than leaving them pressing a button that does nothing.
      if (response.status === 429) {
        setError(tErrors('tooManyRequests'));

        return;
      }

      setSubmitted(true);
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  if (submitted) {
    return (
      <FormMessage tone="success">{t('forgotSubmitted')}</FormMessage>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <div>
        <h2 className="text-2xl font-semibold text-text">
          {t('forgotTitle')}
        </h2>

        <p className="mt-1 text-sm text-text-secondary">
          {t('forgotDescription')}
        </p>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <Field
        label={t('username')}
        value={username}
        onChange={(event) => setUsername(event.target.value)}
        autoComplete="username"
        required
        requiredLabel={tCommon('required')}
      />

      <Button
        type="submit"
        variant="primary"
        busy={busy}
        busyLabel={tCommon('loading')}
      >
        {tCommon('confirm')}
      </Button>
    </form>
  );
}
