'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { FormDialog } from '@/components/shared/form-dialog';
import { PageHeader } from '@/components/shared/page-header';
import { StatusBadge } from '@/components/shared/status-badge';
import { StepUpDialog, needsStepUp, needsMfaEnrolment } from '@/components/shared/step-up-dialog';
import { ApplicationCredentials } from './application-credentials';
import type { RegisteredApplicationDto } from '@/types/platform';

/**
 * The systems allowed to call this Platform.
 *
 * **The screen an administrator opens during an incident**, which is what
 * decides its columns: how many live keys an application has, and whether it is
 * still enabled. "Turn it off" has to be one click away and obvious.
 */
export function ApplicationsScreen() {
  const t = useTranslations('applications');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [applications, setApplications] = useState<RegisteredApplicationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<RegisteredApplicationDto | null>(null);

  const [registering, setRegistering] = useState(false);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const [stepUpOpen, setStepUpOpen] = useState(false);
  const [afterStepUp, setAfterStepUp] = useState<(() => void) | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch('/api/applications');

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setApplications((await response.json()) as RegisteredApplicationDto[]);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function register() {
    setBusy(true);
    setFormError(null);

    try {
      const response = await fetch('/api/applications', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          code: code.trim(),
          name: name.trim(),
          description: description.trim() === '' ? null : description.trim(),
        }),
      });

      if (response.ok) {
        setRegistering(false);
        setCode('');
        setName('');
        setDescription('');
        await load();

        return;
      }

      const problem = (await response.json().catch(() => null)) as { code?: string } | null;

      // Registering an application demands a second factor. The refusal and the
      // remedy are the same conversation: a person who holds the permission is
      // asked for a code, not told to request access they already have.
      if (needsStepUp(response.status, problem?.code)) {
        setAfterStepUp(() => () => void register());
        setStepUpOpen(true);

        return;
      }

      // Nothing to confirm with; the remedy is the enrolment screen.
      if (needsMfaEnrolment(response.status, problem?.code)) {
        setFormError(tErrors('mfaEnrolmentRequired'));

        return;
      }

      setFormError(
        problem?.code === 'AUTHZ.APPLICATION_CODE_TAKEN'
          ? t('codeTaken')
          : tErrors('generic'),
      );
    } catch {
      setFormError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function setActive(application: RegisteredApplicationDto, isActive: boolean) {
    try {
      const response = await fetch(`/api/applications/${application.id}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isActive }),
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

  const columns: Column<RegisteredApplicationDto>[] = [
    {
      key: 'name',
      header: t('name'),
      render: (application) => (
        <div>
          <p className="font-medium text-text">{application.name}</p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{application.code}</p>
        </div>
      ),
    },
    {
      key: 'credentials',
      header: t('liveCredentials'),
      render: (application) => (
        // The number that matters during an incident: how many keys can still
        // authenticate as this system right now.
        <StatusBadge tone={Number(application.liveCredentials) > 0 ? 'neutral' : 'warning'}>
          {String(application.liveCredentials)}
        </StatusBadge>
      ),
      numeric: true,
    },
    {
      key: 'permissions',
      header: t('declaredPermissions'),
      render: (application) => String(application.declaredPermissions),
      numeric: true,
      secondary: true,
    },
    {
      key: 'status',
      header: t('statusHeading'),
      render: (application) => (
        <StatusBadge tone={application.isActive ? 'success' : 'danger'}>
          {application.isActive ? t('enabled') : t('disabled')}
        </StatusBadge>
      ),
    },
    {
      key: 'registered',
      header: t('registered'),
      render: (application) =>
        format.dateTime(new Date(application.createdAt), { dateStyle: 'medium' }),
      secondary: true,
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        action={
          <Button type="button" variant="secondary" onClick={() => setRegistering(true)}>
            {t('register')}
          </Button>
        }
      />

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      {loading && applications.length === 0 ? (
        <p className="text-sm text-text-secondary">{tCommon('loading')}</p>
      ) : (
        <DataTable
          columns={columns}
          rows={applications}
          rowKey={(application) => application.id}
          caption={t('title')}
          labels={{
            noResults: t('noApplications'),
            noResultsDescription: t('noApplicationsDescription'),
            sortAscending: tTable('sortAscending'),
            sortDescending: tTable('sortDescending'),
            actions: tCommon('actions'),
          }}
          rowActions={(application) => (
            <>
              <button
                type="button"
                className="text-sm font-medium text-primary-700 hover:underline"
                onClick={() => setSelected(application)}
              >
                {t('credentials')}
              </button>

              {/* The Platform's own registration cannot be disabled: its
                  permissions are what every endpoint checks for. */}
              {application.isSystem ? null : (
                <button
                  type="button"
                  className={
                    application.isActive
                      ? 'text-sm font-medium text-attention hover:underline'
                      : 'text-sm font-medium text-primary-700 hover:underline'
                  }
                  onClick={() => void setActive(application, !application.isActive)}
                >
                  {application.isActive ? t('disable') : t('enable')}
                </button>
              )}
            </>
          )}
        />
      )}

      {selected ? (
        <ApplicationCredentials
          application={selected}
          onClose={() => setSelected(null)}
          onChanged={() => void load()}
        />
      ) : null}

      <FormDialog
        open={registering}
        title={t('registerTitle')}
        description={t('registerDescription')}
        submitLabel={t('register')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={formError}
        onSubmit={() => void register()}
        onCancel={() => setRegistering(false)}
      >
        <Field
          label={t('code')}
          value={code}
          onChange={(event) => setCode(event.target.value)}
          hint={t('codeHint')}
          required
          requiredLabel={tCommon('required')}
          maxLength={32}
        />

        <Field
          label={t('name')}
          value={name}
          onChange={(event) => setName(event.target.value)}
          required
          requiredLabel={tCommon('required')}
          maxLength={200}
        />

        <Field
          label={t('appDescription')}
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          maxLength={1000}
        />
      </FormDialog>

      <StepUpDialog
        open={stepUpOpen}
        onClose={() => setStepUpOpen(false)}
        onConfirmed={() => {
          setStepUpOpen(false);
          afterStepUp?.();
        }}
      />
    </div>
  );
}
