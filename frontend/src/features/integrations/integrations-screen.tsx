'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge, type StatusTone } from '@/components/shared/status-badge';
import { Button } from '@/components/ui/button';
import { IfPermitted } from '@/lib/permissions';
import { IntegrationCallLog } from './integration-call-log';
import { ProviderForm } from './provider-form';
import { ProviderSettings } from './provider-settings';
import { SubscriptionDeliveries } from './subscription-deliveries';
import { SubscriptionForm } from './subscription-form';
import type {
  IntegrationHealthDto,
  IntegrationProviderDto,
  WebhookSubscriptionDto,
} from '@/types/platform';

/**
 * The external services the Platform is allowed to call.
 *
 * **Built around the question an operator opens it to answer during an
 * incident:** is this provider working, and where is the switch. Health comes
 * first because that is what the page is for; the configuration is one row
 * below.
 *
 * **No credential value appears anywhere on this screen**, and not because the
 * screen hides one — the Platform stores the *name* of a secret and has no field
 * that could carry a value. There is nothing here to leak.
 */
export function IntegrationsScreen() {
  const t = useTranslations('integrations');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [providers, setProviders] = useState<IntegrationProviderDto[]>([]);
  const [health, setHealth] = useState<IntegrationHealthDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [registering, setRegistering] = useState(false);
  const [configuring, setConfiguring] = useState<IntegrationProviderDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // The other direction: who has asked to be told when something happens.
  const [subscriptions, setSubscriptions] = useState<WebhookSubscriptionDto[]>([]);
  const [subscribing, setSubscribing] = useState<{ editing: WebhookSubscriptionDto | null } | null>(
    null,
  );
  const [deliveries, setDeliveries] = useState<WebhookSubscriptionDto | null>(null);

  // When health was read, not when the component last rendered. Health is
  // derived from the last hour of calls and nothing on this page refreshes it,
  // so an operator watching during an incident needs the stamp to go stale in
  // front of them. A live clock would say the figures were current when they
  // were minutes old.
  const [checkedAt, setCheckedAt] = useState<Date | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const [providersResponse, healthResponse, subscriptionsResponse] = await Promise.all([
        fetch('/api/integrations/providers'),
        fetch('/api/integrations/providers/health'),
        fetch('/api/integrations/subscriptions'),
      ]);

      if (!providersResponse.ok) {
        setError(tErrors('generic'));

        return;
      }

      setProviders((await providersResponse.json()) as IntegrationProviderDto[]);

      if (healthResponse.ok) {
        setHealth((await healthResponse.json()) as IntegrationHealthDto[]);
        setCheckedAt(new Date());
      }

      if (subscriptionsResponse.ok) {
        setSubscriptions((await subscriptionsResponse.json()) as WebhookSubscriptionDto[]);
      }
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function setEnabled(provider: IntegrationProviderDto, isEnabled: boolean) {
    try {
      const response = await fetch(`/api/integrations/providers/${provider.id}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isEnabled }),
      });

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      await load();
    } catch {
      setError(tErrors('network'));
    }
  }

  /**
   * The tone for a provider's status.
   *
   * `Idle` is neutral rather than green. Nothing has been asked of it, so
   * nothing is known — and a green light nobody earned is worse than an honest
   * blank.
   */
  async function act(path: string, method: string, body?: unknown, noticeKey?: string) {
    try {
      const response = await fetch(path, {
        method,
        ...(body === undefined
          ? {}
          : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }),
      });

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      if (noticeKey) {
        setNotice(t(noticeKey as never));
      }

      await load();
    } catch {
      setError(tErrors('network'));
    }
  }

  function toneFor(status: string): StatusTone {
    if (status === 'Healthy') {
      return 'success';
    }

    if (status === 'Failing' || status === 'Disabled') {
      return 'danger';
    }

    return status === 'Degraded' ? 'warning' : 'neutral';
  }

  const columns: Column<IntegrationProviderDto>[] = [
    {
      key: 'name',
      header: t('provider'),
      render: (provider) => (
        <div>
          <p className="font-medium text-text">{provider.name}</p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{provider.baseAddress}</p>
        </div>
      ),
    },
    {
      key: 'health',
      header: t('statusHeading'),
      render: (provider) => {
        const state = health.find((h) => h.providerCode === provider.code);
        const status = state?.status ?? (provider.isEnabled ? 'Idle' : 'Disabled');

        return (
          <StatusBadge tone={toneFor(status)}>
            {t(`health.${status}` as never)}
          </StatusBadge>
        );
      },
    },
    {
      key: 'recent',
      header: t('recentCalls'),
      render: (provider) => {
        const state = health.find((h) => h.providerCode === provider.code);

        if (!state) {
          return tCommon('none');
        }

        // Failures out of calls rather than a percentage: an operator reading
        // "3 of 4" knows immediately whether the sample is worth anything, and
        // "75%" does not say it was four calls.
        return `${state.recentFailures} / ${state.recentCalls}`;
      },
      numeric: true,
      secondary: true,
    },
    {
      key: 'credential',
      header: t('credential'),
      render: (provider) => (
        // The reference, never a value. There is no field in the Platform that
        // could hold one.
        <span className="font-mono text-xs text-text-secondary">
          {provider.credentialReference ?? t('noCredential')}
        </span>
      ),
      secondary: true,
    },
    {
      key: 'resilience',
      header: t('resilience'),
      render: (provider) =>
        t('resilienceSummary', {
          timeout: provider.timeoutSeconds,
          retries: provider.maxRetries,
        }),
      secondary: true,
    },
  ];

  const subscriptionColumns: Column<WebhookSubscriptionDto>[] = [
    {
      key: 'name',
      header: t('subscription'),
      render: (subscription) => (
        <div>
          <p className="text-sm font-medium text-text">{subscription.name}</p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{subscription.endpoint}</p>
        </div>
      ),
    },
    {
      key: 'events',
      header: t('subscriptionEvents'),
      render: (subscription) => (
        // Listed, not counted. Which events a system receives is the whole
        // content of a subscription, and a number says nothing anybody needs.
        <ul className="flex flex-col gap-0.5">
          {subscription.eventTypes.map((eventType) => (
            <li key={eventType} className="font-mono text-xs text-text-secondary">
              {eventType}
            </li>
          ))}
        </ul>
      ),
    },
    {
      key: 'state',
      header: t('statusHeading'),
      render: (subscription) => (
        <div>
          <StatusBadge
            tone={
              subscription.suspendedAt
                ? 'danger'
                : subscription.isEnabled
                  ? 'success'
                  : 'neutral'
            }
          >
            {subscription.suspendedAt
              ? t('suspended')
              : subscription.isEnabled
                ? t('on')
                : t('off')}
          </StatusBadge>

          {/*
            A suspended subscription is a business system that has silently
            stopped hearing about anything, so the reason is on the row rather
            than a click away.
          */}
          {subscription.suspendedAt ? (
            <p className="mt-1 text-xs text-attention">{subscription.suspendedReason}</p>
          ) : null}
        </div>
      ),
    },
    {
      key: 'secret',
      header: t('credential'),
      render: (subscription) => (
        // The reference, never a value. There is no field in the Platform that
        // could hold one.
        <span className="font-mono text-xs text-text-secondary">
          {subscription.secretReference}
        </span>
      ),
      secondary: true,
    },
    {
      key: 'lastDelivered',
      header: t('lastDelivered'),
      render: (subscription) =>
        subscription.lastDeliveredAt
          ? format.dateTime(new Date(subscription.lastDeliveredAt), {
              dateStyle: 'short',
              timeStyle: 'short',
            })
          : t('neverDelivered'),
      secondary: true,
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        action={
          // Hidden from somebody who could not do it anyway. The Platform
          // refuses the call regardless; this only spares them the surprise.
          <IfPermitted permission="platform.integrations.manage">
            <Button variant="primary" onClick={() => setRegistering(true)}>
              {t('register')}
            </Button>
          </IfPermitted>
        }
      />

      {notice ? <FormMessage tone="success">{notice}</FormMessage> : null}
      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {loading && providers.length === 0 ? (
        <p className="text-sm text-text-secondary">{tCommon('loading')}</p>
      ) : (
        <DataTable
          columns={columns}
          rows={providers}
          rowKey={(provider) => provider.id}
          caption={t('title')}
          labels={{
            noResults: t('noProviders'),
            noResultsDescription: t('noProvidersDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
          rowActions={(provider) => (
            <>
              <button
                type="button"
                className="text-sm font-medium text-primary-700 hover:underline"
                onClick={() =>
                  setSelected(selected === provider.code ? null : provider.code)
                }
              >
                {t('calls')}
              </button>

              <IfPermitted permission="platform.integrations.manage">
                <button
                  type="button"
                  className="text-sm font-medium text-primary-700 hover:underline"
                  onClick={() => setConfiguring(provider)}
                >
                  {t('settings')}
                </button>
              </IfPermitted>

              <button
                type="button"
                className={
                  provider.isEnabled
                    ? 'text-sm font-medium text-attention hover:underline'
                    : 'text-sm font-medium text-primary-700 hover:underline'
                }
                onClick={() => void setEnabled(provider, !provider.isEnabled)}
              >
                {provider.isEnabled ? t('disable') : t('enable')}
              </button>
            </>
          )}
        />
      )}

      {registering ? (
        <ProviderForm
          onSaved={(name) => {
            setRegistering(false);
            setNotice(t('registered', { name }));
            void load();
          }}
          onCancel={() => setRegistering(false)}
        />
      ) : null}

      {configuring ? (
        <ProviderSettings
          provider={configuring}
          onSaved={() => {
            setNotice(t('settingsSaved', { name: configuring.name }));
            setConfiguring(null);
            void load();
          }}
          onCancel={() => setConfiguring(null)}
        />
      ) : null}

      <section>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-base font-semibold text-text">{t('subscriptions')}</h2>

          <IfPermitted permission="platform.integrations.manage">
            <Button variant="secondary" onClick={() => setSubscribing({ editing: null })}>
              {t('subscribe')}
            </Button>
          </IfPermitted>
        </div>

        <p className="mb-3 text-sm text-text-secondary">{t('subscriptionsHint')}</p>

        <DataTable
          columns={subscriptionColumns}
          rows={subscriptions}
          rowKey={(subscription) => subscription.id}
          caption={t('subscriptions')}
          labels={{
            noResults: t('noSubscriptions'),
            noResultsDescription: t('noSubscriptionsDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
          rowActions={(subscription) => (
            <>
              <button
                type="button"
                className="text-sm font-medium text-primary-700 hover:underline"
                onClick={() =>
                  setDeliveries(deliveries?.id === subscription.id ? null : subscription)
                }
              >
                {t('deliveries')}
              </button>

              <IfPermitted permission="platform.integrations.manage">
                <button
                  type="button"
                  className="text-sm font-medium text-primary-700 hover:underline"
                  onClick={() => setSubscribing({ editing: subscription })}
                >
                  {tCommon('edit')}
                </button>
              </IfPermitted>

              {subscription.suspendedAt ? (
                <IfPermitted permission="platform.integrations.manage">
                  <button
                    type="button"
                    className="text-sm font-medium text-primary-700 hover:underline"
                    onClick={() =>
                      void act(
                        `/api/integrations/subscriptions/${subscription.id}/resume`,
                        'POST',
                        undefined,
                        'subscriptionResumed',
                      )
                    }
                  >
                    {t('resume')}
                  </button>
                </IfPermitted>
              ) : (
                <IfPermitted permission="platform.integrations.manage">
                  <button
                    type="button"
                    className={
                      subscription.isEnabled
                        ? 'text-sm font-medium text-attention hover:underline'
                        : 'text-sm font-medium text-primary-700 hover:underline'
                    }
                    onClick={() =>
                      void act(
                        `/api/integrations/subscriptions/${subscription.id}/status`,
                        'PUT',
                        { isEnabled: !subscription.isEnabled },
                      )
                    }
                  >
                    {subscription.isEnabled ? t('disable') : t('enable')}
                  </button>
                </IfPermitted>
              )}
            </>
          )}
        />
      </section>

      {deliveries ? (
        <SubscriptionDeliveries
          subscriptionId={deliveries.id}
          subscriptionName={deliveries.name}
          onClose={() => setDeliveries(null)}
        />
      ) : null}

      {subscribing ? (
        <SubscriptionForm
          editing={subscribing.editing}
          onSaved={(name) => {
            setSubscribing(null);
            setNotice(t('subscriptionSaved', { name }));
            void load();
          }}
          onCancel={() => setSubscribing(null)}
        />
      ) : null}

      {selected ? (
        <IntegrationCallLog providerCode={selected} onClose={() => setSelected(null)} />
      ) : null}

      {checkedAt ? (
        <p className="text-sm text-text-secondary">
          {t('healthWindow', {
            checked: format.dateTime(checkedAt, { timeStyle: 'medium' }),
          })}
        </p>
      ) : null}
    </div>
  );
}
