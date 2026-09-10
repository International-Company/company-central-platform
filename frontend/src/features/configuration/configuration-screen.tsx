'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { FormDialog } from '@/components/shared/form-dialog';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import { FlagTargeting } from './flag-targeting';
import { SettingHistory } from './setting-history';
import type { FeatureFlagDto, SettingDto } from '@/types/platform';

/**
 * Settings and switches.
 *
 * **A sensitive setting's value is not on this screen, and not because the
 * screen hides it.** The Platform's own response shape has no field for it, so
 * there is nothing here that could be shown by forgetting to check.
 */
export function ConfigurationScreen() {
  const t = useTranslations('configuration');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [settings, setSettings] = useState<SettingDto[]>([]);
  const [flags, setFlags] = useState<FeatureFlagDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [history, setHistory] = useState<string | null>(null);
  const [targeting, setTargeting] = useState<FeatureFlagDto | null>(null);

  const [editing, setEditing] = useState<SettingDto | null>(null);
  const [value, setValue] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const [settingsResponse, flagsResponse] = await Promise.all([
        fetch('/api/configuration/settings'),
        fetch('/api/configuration/flags'),
      ]);

      if (!settingsResponse.ok) {
        setError(tErrors('generic'));

        return;
      }

      setSettings((await settingsResponse.json()) as SettingDto[]);

      if (flagsResponse.ok) {
        setFlags((await flagsResponse.json()) as FeatureFlagDto[]);
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

  async function save() {
    if (!editing) {
      return;
    }

    setBusy(true);
    setFormError(null);

    try {
      const response = await fetch('/api/configuration/settings/value', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          key: editing.key,
          scope: 'Platform',
          scopeId: null,
          value: value.trim() === '' ? null : value,
          reason: reason.trim() === '' ? null : reason.trim(),
        }),
      });

      if (response.ok) {
        setEditing(null);
        setValue('');
        setReason('');
        setNotice(t('saved', { key: editing.key }));
        await load();

        return;
      }

      const problem = (await response.json().catch(() => null)) as { code?: string } | null;

      // The refusal worth naming in full. Somebody pasting a credential into a
      // settings field needs to know it is not a validation quibble — a setting
      // is stored in plaintext, exported and backed up.
      setFormError(
        problem?.code === 'CONFIG.SECRET_SHAPED_VALUE'
          ? t('secretRefused')
          : tErrors('generic'),
      );
    } catch {
      setFormError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function toggleFlag(flag: FeatureFlagDto) {
    try {
      const response = await fetch(
        `/api/configuration/flags/${encodeURIComponent(flag.key)}`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            isEnabled: !flag.isEnabled,
            roleIds: flag.targetedRoles,
            unitIds: flag.targetedUnits,
          }),
        },
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      await load();
    } catch {
      setError(tErrors('network'));
    }
  }

  /** What is in force at Platform scope, or the declared default. */
  function effective(setting: SettingDto): string {
    if (setting.isSensitive) {
      return t('sensitiveValue');
    }

    const platform = setting.values.find((v) => v.scope === 'Platform');

    return platform?.value ?? setting.defaultValue ?? tCommon('none');
  }

  const settingColumns: Column<SettingDto>[] = [
    {
      key: 'key',
      header: t('setting'),
      render: (setting) => (
        <div>
          <p className="font-mono text-sm font-medium text-text">{setting.key}</p>

          {setting.description ? (
            <p className="mt-0.5 text-sm text-text-secondary">{setting.description}</p>
          ) : null}
        </div>
      ),
    },
    {
      key: 'value',
      header: t('inForce'),
      render: (setting) => (
        <span className="font-mono text-sm text-text">{effective(setting)}</span>
      ),
    },
    {
      key: 'type',
      header: t('type'),
      render: (setting) => setting.valueType,
      secondary: true,
    },
    {
      key: 'overrides',
      header: t('overrides'),
      render: (setting) =>
        setting.values.length === 0 ? t('atDefault') : String(setting.values.length),
      numeric: true,
      secondary: true,
    },
    {
      key: 'sensitive',
      header: t('statusHeading'),
      render: (setting) =>
        setting.isSensitive ? (
          <StatusBadge tone="warning">{t('sensitive')}</StatusBadge>
        ) : (
          <StatusBadge tone="neutral">{t('ordinary')}</StatusBadge>
        ),
      secondary: true,
    },
  ];

  const flagColumns: Column<FeatureFlagDto>[] = [
    {
      key: 'key',
      header: t('flag'),
      render: (flag) => (
        <div>
          <p className="font-mono text-sm font-medium text-text">{flag.key}</p>

          {flag.description ? (
            <p className="mt-0.5 text-sm text-text-secondary">{flag.description}</p>
          ) : null}
        </div>
      ),
    },
    {
      key: 'state',
      header: t('statusHeading'),
      render: (flag) => (
        <StatusBadge tone={flag.isEnabled ? 'success' : 'neutral'}>
          {flag.isEnabled ? t('on') : t('off')}
        </StatusBadge>
      ),
    },
    {
      key: 'reach',
      header: t('reach'),
      render: (flag) =>
        // "Everybody" is the honest word for an untargeted flag that is on, and
        // saying "no targets" would read as though it reached nobody.
        flag.isUntargeted
          ? t('everybody')
          : t('targeted', {
              roles: flag.targetedRoles.length,
              units: flag.targetedUnits.length,
            }),
    },
    {
      key: 'updated',
      header: t('updated'),
      render: (flag) =>
        format.dateTime(new Date(flag.updatedAt), { dateStyle: 'medium' }),
      secondary: true,
    },
  ];

  const tableLabels = {
    noResults: tTable('noResults'),
    noResultsDescription: tTable('noResultsDescription'),
    sortAscending: tTable('sortAscending'),
    sortDescending: tTable('sortDescending'),
    actions: tCommon('actions'),
  };

  return (
    <div className="flex flex-col gap-8">
      <PageHeader title={t('title')} description={t('description')} />

      {notice ? <FormMessage tone="success">{notice}</FormMessage> : null}
      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {loading && settings.length === 0 ? (
        <p className="text-sm text-text-secondary">{tCommon('loading')}</p>
      ) : (
        <>
          <section>
            <h2 className="mb-3 text-base font-semibold text-text">{t('settings')}</h2>

            <DataTable
              columns={settingColumns}
              rows={settings}
              rowKey={(setting) => setting.key}
              caption={t('settings')}
              labels={{ ...tableLabels, noResults: t('noSettings') }}
              rowActions={(setting) => (
                <>
                  <button
                    type="button"
                    className="text-sm font-medium text-primary-700 hover:underline"
                    onClick={() => {
                      setEditing(setting);
                      setValue(setting.isSensitive ? '' : effective(setting));
                      setReason('');
                      setFormError(null);
                    }}
                  >
                    {tCommon('edit')}
                  </button>

                  <button
                    type="button"
                    className="text-sm font-medium text-primary-700 hover:underline"
                    onClick={() =>
                      setHistory(history === setting.key ? null : setting.key)
                    }
                  >
                    {t('history')}
                  </button>
                </>
              )}
            />
          </section>

          {history ? (
            <SettingHistory settingKey={history} onClose={() => setHistory(null)} />
          ) : null}

          <section>
            <h2 className="mb-3 text-base font-semibold text-text">{t('flags')}</h2>

            <p className="mb-3 text-sm text-text-secondary">{t('flagsHint')}</p>

            <DataTable
              columns={flagColumns}
              rows={flags}
              rowKey={(flag) => flag.key}
              caption={t('flags')}
              labels={{ ...tableLabels, noResults: t('noFlags') }}
              rowActions={(flag) => (
                <>
                  <button
                    type="button"
                    className="text-sm font-medium text-primary-700 hover:underline"
                    onClick={() => setTargeting(flag)}
                  >
                    {t('target')}
                  </button>

                  <button
                    type="button"
                    className={
                      flag.isEnabled
                        ? 'text-sm font-medium text-danger hover:underline'
                        : 'text-sm font-medium text-primary-700 hover:underline'
                    }
                    onClick={() => void toggleFlag(flag)}
                  >
                    {flag.isEnabled ? t('turnOff') : t('turnOn')}
                  </button>
                </>
              )}
            />
          </section>
        </>
      )}

      {targeting ? (
        <FlagTargeting
          flag={targeting}
          onSaved={() => {
            setTargeting(null);
            setNotice(t('targetSaved', { key: targeting.key }));
            void load();
          }}
          onCancel={() => setTargeting(null)}
        />
      ) : null}

      <FormDialog
        open={editing !== null}
        title={t('editTitle')}
        description={editing?.key ?? ''}
        submitLabel={tCommon('save')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={formError}
        onSubmit={() => void save()}
        onCancel={() => setEditing(null)}
      >
        <Field
          label={t('value')}
          value={value}
          onChange={(event) => setValue(event.target.value)}
          hint={
            editing?.allowedValues.length
              ? t('allowedHint', { values: editing.allowedValues.join(', ') })
              : t('valueHint')
          }
        />

        <Field
          label={t('reason')}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          hint={t('reasonHint')}
          maxLength={1000}
        />
      </FormDialog>
    </div>
  );
}
