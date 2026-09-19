'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { useRouter } from '@/i18n/routing';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { deviceCanVerifyUser, signInWithPasskey } from '@/lib/passkeys';

/**
 * Signing in with the device's own fingerprint, face or passcode.
 *
 * **Offered only where it can work.** The button appears after the browser has
 * been asked whether this machine has a verifying authenticator at all: a
 * desktop without Windows Hello, an old browser, or a page not served over
 * HTTPS all answer no, and a button that opens a dialog saying "your device
 * cannot do that" is worse than no button.
 *
 * It renders nothing at first and appears a moment later, which is the honest
 * shape of the question — it cannot be answered on the server, because the
 * server does not know what the person is holding.
 *
 * **Below the password, not above it.** It is the better credential of the two
 * and still the second thing on the page: nobody has one until they have signed
 * in once and set it up, so leading with it would put the unusable control
 * first for every person's first visit, and for everybody who never sets one up.
 */
export function PasskeySignIn({ separatorLabel }: { separatorLabel: string }) {
  const t = useTranslations('auth');
  const router = useRouter();

  const [available, setAvailable] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let current = true;

    void deviceCanVerifyUser().then((supported) => {
      if (current) {
        setAvailable(supported);
      }
    });

    return () => {
      current = false;
    };
  }, []);

  if (!available) {
    return null;
  }

  async function attempt() {
    setBusy(true);
    setError(null);

    const outcome = await signInWithPasskey();

    if (outcome.kind === 'signed-in') {
      router.push(outcome.mustChangePassword ? '/change-password' : '/dashboard');

      return;
    }

    setBusy(false);

    if (outcome.kind === 'cancelled') {
      // They closed the dialog. Nothing went wrong, and saying so would be
      // telling somebody off for changing their mind.
      return;
    }

    setError(messageFor(outcome.kind === 'failed' ? outcome.code : 'PLATFORM.ERROR'));
  }

  function messageFor(code: string): string {
    // The one refusal the person can act on: their device did not check who
    // they were. Everything else is the Platform's uniform answer, and
    // elaborating on it here would undo what the uniformity is for.
    if (code === 'IDENTITY.PASSKEY_USER_VERIFICATION_REQUIRED') {
      return t('passkeyVerificationRequired');
    }

    if (code === 'IDENTITY.PASSKEY_COUNTER_WENT_BACKWARDS') {
      return t('passkeyRefused');
    }

    return t('passkeyFailed');
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-4">
        <span className="h-px flex-1 bg-border" />
        <span className="text-xs text-text-muted">{separatorLabel}</span>
        <span className="h-px flex-1 bg-border" />
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <Button
        type="button"
        onClick={() => void attempt()}
        busy={busy}
        busyLabel={t('passkeyWaiting')}
      >
        {t('signInWithPasskey')}
      </Button>

      <p className="text-xs leading-relaxed text-text-secondary">{t('passkeyExplainer')}</p>
    </div>
  );
}
