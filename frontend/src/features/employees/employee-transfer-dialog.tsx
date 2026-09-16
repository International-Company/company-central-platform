'use client';

import { useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { FormDialog } from '@/components/shared/form-dialog';
import type {
  EmployeeDto,
  OrganizationUnitTreeDto,
  PositionDto,
  ProblemResponse,
} from '@/types/platform';

/**
 * Moving someone to a different unit, position or manager.
 *
 * **One call, not three edits.** A transfer is a single organizational fact.
 * Split into separate saves it would leave a person briefly reporting to their
 * old manager from their new unit — a state nobody intended, that every report
 * would show, and that a failure halfway through would make permanent.
 *
 * The manager is chosen from the employees this screen already loaded rather
 * than searched for, because the list is what the person is looking at. On a
 * company large enough for that to be wrong, it becomes a search — and that is a
 * decision for when the list actually is too long, not a guess now.
 */
export function EmployeeTransferDialog({
  employee,
  units,
  positions,
  colleagues,
  onClose,
  onTransferred,
}: {
  /** The employee being moved, or null when the dialog is closed. */
  employee: EmployeeDto | null;

  units: readonly OrganizationUnitTreeDto[];
  positions: readonly PositionDto[];

  /** Candidate managers — the employees already on screen. */
  colleagues: readonly EmployeeDto[];

  onClose: () => void;
  onTransferred: () => void;
}) {
  const t = useTranslations('employees');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const locale = useLocale();

  const [unitId, setUnitId] = useState('');
  const [positionId, setPositionId] = useState('');
  const [managerId, setManagerId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!employee) {
      return;
    }

    // Pre-filled with where they are now, so the dialog opens showing the truth
    // and a transfer is a change to it rather than a form filled from nothing.
    setUnitId(employee.unitId);
    setPositionId(employee.positionId ?? '');
    setManagerId(employee.managerId ?? '');
    setError(null);
  }, [employee]);

  async function submit() {
    if (!employee) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/employees/${employee.id}/transfer`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          newUnitId: unitId,
          newPositionId: positionId === '' ? null : positionId,
          newManagerId: managerId === '' ? null : managerId,
        }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;

        setError(
          response.status === 403
            ? tErrors('forbidden')
            : (body.errors?.[0]?.message ?? tErrors('generic')),
        );

        return;
      }

      onTransferred();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  const name = (value: { ar: string; en: string }) =>
    locale === 'ar' ? value.ar : value.en;

  return (
    <FormDialog
      open={employee !== null}
      title={t('transferTitle')}
      description={t('transferDescription')}
      submitLabel={t('transfer')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void submit()}
      onCancel={onClose}
    >
      <div className="flex flex-col gap-1.5">
        <label htmlFor="transfer-unit" className="text-sm font-medium text-text">
          {t('newUnit')}
        </label>

        <select
          id="transfer-unit"
          value={unitId}
          onChange={(event) => setUnitId(event.target.value)}
          className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
        >
          {units.map((unit) => (
            <option key={unit.id} value={unit.id}>
              {'\u00A0\u00A0\u00A0'.repeat(Number(unit.depth))}
              {name(unit.name)} ({unit.code})
            </option>
          ))}
        </select>
      </div>

      <div className="flex flex-col gap-1.5">
        <label
          htmlFor="transfer-position"
          className="text-sm font-medium text-text"
        >
          {t('newPosition')}
        </label>

        <select
          id="transfer-position"
          value={positionId}
          onChange={(event) => setPositionId(event.target.value)}
          className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
        >
          <option value="">{t('noPosition')}</option>

          {positions
            .filter((position) => position.isActive)
            .map((position) => (
              <option key={position.id} value={position.id}>
                {name(position.title)} ({position.code})
              </option>
            ))}
        </select>
      </div>

      <div className="flex flex-col gap-1.5">
        <label
          htmlFor="transfer-manager"
          className="text-sm font-medium text-text"
        >
          {t('newManager')}
        </label>

        <select
          id="transfer-manager"
          value={managerId}
          onChange={(event) => setManagerId(event.target.value)}
          className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
        >
          <option value="">{t('noManager')}</option>

          {colleagues
            // Not themselves. The Platform refuses it, and offering a choice
            // that is always rejected is a trap rather than a validation.
            .filter((candidate) => candidate.id !== employee?.id)
            .map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {name(candidate.fullName)} ({candidate.employeeNumber})
              </option>
            ))}
        </select>
      </div>
    </FormDialog>
  );
}
