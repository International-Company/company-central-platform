'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { FormDialog } from '@/components/shared/form-dialog';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import { FlagTargeting } from './flag-targeting';
import { SettingHistory } from './setting-history';
import {
  describeScope,
  PlatformToken,
  scopeChoices,
  valueAt,
  type ScopeChoice,
} from './setting-scopes';
import type {
  CompanyDto,
  FeatureFlagDto,
  RegisteredApplicationDto,
  SettingDto,
} from '@/types/platform';

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

  // Names in the reader's language, like everywhere else in the portal.
  const arabic = useLocale() === 'ar';

  const [settings, setSettings] = useState<SettingDto[]>([]);
  const [flags, setFlags] = useState<FeatureFlagDto[]>([]);

  // What a setting can be aimed at. Read once with the settings, because a
  // picker built from what exists is the difference between choosing a company
  // and typing a GUID from memory.
  const [company, setCompany] = useState<CompanyDto | null>(null);
  const [applications, setApplications] = useState<RegisteredApplicationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [history, setHistory] = useState<string | null>(null);
  const [targeting, setTargeting] = useState<FeatureFlagDto | null>(null);

  const [editing, setEditing] = useState<SettingDto | null>(null);
  const [scopeToken, setScopeToken] = useState<string>(PlatformToken);
  const [value, setValue] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const [settingsResponse, flagsResponse, companyResponse, applicationsResponse] =
        await Promise.all([
          fetch('/api/configuration/settings'),
          fetch('/api/configuration/flags'),
          fetch('/api/organization/company'),
          fetch('/api/applications'),
        ]);

      if (!settingsResponse.ok) {
        setError(tErrors('generic'));

        return;
      }

      setSettings((await settingsResponse.json()) as SettingDto[]);

      if (flagsResponse.ok) {
        setFlags((await flagsResponse.json()) as FeatureFlagDto[]);
      }

      // Neither of these failing is fatal. A scope whose list did not load is
      // one the picker cannot offer; refusing to show the screen at all because
      // the application registry was slow would be worse than offering less.
      if (companyResponse.ok) {
        setCompany((await companyResponse.json()) as CompanyDto | null);
      }

      if (applicationsResponse.ok) {
        setApplications((await applicationsResponse.json()) as RegisteredApplicationDto[]);
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

  const choices = useMemo(
    () =>
      scopeChoices(
        company,
        applications,
        {
          platform: t('scopePlatform'),
          company: t('scopeCompany'),
          application: t('scopeApplication'),
        },
        arabic,
      ),
    [company, applications, t, arabic],
  );

  const chosen: ScopeChoice =
    choices.find((choice) => choice.token === scopeToken) ?? choices[0]!;

  /**
   * Opens the dialog on one scope, showing what is set there.
   *
   * An empty box means no override at this scope, not "the value is empty" —
   * which is the same thing the field's hint says, because saving an empty box
   * is how an override is cleared.
   */
  function edit(setting: SettingDto, token: string) {
    const choice = choices.find((c) => c.token === token) ?? choices[0]!;

    setEditing(setting);
    setScopeToken(choice.token);
    setValue(setting.isSensitive ? '' : (valueAt(setting, choice) ?? ''));
    setReason('');
    setFormError(null);
  }

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
          scope: chosen.scope,
          scopeId: chosen.scopeId,

          // Empty clears the override at this scope rather than storing an
          // empty string. Narrowest-wins then falls back to the next scope out,
          // which is what "remove this exception" means.
          value: value.trim() === '' ? null : value,
          reason: reason.trim() === '' ? null : reason.trim(),
        }),
      });

      if (response.ok) {
        setEditing(null);
        setValue('');
        setReason('');
        setNotice(t('savedAt', { key: editing.key, scope: chosen.label }));
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

      // The scopes, not a count. A number told somebody an exception existed
      // and refused to say where — which is the one thing worth knowing when a
      // setting is behaving differently for one application than for everybody
      // else.
      render: (setting) =>
        setting.values.length === 0 ? (
          <span className="text-sm text-text-secondary">{t('atDefault')}</span>
        ) : (
          <ul className="flex flex-col gap-0.5">
            {setting.values.map((override) => (
              <li
                key={`${override.scope}:${override.scopeId ?? ''}`}
                className="text-xs text-text-secondary"
              >
                <span className="text-text">
                  {describeScope(override.scope, override.scopeId ?? null, choices)}
                </span>
                {setting.isSensitive ? null : (
                  <span className="ms-1 font-mono">{override.value}</span>
                )}
              </li>
            ))}
          </ul>
        ),
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
                    onClick={() => edit(setting, PlatformToken)}
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
        description={editing ? `${editing.key} — ${chosen.label}` : ''}
        submitLabel={tCommon('save')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={formError}
        onSubmit={() => void save()}
        onCancel={() => setEditing(null)}
      >
        <div className="flex flex-col gap-1.5">
          <label htmlFor="setting-scope" className="text-sm font-medium text-text">
            {t('scope')}
          </label>

          <select
            id="setting-scope"
            value={scopeToken}
            onChange={(event) => {
              const token = event.target.value;

              setScopeToken(token);

              // The box follows the scope. Leaving the previous scope's value
              // behind would make "save" quietly copy an override from one
              // place to another.
              const next = choices.find((choice) => choice.token === token);

              setValue(
                editing && next && !editing.isSensitive ? (valueAt(editing, next) ?? '') : '',
              );
            }}
            className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
          >
            {choices.map((choice) => (
              <option key={choice.token} value={choice.token}>
                {choice.label}
              </option>
            ))}
          </select>

          <p className="text-xs text-text-secondary">{t('scopeHint')}</p>
        </div>

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
