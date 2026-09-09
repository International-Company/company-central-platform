'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { StatusBadge } from '@/components/shared/status-badge';
import { StepUpDialog, needsStepUp } from '@/components/shared/step-up-dialog';
import type {
  ApplicationCredentialDto,
  ApplicationRoleDto,
  IssuedCredentialDto,
  RegisteredApplicationDto,
} from '@/types/platform';

/**
 * One application's keys and roles.
 *
 * **The newly issued secret is shown here and nowhere else, ever.** It is not
 * stored by the Platform in a form anybody can read back, it is not kept by this
 * application, and it disappears from the screen the moment the person navigates
 * away. The banner says so plainly, because somebody who assumes they can come
 * back for it will find out at the worst possible moment.
 */
export function ApplicationCredentials({
  application,
  onClose,
  onChanged,
}: {
  application: RegisteredApplicationDto;
  onClose: () => void;
  onChanged: () => void;
}) {
  const t = useTranslations('applications');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [credentials, setCredentials] = useState<ApplicationCredentialDto[]>([]);
  const [roles, setRoles] = useState<ApplicationRoleDto[]>([]);
  const [issued, setIssued] = useState<IssuedCredentialDto | null>(null);
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [stepUpOpen, setStepUpOpen] = useState(false);

  const load = useCallback(async () => {
    setError(null);

    try {
      const [credentialsResponse, rolesResponse] = await Promise.all([
        fetch(`/api/applications/${application.id}/credentials`),
        fetch(`/api/applications/${application.id}/roles`),
      ]);

      if (credentialsResponse.ok) {
        setCredentials((await credentialsResponse.json()) as ApplicationCredentialDto[]);
      }

      if (rolesResponse.ok) {
        setRoles((await rolesResponse.json()) as ApplicationRoleDto[]);
      }
    } catch {
      setError(tErrors('network'));
    }
  }, [application.id, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function issue() {
    if (label.trim() === '') {
      setError(t('labelRequired'));

      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/applications/${application.id}/credentials`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ label: label.trim(), expiresAt: null }),
      });

      if (response.ok) {
        setIssued((await response.json()) as IssuedCredentialDto);
        setLabel('');
        await load();
        onChanged();

        return;
      }

      const problem = (await response.json().catch(() => null)) as { code?: string } | null;

      if (needsStepUp(response.status, problem?.code)) {
        setStepUpOpen(true);

        return;
      }

      setError(
        problem?.code === 'AUTHZ.TOO_MANY_LIVE_CREDENTIALS'
          ? t('tooManyCredentials')
          : tErrors('generic'),
      );
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function revoke(credential: ApplicationCredentialDto) {
    try {
      const response = await fetch(
        `/api/applications/${application.id}/credentials/${credential.id}`,
        { method: 'DELETE' },
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      await load();
      onChanged();
    } catch {
      setError(tErrors('network'));
    }
  }

  const credentialColumns: Column<ApplicationCredentialDto>[] = [
    {
      key: 'label',
      header: t('label'),
      render: (credential) => (
        <div>
          <p className="font-medium text-text">{credential.label}</p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{credential.clientId}</p>
        </div>
      ),
    },
    {
      key: 'lastUsed',
      header: t('lastUsed'),
      render: (credential) =>
        // The field that makes finishing a rotation possible. "Nothing has used
        // this for a week" is the evidence somebody needs before revoking.
        credential.lastUsedAt === null || credential.lastUsedAt === undefined
          ? t('neverUsed')
          : format.dateTime(new Date(credential.lastUsedAt), {
              dateStyle: 'medium',
              timeStyle: 'short',
            }),
    },
    {
      key: 'state',
      header: t('statusHeading'),
      render: (credential) => (
        <StatusBadge tone={credential.isLive ? 'success' : 'neutral'}>
          {credential.isLive
            ? t('live')
            : credential.revokedAt !== null && credential.revokedAt !== undefined
              ? t('revoked')
              : t('expired')}
        </StatusBadge>
      ),
    },
    {
      key: 'created',
      header: t('issued'),
      render: (credential) =>
        format.dateTime(new Date(credential.createdAt), { dateStyle: 'medium' }),
      secondary: true,
    },
  ];

  const roleColumns: Column<ApplicationRoleDto>[] = [
    { key: 'role', header: t('role'), render: (role) => role.roleNameEn },
    { key: 'code', header: t('roleCode'), render: (role) => role.roleCode, secondary: true },
    { key: 'scope', header: t('scope'), render: (role) => role.scope },
    {
      key: 'state',
      header: t('statusHeading'),
      render: (role) => (
        <StatusBadge tone={role.isRevoked ? 'neutral' : 'success'}>
          {role.isRevoked ? t('revoked') : t('live')}
        </StatusBadge>
      ),
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
    <section className="flex flex-col gap-6 rounded-md border border-border bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-text">{application.name}</h2>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{application.code}</p>
        </div>

        <Button type="button" variant="secondary" onClick={onClose}>
          {tCommon('close')}
        </Button>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {issued ? (
        <div className="rounded-md border border-warning bg-warning-surface p-4">
          <p className="text-sm font-semibold text-warning">{t('secretShownOnce')}</p>

          <p className="mt-1 text-sm text-text">{t('secretShownOnceDetail')}</p>

          <dl className="mt-3 flex flex-col gap-2 text-sm">
            <div>
              <dt className="font-medium text-text">{t('clientId')}</dt>
              <dd className="mt-0.5 break-all font-mono text-xs text-text">{issued.clientId}</dd>
            </div>

            <div>
              <dt className="font-medium text-text">{t('clientSecret')}</dt>
              <dd className="mt-0.5 break-all font-mono text-xs text-text">{issued.secret}</dd>
            </div>
          </dl>

          <div className="mt-3">
            <Button type="button" variant="secondary" onClick={() => setIssued(null)}>
              {t('secretStored')}
            </Button>
          </div>
        </div>
      ) : null}

      <div>
        <h3 className="mb-2 text-sm font-semibold text-text">{t('credentials')}</h3>

        <p className="mb-3 text-sm text-text-secondary">{t('rotationHint')}</p>

        <DataTable
          columns={credentialColumns}
          rows={credentials}
          rowKey={(credential) => credential.id}
          caption={t('credentials')}
          labels={{ ...tableLabels, noResults: t('noCredentials') }}
          rowActions={(credential) =>
            credential.isLive ? (
              <button
                type="button"
                className="text-sm font-medium text-danger hover:underline"
                onClick={() => void revoke(credential)}
              >
                {t('revoke')}
              </button>
            ) : null
          }
        />

        <div className="mt-4 flex flex-wrap items-end gap-3">
          <div className="min-w-64 flex-1">
            <Field
              label={t('label')}
              value={label}
              onChange={(event) => setLabel(event.target.value)}
              hint={t('labelHint')}
              maxLength={120}
            />
          </div>

          <Button
            type="button"
            onClick={() => void issue()}
            busy={busy}
            busyLabel={tCommon('loading')}
          >
            {t('issueCredential')}
          </Button>
        </div>
      </div>

      <div>
        <h3 className="mb-2 text-sm font-semibold text-text">{t('roles')}</h3>

        <p className="mb-3 text-sm text-text-secondary">{t('rolesHint')}</p>

        <DataTable
          columns={roleColumns}
          rows={roles}
          rowKey={(role) => role.assignmentId}
          caption={t('roles')}
          labels={{ ...tableLabels, noResults: t('noRoles') }}
        />
      </div>

      <StepUpDialog
        open={stepUpOpen}
        onClose={() => setStepUpOpen(false)}
        onConfirmed={() => {
          setStepUpOpen(false);
          void issue();
        }}
      />
    </section>
  );
}
