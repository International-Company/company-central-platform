'use client';

import { useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { FormDialog } from '@/components/shared/form-dialog';
import type { OrganizationUnitTreeDto } from '@/types/platform';

/**
 * Moving a unit to a different parent.
 *
 * Its own dialog rather than a field on the edit form, because the consequence
 * is not the unit's: everything beneath it moves too, and every employee in
 * those units changes reporting line. A dropdown sitting among the name fields
 * makes that look like an edit.
 *
 * The choices exclude the unit itself and everything under it. The Platform
 * refuses such a move anyway — a unit cannot be its own ancestor — but offering
 * a choice that is always rejected is a trap rather than a validation.
 */
export function MoveUnitDialog({
  unit,
  tree,
  onClose,
  onMoved,
}: {
  /** The unit being moved, or null when the dialog is closed. */
  unit: OrganizationUnitTreeDto | null;

  tree: readonly OrganizationUnitTreeDto[];
  onClose: () => void;
  onMoved: () => void;
}) {
  const t = useTranslations('organization');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const locale = useLocale();

  const [parentId, setParentId] = useState<string>('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (unit) {
      setParentId(unit.parentId ?? '');
      setError(null);
    }
  }, [unit]);

  async function submit() {
    if (!unit) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/organization/units/${unit.id}/move`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ newParentId: parentId === '' ? null : parentId }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as {
          errors?: { message: string }[];
        };

        setError(
          body.errors?.[0]?.message ??
            (response.status === 403 ? tErrors('forbidden') : tErrors('generic')),
        );

        return;
      }

      onMoved();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  const candidates = unit ? eligibleParents(tree, unit.id) : [];

  return (
    <FormDialog
      open={unit !== null}
      title={t('moveTitle')}
      description={t('moveDescription')}
      submitLabel={t('move')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void submit()}
      onCancel={onClose}
    >
      <div className="flex flex-col gap-1.5">
        <label htmlFor="move-parent" className="text-sm font-medium text-text">
          {t('parent')}
        </label>

        <select
          id="move-parent"
          value={parentId}
          onChange={(event) => setParentId(event.target.value)}
          className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
        >
          <option value="">{t('noParent')}</option>

          {candidates.map((candidate) => (
            <option key={candidate.id} value={candidate.id}>
              {/* The code as well as the name: two units can reasonably share a
                  name — "Finance" in two branches — and the code is what tells
                  them apart. */}
              {'\u00A0\u00A0\u00A0'.repeat(Number(candidate.depth))}
              {locale === 'ar' ? candidate.name.ar : candidate.name.en} (
              {candidate.code})
            </option>
          ))}
        </select>
      </div>
    </FormDialog>
  );
}

/** Every unit except the one being moved and its descendants. */
function eligibleParents(
  units: readonly OrganizationUnitTreeDto[],
  excludedId: string,
): OrganizationUnitTreeDto[] {
  return units.flatMap((unit) =>
    unit.id === excludedId
      ? []
      : [unit, ...eligibleParents(unit.children, excludedId)],
  );
}
