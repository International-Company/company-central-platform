'use client';

import { useCallback, useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { FormDialog } from '@/components/shared/form-dialog';
import type {
  FeatureFlagDto,
  OrganizationUnitTreeDto,
  RoleDto,
} from '@/types/platform';

/**
 * Choosing who a feature flag reaches.
 *
 * **Until now a flag could be switched on and off and not aimed.** Targeting was
 * an API call, and before that it did not work at all — the endpoint that
 * answers "is this on for me" passed empty lists to an evaluator that reads
 * them, so every targeted flag answered *off* to everybody. That is fixed, which
 * is what makes this screen worth having.
 *
 * **Names, not identifiers.** A flag is aimed at "Finance" or "Approvers"; the
 * GUIDs are what the Platform stores and are no help at all to the person
 * deciding. The lists are read from the roles and the unit tree so the choice is
 * made from what exists rather than typed from memory.
 */
export function FlagTargeting({
  flag,
  onSaved,
  onCancel,
}: {
  flag: FeatureFlagDto;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const t = useTranslations('configuration');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  // Names are shown in the reader's language, like everything else. A picker
  // that listed Arabic units to an English reader would be the one place in the
  // portal that ignored the locale.
  const arabic = useLocale() === 'ar';

  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [units, setUnits] = useState<FlatUnit[]>([]);
  const [selectedRoles, setSelectedRoles] = useState<string[]>(flag.targetedRoles);
  const [selectedUnits, setSelectedUnits] = useState<string[]>(flag.targetedUnits);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [rolesResponse, unitsResponse] = await Promise.all([
        fetch('/api/roles'),
        fetch('/api/organization/units'),
      ]);

      if (rolesResponse.ok) {
        setRoles((await rolesResponse.json()) as RoleDto[]);
      }

      if (unitsResponse.ok) {
        setUnits(flatten((await unitsResponse.json()) as OrganizationUnitTreeDto[], 0, arabic));
      }
    } catch {
      // The dialog is still usable with whatever did load: a flag can be aimed
      // at roles when the unit tree is unreachable, and vice versa. Refusing to
      // open at all would be worse than offering half the choice.
      setError(tErrors('network'));
    }
  }, [tErrors, arabic]);

  useEffect(() => {
    void load();
  }, [load]);

  async function save() {
    setBusy(true);
    setError(null);

    try {
      const response = await fetch(
        `/api/configuration/flags/${encodeURIComponent(flag.key)}`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            isEnabled: flag.isEnabled,
            roleIds: selectedRoles,
            unitIds: selectedUnits,
          }),
        },
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      onSaved();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  function toggle(list: string[], id: string): string[] {
    return list.includes(id) ? list.filter((value) => value !== id) : [...list, id];
  }

  const untargeted = selectedRoles.length === 0 && selectedUnits.length === 0;

  return (
    <FormDialog
      open
      title={t('targetTitle')}
      description={flag.key}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void save()}
      onCancel={onCancel}
    >
      {/* Said before the choice, not after it. "Nothing selected" reads as
          "reaches nobody" to almost everyone, and it means the opposite. */}
      <p className="text-sm text-text-secondary">
        {untargeted ? t('targetNoneHint') : t('targetSomeHint')}
      </p>

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-text">{t('targetRoles')}</legend>

        {roles.length === 0 ? (
          <p className="text-sm text-text-secondary">{t('targetNoRoles')}</p>
        ) : (
          roles.map((role) => (
            <label key={role.id} className="flex items-center gap-2 text-sm text-text">
              <input
                type="checkbox"
                checked={selectedRoles.includes(role.id)}
                onChange={() => setSelectedRoles(toggle(selectedRoles, role.id))}
                className="size-4"
              />
              {arabic ? role.nameAr : role.nameEn}
              <span className="font-mono text-xs text-text-secondary">{role.code}</span>
            </label>
          ))
        )}
      </fieldset>

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-text">{t('targetUnits')}</legend>

        {units.length === 0 ? (
          <p className="text-sm text-text-secondary">{t('targetNoUnits')}</p>
        ) : (
          units.map((unit) => (
            <label
              key={unit.id}
              className="flex items-center gap-2 text-sm text-text"
              // Indented by depth, using a logical property so the tree reads
              // the right way round in Arabic.
              style={{ marginInlineStart: `${unit.depth * 16}px` }}
            >
              <input
                type="checkbox"
                checked={selectedUnits.includes(unit.id)}
                onChange={() => setSelectedUnits(toggle(selectedUnits, unit.id))}
                className="size-4"
              />
              {unit.name}
            </label>
          ))
        )}
      </fieldset>

      {/* A unit reaches everybody below it. Worth saying, because the tree
          above shows depth and nothing else explains what selecting a branch
          does. */}
      <p className="text-sm text-text-secondary">{t('targetUnitHint')}</p>
    </FormDialog>
  );
}

interface FlatUnit {
  id: string;
  name: string;
  depth: number;
}

/**
 * The tree as a list, keeping the shape in an indent.
 *
 * A checkbox per node is the honest control: the Platform stores a set of unit
 * ids, so anything cleverer would be a picture of a decision the Platform does
 * not actually make.
 */
function flatten(
  units: readonly OrganizationUnitTreeDto[],
  depth: number,
  arabic: boolean,
): FlatUnit[] {
  return units.flatMap((unit) => [
    { id: unit.id, name: arabic ? unit.name.ar : unit.name.en, depth },
    ...flatten(unit.children, depth + 1, arabic),
  ]);
}
