'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog, fieldErrors } from '@/components/shared/form-dialog';
import type { CompanyDto, ProblemResponse } from '@/types/platform';

/**
 * Defining the company, and renaming it.
 *
 * **The first thing a new Platform needs.** Units belong to the company and
 * employees belong to units, so until this exists the whole section can do
 * nothing — and the Platform shipped with no way to create one at all.
 *
 * Exactly one, and the Platform refuses a second. The code cannot change
 * afterwards, like every other code here: other records refer to it.
 */
export function CompanyForm({
  open,
  editing,
  onClose,
  onSaved,
}: {
  open: boolean;

  /** The company being renamed, or null when defining it for the first time. */
  editing: CompanyDto | null;

  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useTranslations('organization');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [code, setCode] = useState('');
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [defaultLocale, setDefaultLocale] = useState('ar');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!open) {
      return;
    }

    setCode(editing?.code ?? '');
    setNameAr(editing?.name.ar ?? '');
    setNameEn(editing?.name.en ?? '');
    setDefaultLocale(editing?.defaultLocale ?? 'ar');
    setError(null);
    setFields({});
  }, [open, editing]);

  async function submit() {
    setBusy(true);
    setError(null);
    setFields({});

    try {
      const response = await fetch(
        editing ? '/api/organization/company/name' : '/api/organization/company',
        {
          method: editing ? 'PUT' : 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(
            editing ? { nameAr, nameEn } : { code, nameAr, nameEn, defaultLocale },
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

  return (
    <FormDialog
      open={open}
      title={editing ? t('renameCompany') : t('createCompany')}
      {...(editing ? {} : { description: t('companyMissingDescription') })}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void submit()}
      onCancel={onClose}
    >
      {editing ? null : (
        <Field
          label={t('companyCode')}
          value={code}
          onChange={(event) => setCode(event.target.value.toUpperCase())}
          error={fields['code']}
          hint={t('companyCodeHint')}
          autoComplete="off"
          dir="ltr"
          required
          requiredLabel={tCommon('required')}
        />
      )}

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

      {editing ? null : (
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="company-locale"
            className="text-sm font-medium text-text"
          >
            {t('defaultLocale')}
          </label>

          <select
            id="company-locale"
            value={defaultLocale}
            onChange={(event) => setDefaultLocale(event.target.value)}
            className="h-9 rounded-sm border border-border-strong bg-surface px-3 text-sm text-text"
          >
            <option value="ar">{t('localeAr')}</option>
            <option value="en">{t('localeEn')}</option>
          </select>
        </div>
      )}
    </FormDialog>
  );
}
