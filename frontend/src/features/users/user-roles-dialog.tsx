'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { FormDialog } from '@/components/shared/form-dialog';
import { StepUpDialog, needsStepUp, needsMfaEnrolment } from '@/components/shared/step-up-dialog';
import type {
  OrganizationUnitTreeDto,
  ProblemResponse,
  RoleDto,
  UserDto,
  UserRoleDto,
} from '@/types/platform';

/**
 * The scopes a grant can carry, spelled as the Platform names them.
 *
 * A union rather than a list, because nothing iterates them: the options are
 * written out in the markup so each one carries its own explanation of who it
 * reaches, which a generated list cannot.
 */
type Scope = 'All' | 'Unit' | 'UnitAndBelow' | 'Self';

/**
 * What one user may do, and how far it reaches.
 *
 * **This is how the Platform becomes administrable.** Until it existed the only
 * account with any role was the bootstrap one, granted by startup code, and
 * there was no way to make a second administrator through the product at all.
 *
 * Granting demands a recent second factor, so a refusal here is often "confirm
 * who you are" rather than "you may not". The Platform says which in the body;
 * this dialog reads that and asks for a code instead of sending the person to
 * request a permission they already hold. On confirmation the same grant is
 * retried, so the code is asked for once and the action completes — rather than
 * the person confirming and then having to remember what they were doing.
 */
