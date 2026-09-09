'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog, fieldErrors } from '@/components/shared/form-dialog';
import type { ProblemResponse, RoleDto } from '@/types/platform';

/**
 * Naming a role, and renaming one.
 *
 * **Not where its permissions are decided.** The Platform separates the two
 * calls and this follows: naming a bundle is housekeeping, filling it is handing
 * out access, and a single form makes the second look like the first. The code
 * is fixed once created, for the same reason a unit's is — other systems refer
 * to it.
 */
export function RoleForm({
  open,
  editing,
  onClose,
  onSaved,
}: {
  open: boolean;

  /** The role being renamed, or null when creating. */
  editing: RoleDto | null;

  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useTranslations('roles');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [code, setCode] = useState('');
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [description, setDescription] = useState('');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!open) {
      return;
    }

    setCode(editing?.code ?? '');
    setNameAr(editing?.nameAr ?? '');
    setNameEn(editing?.nameEn ?? '');
    setDescription(editing?.description ?? '');
    setError(null);
    setFields({});
  }, [open, editing]);

  async function submit() {
    setBusy(true);
    setError(null);
    setFields({});

    try {
      const response = await fetch(
        editing ? `/api/roles/${editing.id}` : '/api/roles',
        {
          method: editing ? 'PUT' : 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(
            editing
              ? { nameAr, nameEn, description: description || null }
              : { code, nameAr, nameEn, description: description || null },
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
      title={editing ? t('editRole') : t('createRole')}
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
        <Field
          label={t('code')}
          value={code}
          onChange={(event) => setCode(event.target.value.toLowerCase())}
          error={fields['code']}
          hint={t('codeHint')}
          autoComplete="off"
          dir="ltr"
          required
          requiredLabel={tCommon('required')}
        />
      )}

      <Field
        label={t('name') + ' — AR'}
        value={nameAr}
        onChange={(event) => setNameAr(event.target.value)}
        error={fields['nameAr']}
        lang="ar"
        dir="rtl"
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('name') + ' — EN'}
        value={nameEn}
        onChange={(event) => setNameEn(event.target.value)}
        error={fields['nameEn']}
        lang="en"
        dir="ltr"
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('descriptionLabel')}
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        error={fields['description']}
      />
    </FormDialog>
  );
}
