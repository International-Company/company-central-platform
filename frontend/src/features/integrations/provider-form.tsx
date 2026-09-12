'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog } from '@/components/shared/form-dialog';

/**
 * Registering an external service the Platform is allowed to call.
 *
 * **Three fields, and the third one is the allow-list's whole point.** A
 * provider is a code, a name and a base address; every call the Platform makes
 * to it is built from that address and a relative path, which is what stops an
 * endpoint definition from pointing somewhere the provider was never registered
 * for.
 *
 * **Registration is deliberately not where resilience is set.** A provider
 * arrives with the Platform's defaults and is tuned afterwards, because a form
 * that asked for five numbers before it would accept anything would be answered
 * by guessing — and the guesses would then look like decisions.
 */
export function ProviderForm({
  onSaved,
  onCancel,
}: {
  onSaved: (name: string) => void;
  onCancel: () => void;
}) {
  const t = useTranslations('integrations');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [baseAddress, setBaseAddress] = useState('https://');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);

    try {
      const response = await fetch('/api/integrations/providers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          code: code.trim(),
          name: name.trim(),
          baseAddress: baseAddress.trim(),
        }),
      });

      if (response.ok) {
        onSaved(name.trim());

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

  /**
   * The refusals worth naming.
   *
   * A taken code and a malformed address are both things the person can fix in
   * the box in front of them, and "something went wrong" would send them to look
   * for the problem somewhere else.
   */
  function messageFor(problemCode: string | undefined): string {
    switch (problemCode) {
      case 'INTEGRATIONS.PROVIDER_CODE_TAKEN':
        return t('codeTaken');
      case 'INTEGRATIONS.BASE_ADDRESS_INVALID':
        return t('addressInvalid');
      default:
        return tErrors('generic');
    }
  }

  return (
    <FormDialog
      open
      title={t('registerTitle')}
      description={t('registerHint')}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void save()}
      onCancel={onCancel}
    >
      <Field
        label={t('code')}
        value={code}
        onChange={(event) => setCode(event.target.value)}
        hint={t('codeHint')}
        maxLength={50}
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('name')}
        value={name}
        onChange={(event) => setName(event.target.value)}
        maxLength={200}
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('baseAddress')}
        value={baseAddress}
        onChange={(event) => setBaseAddress(event.target.value)}
        hint={t('baseAddressHint')}
        maxLength={500}
        required
        requiredLabel={tCommon('required')}
      />
    </FormDialog>
  );
}
