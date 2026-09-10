'use client';

import { useCallback, useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { FormMessage } from '@/components/ui/field';
import { FormDialog } from '@/components/shared/form-dialog';
import { StepUpDialog, needsStepUp } from '@/components/shared/step-up-dialog';
import type {
  PermissionDto,
  ProblemResponse,
  RoleDetailDto,
  RoleDto,
} from '@/types/platform';

/**
 * Choosing what a role grants.
 *
 * **Grouped by resource, because a flat list of every permission is unreadable
 * and unreviewable.** `platform.users.view` and `platform.users.create` belong
 * together in the reader's head, and a person deciding what a role should do
 * thinks in terms of "users" before "view".
 *
 * The whole set is submitted, not a change to it — matching the Platform, whose
 * endpoint replaces rather than patches. What is on screen when Save is pressed
 * is what the role will hold, which is a thing a reviewer can check; a sequence
 * of additions and removals is not.
 *
 * Step-up protected, like a grant: changing what a role carries changes what
 * everyone holding it can do without touching a single assignment.
 */
export function RolePermissionsDialog({
  role,
  onClose,
  onSaved,
}: {
  /** The role being edited, or null when closed. */
  role: RoleDto | null;

  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useTranslations('roles');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [permissions, setPermissions] = useState<PermissionDto[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [pendingAfterStepUp, setPendingAfterStepUp] = useState<
    (() => Promise<void>) | null
  >(null);

  const load = useCallback(async () => {
    if (!role) {
      return;
    }

    setLoading(true);
    setError(null);

    try {
      // Both, and both must succeed. The set being edited is as necessary as
      // the catalogue to choose from: without it this form would start empty
      // and Save would strip everything the role already held.
      const [all, detail] = await Promise.all([
        fetch('/api/permissions'),
        fetch(`/api/roles/${role.id}`),
      ]);

      if (!all.ok || !detail.ok) {
        setError(
          all.status === 403 || detail.status === 403
            ? tErrors('forbidden')
            : tErrors('generic'),
        );

        return;
      }

      setPermissions((await all.json()) as PermissionDto[]);
      setSelected(
        new Set(((await detail.json()) as RoleDetailDto).permissionIds),
      );
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [role, tErrors]);

  useEffect(() => {
    if (role) {
      void load();
    }
  }, [role, load]);

  async function save(): Promise<void> {
    if (!role) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/roles/${role.id}/permissions`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        // What the role looked like when this dialog opened it. Without it a
        // save made against a five-minute-old page silently discards whatever
        // somebody else changed in between -- in the table that decides what
        // everyone in the company can do.
        body: JSON.stringify({
          permissionIds: [...selected],
          expectedVersion: role.version,
        }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;

        if (needsStepUp(response.status, body.code)) {
          setPendingAfterStepUp(() => save);

          return;
        }

        // A conflict is not a mistake the person made, so it is worded as
        // what happened and what to do rather than as a validation failure.
        if (body.code === 'AUTHZ.ROLE_CHANGED_ELSEWHERE') {
          setError(t('changedElsewhere'));

          return;
        }

        setError(
          response.status === 403
            ? // Not a missing permission on this screen — they reached it. It
              // is the escalation rule: a permission was chosen that the caller
              // does not hold themselves.
              (body.detail ?? t('cannotEscalate'))
            : (body.errors?.[0]?.message ?? tErrors('generic')),
        );

        return;
      }

      onSaved();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current);

      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }

      return next;
    });
  }

  const groups = groupByResource(permissions);
  const readOnly = role?.isSystem === true;

  return (
    <>
      <FormDialog
        open={role !== null}
        title={t('permissionsTitle')}
        description={t('permissionsDescription')}
        submitLabel={tCommon('save')}
        cancelLabel={tCommon('cancel')}
        busy={busy}
        busyLabel={tCommon('loading')}
        error={error}
        onSubmit={() => void save()}
        onCancel={onClose}
      >
        {readOnly ? (
          <FormMessage tone="info">{t('systemRoleReadOnly')}</FormMessage>
        ) : loading ? (
          <p role="status" className="text-sm text-text-secondary">
            {tCommon('loading')}
          </p>
        ) : (
          <>
            <div className="flex gap-2">
              <Button
                variant="quiet"
                size="sm"
                onClick={() =>
                  setSelected(new Set(permissions.map((permission) => permission.id)))
                }
              >
                {t('selectAll')}
              </Button>

              <Button
                variant="quiet"
                size="sm"
                onClick={() => setSelected(new Set())}
              >
                {t('clearAll')}
              </Button>
            </div>

            <div className="max-h-80 overflow-y-auto rounded-md border border-border">
              {groups.map(([resource, group]) => (
                <fieldset key={resource} className="border-b border-border p-3 last:border-b-0">
                  <legend className="px-1 text-sm font-semibold text-text">
                    {resource}
                  </legend>

                  <div className="mt-1 flex flex-col gap-1.5">
                    {group.map((permission) => (
                      <label
                        key={permission.id}
                        className="flex items-center gap-2 text-sm text-text"
                      >
                        <input
                          type="checkbox"
                          checked={selected.has(permission.id)}
                          onChange={() => toggle(permission.id)}
                          className="size-4 rounded border-border-strong"
                        />

                        <span dir="ltr" className="font-mono text-xs">
                          {permission.name}
                        </span>
                      </label>
                    ))}
                  </div>
                </fieldset>
              ))}
            </div>
          </>
        )}
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

/**
 * Permissions gathered under the resource they act on, resources in order.
 *
 * `platform.users.view` and `platform.users.create` sit together, because that
 * is how a person deciding what a role should do reads them.
 */
function groupByResource(
  permissions: readonly PermissionDto[],
): [string, PermissionDto[]][] {
  const groups = new Map<string, PermissionDto[]>();

  for (const permission of permissions) {
    const group = groups.get(permission.resource);

    if (group) {
      group.push(permission);
    } else {
      groups.set(permission.resource, [permission]);
    }
  }

  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
}
