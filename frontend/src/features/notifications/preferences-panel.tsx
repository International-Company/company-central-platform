'use client';

import { useCallback, useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { FormMessage } from '@/components/ui/field';
import { StatusBadge } from '@/components/shared/status-badge';
import type { NotificationPreferenceDto } from '@/types/platform';

/**
 * What this person wants to receive.
 *
 * **A matrix of what can be turned off, not a list of what is stored.** The
 * Platform keeps a row only when somebody has said no, so reading the stored
 * rows would show a new employee an empty screen — and "nothing here" is a
 * terrible way to say "everything is on".
 *
 * Security is shown and cannot be changed, rather than hidden. Somebody looking
 * for "stop emailing me" should find out that these exist and why, instead of
 * concluding the setting is missing and asking an administrator to remove it.
 */
export function PreferencesPanel() {
  const t = useTranslations('notifications');
  const tErrors = useTranslations('errors');

  const [stored, setStored] = useState<NotificationPreferenceDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  /** The categories a person can decide about, and the one they cannot. */
  const categories = [
    { code: 'workflow', label: t('categoryWorkflow'), locked: false },
    { code: 'security', label: t('categorySecurity'), locked: true },
  ] as const;

  const channels = [
    { code: 'InApp', label: t('channelInApp') },
    { code: 'Email', label: t('channelEmail') },
  ] as const;

  const load = useCallback(async () => {
    setError(null);

    try {
      const response = await fetch('/api/me/notification-preferences');

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setStored((await response.json()) as NotificationPreferenceDto[]);
    } catch {
      setError(tErrors('network'));
    }
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  /** Absence means yes — the Platform stores a row only to say no. */
  function isEnabled(category: string, channel: string): boolean {
    const preference = (stored ?? []).find(
      (p) => p.category === category && p.channel === channel,
    );

    return preference?.isEnabled ?? true;
  }

  async function toggle(category: string, channel: string, next: boolean) {
    setBusy(`${category}:${channel}`);
    setError(null);

    try {
      const response = await fetch('/api/me/notification-preferences', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ category, channel, isEnabled: next }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as { detail?: string };

        // The Platform's own words when it has them: "security notifications
        // cannot be turned off" explains itself, and a generic banner would not.
        setError(body.detail ?? tErrors('generic'));

        return;
      }

      await load();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className="mt-8">
      <h2 className="text-base font-semibold text-text">{t('preferencesTitle')}</h2>

      <p className="mb-3 mt-0.5 max-w-prose text-sm text-text-secondary">
        {t('preferencesDescription')}
      </p>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {stored ? (
        <div className="overflow-x-auto rounded-md border border-border bg-surface">
          <table className="w-full text-sm">
            <caption className="sr-only">{t('preferencesTitle')}</caption>

            <thead>
              <tr className="border-b border-border">
                <th scope="col" className="p-3 text-start font-medium text-text">
                  {t('category')}
                </th>

                {channels.map((channel) => (
                  <th
                    key={channel.code}
                    scope="col"
                    className="p-3 text-start font-medium text-text"
                  >
                    {channel.label}
                  </th>
                ))}
              </tr>
            </thead>

            <tbody>
              {categories.map((category) => (
                <tr key={category.code} className="border-b border-border last:border-b-0">
                  <th scope="row" className="p-3 text-start font-medium text-text">
                    {category.label}

                    {category.locked ? (
                      <StatusBadge tone="neutral">{t('securityLocked')}</StatusBadge>
                    ) : null}
                  </th>

                  {channels.map((channel) => (
                    <td key={channel.code} className="p-3">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={
                            category.locked || isEnabled(category.code, channel.code)
                          }
                          // Shown and disabled rather than hidden: somebody
                          // looking for "stop emailing me" should learn that
                          // these exist and why they cannot be stopped.
                          disabled={
                            category.locked
                            || busy === `${category.code}:${channel.code}`
                          }
                          onChange={(event) =>
                            void toggle(category.code, channel.code, event.target.checked)
                          }
                          className="size-4 rounded border-border-strong"
                        />

                        <span className="text-text-secondary">{t('enabled')}</span>
                      </label>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
