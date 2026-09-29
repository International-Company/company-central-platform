'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { StatusBadge } from '@/components/shared/status-badge';
import { StepUpDialog, needsStepUp, needsMfaEnrolment } from '@/components/shared/step-up-dialog';
import { refusalMessage } from '@/lib/refusals';
import type {
  ApplicationCredentialDto,
  ApplicationRoleDto,
  IssuedCredentialDto,
  OrganizationUnitTreeDto,
  RegisteredApplicationDto,
  RoleDto,
} from '@/types/platform';

/**
 * The scopes a grant can carry, spelled as the Platform names them.
 *
 * The same four a person's grant carries, because an application holds the
 * same roles at the same scopes and the screen should not invent a second
 * vocabulary for it.
 */
type Scope = 'All' | 'Unit' | 'UnitAndBelow' | 'Self';

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
  const tRoles = useTranslations('roles');
  const tErrors = useTranslations('errors');
  const format = useFormatter();
  const locale = useLocale();

  const [credentials, setCredentials] = useState<ApplicationCredentialDto[]>([]);
  const [roles, setRoles] = useState<ApplicationRoleDto[]>([]);
  const [issued, setIssued] = useState<IssuedCredentialDto | null>(null);
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Every role on the Platform, and the tree, for the grant form below. Loaded
  // once with the rest rather than when the form is opened: there is no form to
  // open, it is simply there.
  const [allRoles, setAllRoles] = useState<RoleDto[]>([]);
  const [units, setUnits] = useState<OrganizationUnitTreeDto[]>([]);

  const [roleId, setRoleId] = useState('');
  const [scope, setScope] = useState<Scope>('All');
  const [scopeUnitId, setScopeUnitId] = useState('');

  /**
   * Which action is waiting on a confirmation of identity.
   *
   * A single flag was enough while issuing a key was the only thing here that
   * needed one. With two, a flag would confirm the person and then run
   * whichever action the code happened to call.
   */
  const [pending, setPending] = useState<'issue' | 'grant' | null>(null);

  const load = useCallback(async () => {
    setError(null);

    try {
      const [credentialsResponse, rolesResponse, allRolesResponse, unitsResponse] =
        await Promise.all([
          fetch(`/api/applications/${application.id}/credentials`),
          fetch(`/api/applications/${application.id}/roles`),
          fetch('/api/roles'),
          fetch('/api/organization/units'),
        ]);

      if (credentialsResponse.ok) {
        setCredentials((await credentialsResponse.json()) as ApplicationCredentialDto[]);
      }

      if (rolesResponse.ok) {
        setRoles((await rolesResponse.json()) as ApplicationRoleDto[]);
      }

      // The two the grant form offers. A failure here is not reported: the
      // credentials above are what this panel is mostly for, and a reader who
      // cannot list roles should still see the keys.
      if (allRolesResponse.ok) {
        const available = ((await allRolesResponse.json()) as RoleDto[])
          .filter((role) => role.isActive);

        setAllRoles(available);
        setRoleId((current) => current || (available[0]?.id ?? ''));
      }

      if (unitsResponse.ok) {
        const tree = flatten((await unitsResponse.json()) as OrganizationUnitTreeDto[]);

        setUnits(tree);
        setScopeUnitId((current) => current || (tree[0]?.id ?? ''));
      }
    } catch {
      setError(tErrors('network'));
    }
  }, [application.id, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  /**
   * Gives the application a role.
   *
   * **This had no screen at all until the first business application needed
   * one.** The endpoint existed, the route through this application existed,
   * and the panel listed what an application held without any way to add to
   * it — so registering an application and then making it able to do anything
   * were two tasks, one of which could not be done here.
   */
  async function grant() {
    if (roleId === '') {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/applications/${application.id}/roles`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          roleId,
          scope,

          // Only the unit scopes carry a unit. Sending one with "All" would be
          // describing a restriction that is not being applied.
          scopeUnitId: scope === 'Unit' || scope === 'UnitAndBelow' ? scopeUnitId : null,
          expiresAt: null,
        }),
      });

      if (response.ok) {
        await load();
        onChanged();

        return;
      }

      const problem = (await response.json().catch(() => null)) as { code?: string } | null;

      if (needsStepUp(response.status, problem?.code)) {
        setPending('grant');

        return;
      }

      if (needsMfaEnrolment(response.status, problem?.code)) {
        setError(tErrors('mfaEnrolmentRequired'));

        return;
      }

      // Named rather than reported as a fault. Refusing to grant what the
      // granter does not hold is a rule, and "something went wrong" would send
      // somebody looking for a broken screen.
      setError(refusalMessage(problem, tErrors) ?? tErrors('generic'));
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

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
        setPending('issue');

        return;
      }

      // Nothing to confirm with; the remedy is the enrolment screen.
      if (needsMfaEnrolment(response.status, problem?.code)) {
        setError(tErrors('mfaEnrolmentRequired'));

        return;
      }

      setError(refusalMessage(problem, tErrors) ?? tErrors('generic'));
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
        <div className="rounded-md border border-caution bg-caution-surface p-4">
          <p className="text-sm font-semibold text-caution">{t('secretShownOnce')}</p>

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
                className="text-sm font-medium text-attention hover:underline"
                onClick={() => void revoke(credential)}
              >
                {t('revoke')}
              </button>
            ) : null
          }
        />

        <div className="mt-4 flex flex-wrap items-end gap-3">
          <div className="w-full max-w-sm">
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

        {allRoles.length === 0 ? (
          // No role to give. Said plainly, with where to make one, because an
          // empty select beside a button that refuses is a screen that looks
          // broken.
          <p className="mt-4 text-sm text-text-secondary">{t('noRolesToGrant')}</p>
        ) : (
          <div className="mt-4 flex flex-wrap items-end gap-3">
            <div className="flex w-full max-w-xs flex-col gap-1.5">
              <label htmlFor="grant-app-role" className="text-sm font-medium text-text">
                {t('role')}
              </label>

              <select
                id="grant-app-role"
                value={roleId}
                onChange={(event) => setRoleId(event.target.value)}
                className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
              >
                {allRoles.map((role) => (
                  <option key={role.id} value={role.id}>
                    {locale === 'ar' ? role.nameAr : role.nameEn} ({role.code})
                  </option>
                ))}
              </select>
            </div>

            <div className="flex w-full max-w-[13rem] flex-col gap-1.5">
              <label htmlFor="grant-app-scope" className="text-sm font-medium text-text">
                {t('scope')}
              </label>

              <select
                id="grant-app-scope"
                value={scope}
                onChange={(event) => setScope(event.target.value as Scope)}
                className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
              >
                <option value="All">{tRoles('scopeAll')}</option>
                <option value="UnitAndBelow">{tRoles('scopeUnitAndBelow')}</option>
                <option value="Unit">{tRoles('scopeUnit')}</option>
                <option value="Self">{tRoles('scopeSelf')}</option>
              </select>
            </div>

            {scope === 'Unit' || scope === 'UnitAndBelow' ? (
              <div className="flex w-full max-w-xs flex-col gap-1.5">
                <label
                  htmlFor="grant-app-scope-unit"
                  className="text-sm font-medium text-text"
                >
                  {tRoles('scopeUnitLabel')}
                </label>

                <select
                  id="grant-app-scope-unit"
                  value={scopeUnitId}
                  onChange={(event) => setScopeUnitId(event.target.value)}
                  className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
                >
                  {units.map((unit) => (
                    <option key={unit.id} value={unit.id}>
                      {'   '.repeat(Number(unit.depth))}
                      {locale === 'ar' ? unit.name.ar : unit.name.en} ({unit.code})
                    </option>
                  ))}
                </select>
              </div>
            ) : null}

            <Button onClick={() => void grant()} busy={busy} busyLabel={tCommon('loading')}>
              {t('grantRole')}
            </Button>
          </div>
        )}
      </div>

      <StepUpDialog
        open={pending !== null}
        onClose={() => setPending(null)}
        onConfirmed={() => {
          const action = pending;

          setPending(null);

          // The action that was refused, not whichever one this dialog was
          // written for first.
          if (action === 'issue') {
            void issue();
          } else if (action === 'grant') {
            void grant();
          }
        }}
      />
    </section>
  );
}

/**
 * The unit tree as a flat list, each carrying its depth.
 *
 * A select cannot nest, so the hierarchy is shown by indenting the label. The
 * alternative — a flat list of names — makes two departments called
 * "Operations" under different parents indistinguishable at the moment somebody
 * is deciding what an application may reach.
 */
function flatten(
  tree: OrganizationUnitTreeDto[],
  depth = 0,
): (OrganizationUnitTreeDto & { depth: number })[] {
  return tree.flatMap((unit) => [
    { ...unit, depth },
    ...flatten(unit.children, depth + 1),
  ]);
}