export function UserRolesDialog({
  user,
  roles,
  units,
  onClose,
}: {
  /** The user whose roles are being managed, or null when closed. */
  user: UserDto | null;

  roles: readonly RoleDto[];
  units: readonly OrganizationUnitTreeDto[];
  onClose: () => void;
}) {
  const t = useTranslations('roles');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const format = useFormatter();
  const locale = useLocale();

  const [assignments, setAssignments] = useState<UserRoleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [roleId, setRoleId] = useState('');
  const [scope, setScope] = useState<Scope>('All');
  const [scopeUnitId, setScopeUnitId] = useState('');
  const [busy, setBusy] = useState(false);

  // The action to run again once a second factor has been confirmed. Held so
  // the person is not left to work out what they were doing before the prompt.
  const [pendingAfterStepUp, setPendingAfterStepUp] = useState<
    (() => Promise<void>) | null
  >(null);

  const load = useCallback(async () => {
    if (!user) {
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`/api/users/${user.id}/roles`);

      if (!response.ok) {
        setError(
          response.status === 403 ? tErrors('forbidden') : tErrors('generic'),
        );

        return;
      }

      const body = (await response.json()) as UserRoleDto[];

      // Revoked assignments come back too — the Platform keeps them, because an
      // access record that disappears is an access record nobody can audit.
      // They are history rather than state, so they are not listed here.
      setAssignments(body.filter((assignment) => !assignment.revokedAt));
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [user, tErrors]);

  useEffect(() => {
    if (user) {
      setRoleId(roles[0]?.id ?? '');
      setScope('All');
      setScopeUnitId(units[0]?.id ?? '');
      setError(null);
      void load();
    }
  }, [user, roles, units, load]);

  /**
   * Runs a write, and turns a step-up refusal into a prompt rather than a
   * dead end. The action is kept so it can be replayed on confirmation.
   */
  async function withStepUp(action: () => Promise<Response>): Promise<void> {
    setBusy(true);
    setError(null);

    try {
      const response = await action();

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;

        if (needsStepUp(response.status, body.code)) {
          setPendingAfterStepUp(() => () => withStepUp(action));

          return;
        }

        // Nothing to confirm with. Saying "confirm your identity" here sends
        // somebody looking for a code they have never had.
        if (needsMfaEnrolment(response.status, body.code)) {
          setError(tErrors('mfaEnrolmentRequired'));

          return;
        }

        setError(
          response.status === 403
            ? t('cannotEscalate')
            : (body.errors?.[0]?.message ?? tErrors('generic')),
        );

        return;
      }

      await load();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function grant() {
    if (!user) {
      return;
    }

    await withStepUp(() =>
      fetch(`/api/users/${user.id}/roles`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          roleId,
          scope,

          // Only the unit scopes carry a unit. Sending one with "All" would be
          // a value the Platform has to decide how to ignore.
          scopeUnitId:
            scope === 'Unit' || scope === 'UnitAndBelow' ? scopeUnitId : null,
          expiresAt: null,
        }),
      }),
    );
  }

  async function revoke(assignment: UserRoleDto) {
    if (!user) {
      return;
    }

    await withStepUp(() =>
      fetch(`/api/users/${user.id}/roles/${assignment.id}`, {
        method: 'DELETE',
      }),
    );
  }

  function roleName(id: string): string {
    const role = roles.find((candidate) => candidate.id === id);

    if (!role) {
      return id;
    }

    return locale === 'ar' ? role.nameAr : role.nameEn;
  }

  function scopeLabel(assignment: UserRoleDto): string {
    switch (assignment.scopeType) {
      case 'All':
        return t('scopeAll');
      case 'Self':
        return t('scopeSelf');
      case 'Unit':
      case 'UnitAndBelow': {
        const unit = units.find(
          (candidate) => candidate.id === assignment.scopeUnitId,
        );

        const name = unit
          ? locale === 'ar'
            ? unit.name.ar
            : unit.name.en
          : (assignment.scopeUnitId ?? '');

        return assignment.scopeType === 'Unit'
          ? `${t('scopeUnit')} — ${name}`
          : `${t('scopeUnitAndBelow')} — ${name}`;
      }
      default:
        return assignment.scopeType;
    }
  }

  const needsUnit = scope === 'Unit' || scope === 'UnitAndBelow';

  return (
    <>
      <FormDialog
        open={user !== null}
        title={t('assignTitle')}
        description={t('assignDescription')}
        submitLabel={t('grant')}
        cancelLabel={tCommon('close')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={error}
        onSubmit={() => void grant()}
        onCancel={onClose}
      >
        {loading ? (
          <p role="status" className="text-sm text-text-secondary">
            {tCommon('loading')}
          </p>
        ) : assignments.length === 0 ? (
          <FormMessage tone="info">{t('noAssignmentsDescription')}</FormMessage>
        ) : (
          <ul className="flex flex-col gap-2">
            {assignments.map((assignment) => (
              <li
                key={assignment.id}
                className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-surface-sunken px-3 py-2"
              >
                <div className="text-sm">
                  <p className="font-medium text-text">
                    {roleName(assignment.roleId)}
                  </p>

                  <p className="text-text-secondary">
                    {scopeLabel(assignment)} ·{' '}
                    {format.dateTime(new Date(assignment.grantedAt), {
                      dateStyle: 'medium',
                    })}
                  </p>
                </div>

                <Button
                  variant="quiet"
                  size="sm"
                  onClick={() => void revoke(assignment)}
                >
                  {t('revoke')}
                </Button>
              </li>
            ))}
          </ul>
        )}

        <hr className="border-border" />

        <div className="flex flex-col gap-1.5">
          <label htmlFor="grant-role" className="text-sm font-medium text-text">
            {t('role')}
          </label>

          <select
            id="grant-role"
            value={roleId}
            onChange={(event) => setRoleId(event.target.value)}
            className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
          >
            {roles.map((role) => (
              <option key={role.id} value={role.id}>
                {locale === 'ar' ? role.nameAr : role.nameEn} ({role.code})
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="grant-scope" className="text-sm font-medium text-text">
            {t('scope')}
          </label>

          <select
            id="grant-scope"
            value={scope}
            onChange={(event) => setScope(event.target.value as Scope)}
            className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
          >
            <option value="All">{t('scopeAll')}</option>
            <option value="UnitAndBelow">{t('scopeUnitAndBelow')}</option>
            <option value="Unit">{t('scopeUnit')}</option>
            <option value="Self">{t('scopeSelf')}</option>
          </select>
        </div>

        {needsUnit ? (
          <div className="flex flex-col gap-1.5">
            <label
              htmlFor="grant-scope-unit"
              className="text-sm font-medium text-text"
            >
              {t('scopeUnitLabel')}
            </label>

            <select
              id="grant-scope-unit"
              value={scopeUnitId}
              onChange={(event) => setScopeUnitId(event.target.value)}
              className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
            >
              {units.map((unit) => (
                <option key={unit.id} value={unit.id}>
                  {'— '.repeat(Number(unit.depth))}
                  {locale === 'ar' ? unit.name.ar : unit.name.en} ({unit.code})
                </option>
              ))}
            </select>
          </div>
        ) : null}
      </FormDialog>

      <StepUpDialog
        open={pendingAfterStepUp !== null}
        onClose={() => {
          setPendingAfterStepUp(null);
          setBusy(false);
        }}
        onConfirmed={() => {
          const retry = pendingAfterStepUp;

          setPendingAfterStepUp(null);

          void retry?.();
        }}
      />
    </>
  );
}
