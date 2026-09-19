'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { ConfirmDialog } from '@/components/shared/confirm-dialog';
import { FormDialog } from '@/components/shared/form-dialog';
import { DataTable, type Column } from '@/components/shared/data-table';
import { EmptyValue } from '@/components/shared/empty-value';
import { deviceCanVerifyUser, registerPasskey } from '@/lib/passkeys';
import type { PasskeyDto } from '@/types/platform';

/**
 * The devices that can sign in as this person, and adding or removing one.
 *
 * **The password is asked for before a passkey is added**, and the reason is
 * worth saying on the screen rather than only in the code: a passkey is a new
 * way into the account that outlives a password change, so adding one is
 * exactly what somebody holding a stolen session would do to keep their way in.
 *
 * **Removing one is never behind anything.** It is what a person does the
 * moment a laptop is lost, and a control you cannot reach in a hurry is a
 * control you do not have.
 */
export function PasskeysPanel() {
  const t = useTranslations('security');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tAuth = useTranslations('auth');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [supported, setSupported] = useState<boolean | null>(null);
  const [passkeys, setPasskeys] = useState<PasskeyDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [adding, setAdding] = useState(false);
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);

  const [removing, setRemoving] = useState<PasskeyDto | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await fetch('/api/me/passkeys');

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setPasskeys((await response.json()) as PasskeyDto[]);
    } catch {
      setError(tErrors('network'));
    }
  }, [tErrors]);

  useEffect(() => {
    void deviceCanVerifyUser().then(setSupported);
    void load();
  }, [load]);

  async function add() {
    setBusy(true);
    setDialogError(null);

    const outcome = await registerPasskey(password, name.trim() || t('passkeyDefaultName'));

    setBusy(false);

    if (outcome.kind === 'registered') {
      setAdding(false);
      setPassword('');
      setName('');
      setNotice(t('passkeyAdded'));

      await load();

      return;
    }

    if (outcome.kind === 'cancelled') {
      // The device dialog was closed. The password was already accepted, so
      // leaving the form open with it filled in is what somebody who meant to
      // try again would want.
      return;
    }

    setDialogError(messageFor(outcome.kind === 'failed' ? outcome.code : 'PLATFORM.ERROR'));
  }

  function messageFor(code: string): string {
    switch (code) {
      case 'IDENTITY.CURRENT_PASSWORD_INCORRECT':
        return t('passkeyWrongPassword');
      case 'IDENTITY.PASSKEY_ALREADY_REGISTERED':
        return t('passkeyAlreadyRegistered');
      case 'IDENTITY.PASSKEY_USER_VERIFICATION_REQUIRED':
        return t('passkeyVerificationRequired');
      default:
        return tErrors('generic');
    }
  }

  async function remove(passkey: PasskeyDto) {
    setRemoving(null);
    setNotice(null);

    const response = await fetch(`/api/me/passkeys/${passkey.id}`, { method: 'DELETE' });

    if (!response.ok) {
      setError(tErrors('generic'));

      return;
    }

    setNotice(t('passkeyRemoved', { name: passkey.name }));

    await load();
  }

  const columns: Column<PasskeyDto>[] = [
    {
      key: 'name',
      header: t('passkeyName'),
      render: (passkey) => <span className="font-medium">{passkey.name}</span>,
    },
    {
      key: 'created',
      header: t('passkeyAddedOn'),
      render: (passkey) =>
        format.dateTime(new Date(passkey.createdAt), { dateStyle: 'medium' }),
    },
    {
      key: 'lastUsed',
      header: t('passkeyLastUsed'),
      secondary: true,
      render: (passkey) =>
        passkey.lastUsedAt
          ? format.dateTime(new Date(passkey.lastUsedAt), { dateStyle: 'medium' })
          : <EmptyValue />,
    },
  ];

  return (
    <section aria-labelledby="passkeys-heading" className="mt-10">
      <div className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-2 border-b border-border pb-2.5">
        <h2 id="passkeys-heading" className="text-base font-semibold text-text">
          {t('passkeys')}
        </h2>

        {supported ? (
          <Button size="sm" onClick={() => setAdding(true)}>
            {t('addPasskey')}
          </Button>
        ) : null}
      </div>

      <p className="mt-3 max-w-2xl text-sm leading-relaxed text-text-secondary">
        {t('passkeysExplainer')}
      </p>

      {supported === false ? (
        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-text-muted">
          {t('passkeysUnsupported')}
        </p>
      ) : null}

      {error ? (
        <div className="mt-4">
          <FormMessage tone="error">{error}</FormMessage>
        </div>
      ) : null}

      {notice ? (
        <div className="mt-4">
          <FormMessage tone="success">{notice}</FormMessage>
        </div>
      ) : null}

      <div className="mt-4">
        <DataTable
          columns={columns}
          rows={passkeys ?? []}
          rowKey={(passkey) => passkey.id}
          caption={t('passkeys')}
          labels={{
            noResults: t('noPasskeys'),
            noResultsDescription: t('noPasskeysDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
          rowActions={(passkey) => (
            <Button variant="quiet" size="sm" onClick={() => setRemoving(passkey)}>
              {tCommon('delete')}
            </Button>
          )}
        />
      </div>

      <FormDialog
        open={adding}
        title={t('addPasskey')}
        submitLabel={t('addPasskey')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={t('passkeyWaiting')}
        {...(dialogError ? { error: dialogError } : {})}
        onSubmit={() => void add()}
        onCancel={() => {
          setAdding(false);
          setDialogError(null);
        }}
      >
        <div className="flex flex-col gap-4">
          <p className="text-sm leading-relaxed text-text-secondary">
            {t('addPasskeyLead')}
          </p>

          <Field
            label={t('passkeyName')}
            value={name}
            onChange={(event) => setName(event.target.value)}
            hint={t('passkeyNameHint')}
            maxLength={64}
          />

          <Field
            label={tAuth('currentPassword')}
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            hint={t('passkeyPasswordHint')}
            required
            requiredLabel={tCommon('required')}
          />
        </div>
      </FormDialog>

      <ConfirmDialog
        open={removing !== null}
        title={t('removePasskey')}
        description={t('removePasskeyConfirm', { name: removing?.name ?? '' })}
        destructive
        confirmLabel={tCommon('delete')}
        cancelLabel={tCommon('cancel')}
        onConfirm={() => {
          if (removing) {
            void remove(removing);
          }
        }}
        onCancel={() => setRemoving(null)}
      />
    </section>
  );
}
