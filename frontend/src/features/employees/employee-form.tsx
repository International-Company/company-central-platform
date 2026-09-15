'use client';

import { useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog, fieldErrors } from '@/components/shared/form-dialog';
import type {
  OrganizationUnitTreeDto,
  ProblemResponse,
} from '@/types/platform';

/**
 * Creating an employee.
 *
 * **The unit is required and chosen from the structure**, not typed. An
 * employee who belongs to no unit belongs to no reporting line and appears in
 * no scoped query — which is why the Platform refuses one, and why this form
 * offers the units that exist rather than a box to guess a code into.
 *
 * The screen behind this will not open the form at all when there are no units.
 * A create button that always fails is worse than no create button: the person
 * cannot tell whether they lack a permission, mistyped something, or hit a bug.
 */
export function EmployeeForm({
  open,
  units,
  onClose,
  onSaved,
}: {
  open: boolean;

  /** The structure, flattened, for choosing where the employee belongs. */
  units: readonly OrganizationUnitTreeDto[];

  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useTranslations('employees');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');
  const locale = useLocale();

  const [employeeNumber, setEmployeeNumber] = useState('');
  const [fullNameAr, setFullNameAr] = useState('');
  const [fullNameEn, setFullNameEn] = useState('');
  const [unitId, setUnitId] = useState('');
  const [workEmail, setWorkEmail] = useState('');
  const [workPhone, setWorkPhone] = useState('');
  const [hireDate, setHireDate] = useState('');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!open) {
      return;
    }

    setEmployeeNumber('');
    setFullNameAr('');
    setFullNameEn('');
    setUnitId(units[0]?.id ?? '');
    setWorkEmail('');
    setWorkPhone('');
    setHireDate('');
    setError(null);
    setFields({});
  }, [open, units]);

  async function submit() {
    setBusy(true);
    setError(null);
    setFields({});

    try {
      const response = await fetch('/api/employees', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          employeeNumber,
          fullNameAr,
          fullNameEn,
          unitId,

          // Omitted rather than sent empty. An empty string is a value the
          // Platform would have to interpret; absence is unambiguous.
          workEmail: workEmail || null,
          workPhone: workPhone || null,
          hireDate: hireDate || null,
        }),
      });

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

  return (
    <FormDialog
      open={open}
      title={t('createEmployee')}
      description={t('createDescription')}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void submit()}
      onCancel={onClose}
    >
      <Field
        label={t('employeeNumber')}
        value={employeeNumber}
        onChange={(event) => setEmployeeNumber(event.target.value)}
        error={fields['employeeNumber']}
        hint={t('employeeNumberHint')}
        autoComplete="off"
        required
        requiredLabel={tCommon('required')}
      />

      {/* Both names, always — the reason the Employees table can show each
          reader a name in their own language. */}
      <Field
        label={t('fullNameAr')}
        value={fullNameAr}
        onChange={(event) => setFullNameAr(event.target.value)}
        error={fields['fullNameAr'] ?? fields['fullName']}
        lang="ar"
        dir="rtl"
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('fullNameEn')}
        value={fullNameEn}
        onChange={(event) => setFullNameEn(event.target.value)}
        error={fields['fullNameEn']}
        lang="en"
        dir="ltr"
        required
        requiredLabel={tCommon('required')}
      />

      <div className="flex flex-col gap-1.5">
        <label htmlFor="employee-unit" className="text-sm font-medium text-text">
          {t('unit')}
          <span className="ms-1 font-normal text-text-muted">
            ({tCommon('required')})
          </span>
        </label>

        <select
          id="employee-unit"
          value={unitId}
          onChange={(event) => setUnitId(event.target.value)}
          aria-invalid={fields['unitId'] ? true : undefined}
          className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
          required
        >
          {units.map((unit) => (
            <option key={unit.id} value={unit.id}>
              {'\u00A0\u00A0\u00A0'.repeat(Number(unit.depth))}
              {locale === 'ar' ? unit.name.ar : unit.name.en} ({unit.code})
            </option>
          ))}
        </select>

        {fields['unitId'] ? (
          <p role="alert" className="text-xs text-attention">
            {fields['unitId']}
          </p>
        ) : null}
      </div>

      <Field
        label={t('workEmail')}
        type="email"
        value={workEmail}
        onChange={(event) => setWorkEmail(event.target.value)}
        error={fields['workEmail']}
        autoComplete="off"
      />

      <Field
        label={t('workPhone')}
        type="tel"
        value={workPhone}
        onChange={(event) => setWorkPhone(event.target.value)}
        error={fields['workPhone']}
        autoComplete="off"
        dir="ltr"
      />

      <Field
        label={t('hireDate')}
        type="date"
        value={hireDate}
        onChange={(event) => setHireDate(event.target.value)}
        error={fields['hireDate']}
      />
    </FormDialog>
  );
}
