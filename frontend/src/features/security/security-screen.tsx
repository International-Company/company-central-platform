'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { FormDialog } from '@/components/shared/form-dialog';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import { SessionsPanel } from './sessions-panel';
import { SecurityEventsPanel } from './security-events-panel';
import type { MfaEnrolmentDto, MfaStatusDto, RecoveryCodesDto } from '@/types/platform';

/**
 * The signed-in person's own second factor.
 *
 * **Not an administration screen.** It shows one account — the caller's — and
 * needs no permission, because reading and changing one's own second factor is
 * not something an administrator grants. An administrator-initiated reset is a
 * different feature with different risks, and it does not exist yet.
 *
 * This screen matters more than it looks: granting a role demands a recent
 * second factor, so an administrator who never enrolled cannot create another
 * administrator. The Platform is administrable only through here.
 */
export function SecurityScreen() {
  const t = useTranslations('security');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [status, setStatus] = useState<MfaStatusDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [enrolment, setEnrolment] = useState<MfaEnrolmentDto | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const [disabling, setDisabling] = useState(false);

  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch('/api/auth/mfa');

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setStatus((await response.json()) as MfaStatusDto);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function beginEnrolment() {
    setBusy(true);
    setError(null);

    try {
      const response = await fetch('/api/auth/mfa/enrol', { method: 'POST' });

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setCode('');
      setDialogError(null);
      setEnrolment((await response.json()) as MfaEnrolmentDto);
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function confirmEnrolment() {
    setBusy(true);
    setDialogError(null);

    try {
      const response = await fetch('/api/auth/mfa/confirm', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code }),
      });

      if (!response.ok) {
        setDialogError(
          response.status === 400 || response.status === 422
            ? t('invalidCode')
            : tErrors('generic'),
        );

        return;
      }

      const body = (await response.json()) as RecoveryCodesDto;

      setEnrolment(null);

      // Held in component state and shown immediately. They are stored hashed,
      // so this response is the only moment they exist in readable form — not
      // written anywhere, not logged, and gone when the person closes the panel.
      setRecoveryCodes(body.codes);

      await load();
    } catch {
      setDialogError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function disable() {
    setBusy(true);
    setDialogError(null);

    try {
      const response = await fetch('/api/auth/mfa/disable', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code }),
      });

      if (!response.ok) {
        setDialogError(
          response.status === 400 || response.status === 422
            ? t('invalidCode')
            : tErrors('generic'),
        );

        return;
      }

      setDisabling(false);
      await load();
    } catch {
      setDialogError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader title={t('title')} description={t('description')} />

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {loading && !status ? (
        <p role="status" className="text-sm text-text-secondary">
          {tCommon('loading')}
        </p>
      ) : status ? (
        <section className="rounded-md border border-border bg-surface p-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="text-sm font-semibold text-text">
                {t('mfaTitle')}
              </h2>

              <p className="mt-1 max-w-prose text-sm text-text-secondary">
                {t('mfaWhy')}
              </p>
            </div>

            <StatusBadge tone={status.isActive ? 'success' : 'warning'}>
              {status.isActive ? t('mfaEnrolled') : t('mfaNotEnrolled')}
            </StatusBadge>
          </div>

          {status.isActive ? (
            <dl className="mt-4 grid gap-2 text-sm sm:grid-cols-2">
              <div>
                <dt className="text-text-secondary">{t('recoveryRemaining')}</dt>
                <dd className="font-medium text-text">
                  {format.number(Number(status.remainingRecoveryCodes))}
                </dd>
              </div>
            </dl>
          ) : null}

          <div className="mt-4 flex flex-wrap gap-2">
            {status.isActive ? (
              <Button
                onClick={() => {
                  setCode('');
                  setDialogError(null);
                  setDisabling(true);
                }}
              >
                {t('disable')}
              </Button>
            ) : (
              <Button
                variant="primary"
                busy={busy && !enrolment}
                busyLabel={tCommon('loading')}
                onClick={() => void beginEnrolment()}
              >
                {t('enrol')}
              </Button>
            )}
          </div>
        </section>
      ) : null}

      {recoveryCodes ? (
        <section className="mt-4 rounded-md border border-border-strong bg-surface p-4">
          <h2 className="text-sm font-semibold text-text">
            {t('recoveryTitle')}
          </h2>

          <p className="mt-1 max-w-prose text-sm text-text-secondary">
            {t('recoveryDescription')}
          </p>

          <ul className="mt-3 grid gap-1 font-mono text-sm sm:grid-cols-2" dir="ltr">
            {recoveryCodes.map((recoveryCode) => (
              <li key={recoveryCode} className="rounded bg-surface-sunken px-2 py-1">
                {recoveryCode}
              </li>
            ))}
          </ul>

          <Button
            className="mt-3"
            onClick={() => setRecoveryCodes(null)}
          >
            {t('recoveryDone')}
          </Button>
        </section>
      ) : null}

      {/* One's own security below one's own second factor, then the Platform's
          log — which needs a permission and is therefore not everyone's. The
          order is by who it belongs to: you, then the company. */}
      <SessionsPanel />

      <SecurityEventsPanel />

      <FormDialog
        open={enrolment !== null}
        title={t('enrolTitle')}
        submitLabel={t('confirm')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={dialogError}
        onSubmit={() => void confirmEnrolment()}
        onCancel={() => setEnrolment(null)}
      >
        <p className="text-sm text-text-secondary">{t('enrolStep1')}</p>

        {/* The key in text rather than only a QR image. A code that can only be
            scanned excludes anyone using a desktop authenticator, and anyone
            whose camera cannot read the screen they are reading it from. */}
        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium text-text">{t('manualKey')}</span>

          <code
            dir="ltr"
            className="select-all break-all rounded-md bg-surface-sunken px-3 py-2 font-mono text-sm text-text"
          >
            {enrolment?.manualEntryKey}
          </code>
        </div>

        <p className="text-sm text-text-secondary">{t('enrolStep2')}</p>

        <Field
          label={t('code')}
          value={code}
          onChange={(event) => setCode(event.target.value)}
          autoComplete="one-time-code"
          inputMode="numeric"
          dir="ltr"
          required
          requiredLabel={tCommon('required')}
        />
      </FormDialog>

      <FormDialog
        open={disabling}
        title={t('disableTitle')}
        description={t('disableDescription')}
        submitLabel={t('disable')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={dialogError}
        onSubmit={() => void disable()}
        onCancel={() => setDisabling(false)}
      >
        <Field
          label={t('code')}
          value={code}
          onChange={(event) => setCode(event.target.value)}
          autoComplete="one-time-code"
          inputMode="numeric"
          dir="ltr"
          required
          requiredLabel={tCommon('required')}
        />
      </FormDialog>
    </>
  );
}
