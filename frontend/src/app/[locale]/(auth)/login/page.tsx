'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useTranslations } from 'next-intl';
import { useRouter } from '@/i18n/routing';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';

/**
 * Sign-in.
 *
 * Posts to the BFF, never to the Platform directly. The tokens stay on the
 * server; this page learns only where to go next.
 */
export default function LoginPage() {
  const t = useTranslations('auth');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const router = useRouter();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<{ message: string; reference?: string } | null>(
    null,
  );

  // Void-returning, with the async work inside. An async handler passed
  // straight to onSubmit returns a promise nothing awaits, so a rejection would
  // surface as an unhandled rejection rather than as an error the user sees.
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    void submit();
  }

  async function submit() {
    setError(null);
    setBusy(true);

    try {
      const response = await fetch('/api/auth/sign-in', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      });

      if (!response.ok) {
        const body = (await response
          .json()
          .catch(() => ({}))) as { code?: string; correlationId?: string };

        setError({
          message: messageFor(response.status),
          ...(body.correlationId ? { reference: body.correlationId } : {}),
        });

        return;
      }

      const body = (await response.json()) as {
        requiresMfa: boolean;
        mustChangePassword: boolean;
      };

      router.push(
        body.requiresMfa
          ? '/mfa'
          : body.mustChangePassword
            ? '/reset-password'
            : '/dashboard',
      );
    } catch {
      setError({ message: tErrors('network') });
    } finally {
      setBusy(false);
    }
  }

  function messageFor(status: number): string {
    // The Platform answers a wrong username and a wrong password identically,
    // and so does this. Distinguishing them here would hand back the account
    // enumeration the uniform failure exists to prevent.
    if (status === 429) {
      return tErrors('tooManyRequests');
    }

    if (status === 401 || status === 422) {
      return tErrors('unauthorized');
    }

    return tErrors('generic');
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <h2 className="text-sm font-medium text-[--color-text-secondary]">
        {t('signInTitle')}
      </h2>

      {error ? (
        <FormMessage tone="error">
          <p>{error.message}</p>

          {error.reference ? (
            // So a user reporting a problem can quote something an engineer can
            // find, instead of "it said something went wrong".
            <p className="mt-1 text-xs opacity-80">
              {tErrors('correlationId', { id: error.reference })}
            </p>
          ) : null}
        </FormMessage>
      ) : null}

      <Field
        label={t('username')}
        name="username"
        value={username}
        onChange={(event) => setUsername(event.target.value)}
        autoComplete="username"
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('password')}
        name="password"
        type="password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
        autoComplete="current-password"
        required
        requiredLabel={tCommon('required')}
      />

      <Button
        type="submit"
        variant="primary"
        busy={busy}
        busyLabel={tCommon('loading')}
      >
        {t('signIn')}
      </Button>

      <a
        href="forgot-password"
        className="text-center text-sm text-[--color-primary-700] underline underline-offset-2"
      >
        {t('forgotPassword')}
      </a>
    </form>
  );
}
