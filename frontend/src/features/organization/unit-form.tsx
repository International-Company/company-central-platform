'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog, fieldErrors } from '@/components/shared/form-dialog';
import type { OrganizationUnitTreeDto, ProblemResponse } from '@/types/platform';

/** The unit types the Platform accepts, in the order a structure is usually built. */
export const UnitTypes = [
  'Division',
  'Department',
  'Section',
  'Center',
  'Branch',
  'Team',
] as const;

export type UnitType = (typeof UnitTypes)[number];

/**
 * Creating a unit, and renaming one.
 *
 * Two operations rather than one form, because the Platform separates them: a
 * unit's code and its type are fixed once created, and its parent changes
 * through a move that carries every descendant with it. So editing here is
 * renaming, and the other two have their own controls that say what they do.
 *
 * That is not a limitation to work around. A code that can be edited is a code
 * other systems cannot rely on, and a parent changed by a dropdown quietly
 * relocates every employee beneath it.
 */
export function UnitForm({
  open,
  parent,
  editing,
  onClose,
  onSaved,
}: {
  open: boolean;

  /** The unit the new one is created beneath, or null for a top-level unit. */
  parent: OrganizationUnitTreeDto | null;

  /** The unit being renamed, or null when creating. */
  editing: OrganizationUnitTreeDto | null;

  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useTranslations('organization');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [code, setCode] = useState('');
  const [unitType, setUnitType] = useState<UnitType>('Department');
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!open) {
      return;
    }

    setCode(editing?.code ?? '');
    setUnitType((editing?.unitType as UnitType) ?? 'Department');
    setNameAr(editing?.name.ar ?? '');
    setNameEn(editing?.name.en ?? '');
    setError(null);
    setFields({});
  }, [open, editing]);

  async function submit() {
    setBusy(true);
    setError(null);
    setFields({});

    try {
      const response = await fetch(
        editing
          ? `/api/organization/units/${editing.id}/name`
          : '/api/organization/units',
        {
          method: editing ? 'PUT' : 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(
            editing
              ? { nameAr, nameEn }
              : { parentId: parent?.id ?? null, unitType, code, nameAr, nameEn },
          ),
        },
      );

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;
        const mapped = fieldErrors(body.errors);

        setFields(mapped);

        if (Object.keys(mapped).length === 0) {
          setError(
            response.status === 403
              ? tErrors('forbidden')
              : response.status === 409
                ? (body.errors?.[0]?.message ?? tErrors('generic'))
                : tErrors('generic'),
          );
        }

        return;
      }

      onSaved();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  const typeLabels: Record<UnitType, string> = {
    Division: t('typeDivision'),
    Department: t('typeDepartment'),
    Section: t('typeSection'),
    Center: t('typeCenter'),
    Branch: t('typeBranch'),
    Team: t('typeTeam'),
  };

  return (
    <FormDialog
      open={open}
      title={editing ? t('editUnit') : t('createUnit')}
      {...(editing ? {} : { description: t('createDescription') })}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void submit()}
      onCancel={onClose}
    >
      {editing ? null : (
        <>
          {/* Read-only context rather than a parent dropdown: the parent is
              whichever row the person pressed "add beneath" on, and a second
              way to choose it invites choosing a different one by accident. */}
          <p className="text-sm text-text-secondary">
            {t('parent')}:{' '}
            <span className="font-medium text-text">
              {parent ? parent.code : t('noParent')}
            </span>
          </p>

          <Field
            label={t('code')}
            value={code}
            onChange={(event) => setCode(event.target.value.toUpperCase())}
            error={fields['code']}
            hint={t('codeHint')}
            autoComplete="off"
            required
            requiredLabel={tCommon('required')}
          />

          <div className="flex flex-col gap-1.5">
            <label
              htmlFor="unit-type"
              className="text-sm font-medium text-text"
            >
              {t('unitType')}
              <span className="ms-1 font-normal text-text-muted">
                ({tCommon('required')})
              </span>
            </label>

            <select
              id="unit-type"
              value={unitType}
              onChange={(event) => setUnitType(event.target.value as UnitType)}
              className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
            >
              {UnitTypes.map((type) => (
                <option key={type} value={type}>
                  {typeLabels[type]}
                </option>
              ))}
            </select>
          </div>
        </>
      )}

      {/* Both names, always. An optional second name becomes a permanently
          empty column, and the Arabic interface then shows English names. */}
      <Field
        label={t('nameAr')}
        value={nameAr}
        onChange={(event) => setNameAr(event.target.value)}
        error={fields['nameAr'] ?? fields['name']}
        lang="ar"
        dir="rtl"
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('nameEn')}
        value={nameEn}
        onChange={(event) => setNameEn(event.target.value)}
        error={fields['nameEn']}
        lang="en"
        dir="ltr"
        required
        requiredLabel={tCommon('required')}
      />
    </FormDialog>
  );
}
