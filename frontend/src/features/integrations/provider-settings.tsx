'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog } from '@/components/shared/form-dialog';
import type { IntegrationProviderDto } from '@/types/platform';

/**
 * How hard the Platform tries, what it blanks in the log, and which secret it
 * uses.
 *
 * **The credential field holds a name, never a value.** The Platform stores the
 * *reference* — `integrations/acme/api-key` — and resolves it at call time from
 * wherever the deployment keeps secrets. There is no column in the module that
 * could hold a value, so a database backup that leaks is not a credential leak.
 * Pasting a value here is refused by the Platform, and this form says why rather
 * than showing a generic failure: somebody who has just pasted an API key needs
 * to be told it was not stored, not that something went wrong.
 *
 * **The redaction list is applied before the row is written**, not before it is
 * read. Storing the real payload and hiding it at read time leaves the secret in
 * the database, where the next export and the next backup will find it.
 */
export function ProviderSettings({
  provider,
  onSaved,
  onCancel,
}: {
  provider: IntegrationProviderDto;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const t = useTranslations('integrations');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [timeout, setTimeoutSeconds] = useState(String(provider.timeoutSeconds));
  const [retries, setRetries] = useState(String(provider.maxRetries));
  const [failures, setFailures] = useState(String(provider.failuresBeforeBreaking));
  const [breakFor, setBreakFor] = useState(String(provider.breakDurationSeconds));
  const [concurrency, setConcurrency] = useState(String(provider.maxConcurrentCalls));
  const [redacted, setRedacted] = useState(provider.redactedFields.join(', '));
  const [credential, setCredential] = useState(provider.credentialReference ?? '');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/integrations/providers/${provider.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          timeoutSeconds: Number(timeout),
          maxRetries: Number(retries),
          failuresBeforeBreaking: Number(failures),
          breakDurationSeconds: Number(breakFor),
          maxConcurrentCalls: Number(concurrency),

          // Split on commas and thrown away if empty. An empty box means "blank
          // nothing", which is a real choice and different from leaving the list
          // as it was.
          redactedFields: redacted
            .split(',')
            .map((field) => field.trim())
            .filter((field) => field.length > 0),

          credentialReference: credential.trim() === '' ? null : credential.trim(),
        }),
      });

      if (response.ok) {
        onSaved();

        return;
      }

      const problem = (await response.json().catch(() => null)) as { code?: string } | null;

      setError(messageFor(problem?.code));
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  function messageFor(problemCode: string | undefined): string {
    switch (problemCode) {
      // The one refusal that must be named in full. Somebody who has just
      // pasted an API key into this box needs to know it was not stored and
      // what belongs there instead.
      case 'INTEGRATIONS.CREDENTIAL_VALUE_SUPPLIED':
        return t('credentialValueRefused');
      case 'INTEGRATIONS.TIMEOUT_OUT_OF_RANGE':
      case 'INTEGRATIONS.RETRIES_OUT_OF_RANGE':
      case 'INTEGRATIONS.RESILIENCE_VALUE_OUT_OF_RANGE':
        return t('resilienceOutOfRange');
      default:
        return tErrors('generic');
    }
  }

  return (
    <FormDialog
      open
      title={t('settingsTitle')}
      description={provider.name}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void save()}
      onCancel={onCancel}
    >
      <Field
        label={t('timeout')}
        type="number"
        value={timeout}
        onChange={(event) => setTimeoutSeconds(event.target.value)}
        hint={t('timeoutHint')}
        min={1}
      />

      <Field
        label={t('retries')}
        type="number"
        value={retries}
        onChange={(event) => setRetries(event.target.value)}
        hint={t('retriesHint')}
        min={0}
      />

      <Field
        label={t('failures')}
        type="number"
        value={failures}
        onChange={(event) => setFailures(event.target.value)}
        hint={t('failuresHint')}
        min={1}
      />

      <Field
        label={t('breakFor')}
        type="number"
        value={breakFor}
        onChange={(event) => setBreakFor(event.target.value)}
        hint={t('breakForHint')}
        min={1}
      />

      <Field
        label={t('concurrency')}
        type="number"
        value={concurrency}
        onChange={(event) => setConcurrency(event.target.value)}
        hint={t('concurrencyHint')}
        min={1}
      />

      <Field
        label={t('redactedFields')}
        value={redacted}
        onChange={(event) => setRedacted(event.target.value)}
        hint={t('redactedFieldsHint')}
        maxLength={1000}
      />

      <Field
        label={t('credentialReference')}
        value={credential}
        onChange={(event) => setCredential(event.target.value)}
        hint={t('credentialReferenceHint')}
        maxLength={200}

        // Not type="password". This is a name, and hiding it would teach the
        // person that a value belongs here.
        autoComplete="off"
      />
    </FormDialog>
  );
}
