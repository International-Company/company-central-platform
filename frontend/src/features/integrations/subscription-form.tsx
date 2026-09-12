'use client';

import { useCallback, useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog } from '@/components/shared/form-dialog';
import type { RegisteredApplicationDto, WebhookSubscriptionDto } from '@/types/platform';

/**
 * Asking the Platform to tell a business system when something happens.
 *
 * **A subscription is a standing instruction to send the company's events to an
 * address somebody chose**, which is why it is administered here rather than by
 * the subscribing application itself, and why the form asks for the application
 * by name rather than by identifier.
 *
 * **The secret field holds a name, never a value.** The Platform signs with a
 * secret it resolves at delivery time; there is no field anywhere in the module
 * that could carry one. The hint says so, and so does the refusal.
 */
export function SubscriptionForm({
  editing,
  onSaved,
  onCancel,
}: {
  editing: WebhookSubscriptionDto | null;
  onSaved: (name: string) => void;
  onCancel: () => void;
}) {
  const t = useTranslations('integrations');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [applications, setApplications] = useState<RegisteredApplicationDto[]>([]);
  const [applicationId, setApplicationId] = useState(editing?.applicationId ?? '');
  const [name, setName] = useState(editing?.name ?? '');
  const [endpoint, setEndpoint] = useState(editing?.endpoint ?? 'https://');
  const [eventTypes, setEventTypes] = useState((editing?.eventTypes ?? []).join(', '));
  const [secret, setSecret] = useState(editing?.secretReference ?? '');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await fetch('/api/applications');

      if (response.ok) {
        setApplications((await response.json()) as RegisteredApplicationDto[]);
      }
    } catch {
      // The dialog stays usable for an edit, where the application is already
      // chosen. Refusing to open because a list did not load would be worse
      // than offering less.
      setError(tErrors('network'));
    }
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function save() {
    setBusy(true);
    setError(null);

    try {
      const body = JSON.stringify({
        applicationId,
        name: name.trim(),
        endpoint: endpoint.trim(),

        // Split on commas and emptied of blanks. The Platform normalises,
        // deduplicates and orders them; this only has to stop sending noise.
        eventTypes: eventTypes
          .split(',')
          .map((type) => type.trim())
          .filter((type) => type.length > 0),

        secretReference: secret.trim(),
      });

      const response = await fetch(
        editing
          ? `/api/integrations/subscriptions/${editing.id}`
          : '/api/integrations/subscriptions',
        {
          method: editing ? 'PUT' : 'POST',
          headers: { 'Content-Type': 'application/json' },
          body,
        },
      );

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
   * Each is something the person can fix in the box in front of them, and
   * "something went wrong" would send them looking elsewhere — most expensively
   * in the case of an address the allow-list does not carry, which is a
   * configuration change rather than a typing mistake.
   */
  function messageFor(code: string | undefined): string {
    switch (code) {
      case 'INTEGRATIONS.SUBSCRIPTION_ENDPOINT_REFUSED':
        return t('subscriptionEndpointRefused');
      case 'INTEGRATIONS.SUBSCRIPTION_ENDPOINT_INVALID':
        return t('subscriptionEndpointInvalid');
      case 'INTEGRATIONS.SUBSCRIPTION_EVENTS_REQUIRED':
        return t('subscriptionEventsRequired');
      case 'INTEGRATIONS.SUBSCRIPTION_SECRET_REQUIRED':
        return t('subscriptionSecretRequired');
      default:
        return tErrors('generic');
    }
  }

  return (
    <FormDialog
      open
      title={editing ? t('subscriptionEditTitle') : t('subscriptionCreateTitle')}
      description={t('subscriptionHint')}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void save()}
      onCancel={onCancel}
    >
      {editing ? null : (
        <div className="flex flex-col gap-1.5">
          <label htmlFor="subscription-application" className="text-sm font-medium text-text">
            {t('subscriptionApplication')}
          </label>

          <select
            id="subscription-application"
            value={applicationId}
            onChange={(event) => setApplicationId(event.target.value)}
            className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
          >
            <option value="">{t('subscriptionChooseApplication')}</option>

            {applications.map((application) => (
              <option key={application.id} value={application.id}>
                {application.name}
              </option>
            ))}
          </select>
        </div>
      )}

      <Field
        label={t('name')}
        value={name}
        onChange={(event) => setName(event.target.value)}
        maxLength={200}
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('subscriptionEndpoint')}
        value={endpoint}
        onChange={(event) => setEndpoint(event.target.value)}
        hint={t('subscriptionEndpointHint')}
        maxLength={500}
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('subscriptionEvents')}
        value={eventTypes}
        onChange={(event) => setEventTypes(event.target.value)}
        hint={t('subscriptionEventsHint')}
        maxLength={1000}
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('subscriptionSecret')}
        value={secret}
        onChange={(event) => setSecret(event.target.value)}
        hint={t('subscriptionSecretHint')}
        maxLength={200}
        required
        requiredLabel={tCommon('required')}
        // Not type="password". This is a name, and masking it would teach the
        // person that a value belongs here.
        autoComplete="off"
      />
    </FormDialog>
  );
}
