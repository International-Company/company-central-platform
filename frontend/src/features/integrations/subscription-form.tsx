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
 * **Event types are chosen, not typed.** This was a comma-separated text box,
 * and nothing checked what went into it: a typo was accepted and the
 * subscription then received nothing, silently and for ever. The list now comes
 * from the Platform, which also refuses a type it does not send.
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
  const [eventTypes, setEventTypes] = useState<string[]>(editing?.eventTypes ?? []);
  const [available, setAvailable] = useState<string[] | null>(null);
  const [secret, setSecret] = useState(editing?.secretReference ?? '');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [applicationsResponse, typesResponse] = await Promise.all([
        fetch('/api/applications'),
        fetch('/api/integrations/event-types'),
      ]);

      if (applicationsResponse.ok) {
        setApplications((await applicationsResponse.json()) as RegisteredApplicationDto[]);
      }

      setAvailable(typesResponse.ok ? ((await typesResponse.json()) as string[]) : []);
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

        eventTypes,

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
      case 'INTEGRATIONS.SUBSCRIPTION_EVENT_TYPES_UNKNOWN':
        return t('subscriptionEventTypesUnknown');
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

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-text">
          {t('subscriptionEvents')}{' '}
          <span className="text-text-secondary">({tCommon('required')})</span>
        </legend>

        <p className="text-sm text-text-secondary">{t('subscriptionEventsHint')}</p>

        {available === null ? (
          <p className="text-sm text-text-secondary">{tCommon('loading')}</p>
        ) : available.length === 0 ? (
          <p className="text-sm text-text-secondary">{t('subscriptionNoEventTypes')}</p>
        ) : (
          <div className="flex max-h-64 flex-col gap-1.5 overflow-y-auto rounded-md border border-border p-3">
            {choices(available, eventTypes).map((type) => (
              <label key={type} className="flex items-center gap-2 text-sm text-text">
                <input
                  type="checkbox"
                  checked={eventTypes.includes(type)}
                  onChange={() => setEventTypes(toggle(eventTypes, type))}
                  className="size-4"
                />
                {/* Event types are identifiers, not prose, and stay
                    left-to-right inside an Arabic layout. */}
                <span dir="ltr" className="font-mono text-xs">
                  {type}
                </span>
                {available.includes(type) ? null : (
                  <span className="text-xs text-text-secondary">
                    {t('subscriptionEventNoLongerSent')}
                  </span>
                )}
              </label>
            ))}
          </div>
        )}
      </fieldset>

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

/**
 * The types to offer: everything the Platform sends, plus anything an existing
 * subscription still names that it no longer does.
 *
 * Kept visible rather than dropped, so editing an old subscription shows what it
 * was asking for and lets somebody remove it deliberately. Saving with it still
 * ticked is refused by the Platform, which is the point.
 */
function choices(available: string[], selected: string[]): string[] {
  const retired = selected.filter((type) => !available.includes(type));

  return [...available, ...retired.sort()];
}

function toggle(values: string[], value: string): string[] {
  return values.includes(value) ? values.filter((v) => v !== value) : [...values, value];
}
