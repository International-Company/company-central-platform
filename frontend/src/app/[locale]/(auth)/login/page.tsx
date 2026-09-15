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

      // change-password, not reset-password. Reset proves identity with an
      // emailed token; this account is already signed in and proves itself
      // with the password it is replacing. Sending it to reset was a dead end —
      // mail delivery does not exist until Phase 9, so the token would never
      // arrive.
      router.push(
        body.requiresMfa
          ? '/mfa'
          : body.mustChangePassword
            ? '/change-password'
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
      // "Sign in to continue" is what an expired session says. For a rejected
      // credential it is actively confusing: the person is trying to sign in.
      //
      // The message also warns that repeated failures slow the next attempt,
      // because the account lockout is progressive and silent — the Platform
      // returns the same uniform failure whether the password was wrong or the
      // account is waiting out a delay, so that an attacker cannot tell the
      // difference. An honest user, having no such warning, concludes the
      // system is broken.
      return tErrors('invalidCredentials');
    }

    return tErrors('generic');
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-5" noValidate>
      <div>
        <h2 className="text-2xl font-semibold text-text">
          {t('signInTitle')}
        </h2>

        <p className="mt-2 text-sm text-text-secondary">{t('signInLead')}</p>
      </div>

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
        className="text-sm text-primary-700 hover:text-primary-900 hover:underline underline-offset-4"
      >
        {t('forgotPassword')}
      </a>
    </form>
  );
}
