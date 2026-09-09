'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Field } from '@/components/ui/field';
import { FormDialog, fieldErrors } from '@/components/shared/form-dialog';
import type { ProblemResponse, UserDto } from '@/types/platform';

/**
 * Creating and editing a user.
 *
 * One component for both, because they are the same form minus two fields. Two
 * components would be two places to keep the labels, the validation feedback and
 * the error mapping in step — and they would drift.
 *
 * **No client-side validation beyond "these two boxes match".** The Platform
 * owns the rules: username shape, email format, password policy, uniqueness. It
 * answers with the specific rule that failed and the field it belongs to, and
 * this form puts the message on that field. A second copy of the rules here
 * would drift the first time one changed, and the drift shows up as a form that
 * accepts what the server then refuses.
 */
export function UserForm({
  open,
  editing,
  onClose,
  onSaved,
}: {
  open: boolean;

  /** The user being edited, or null when creating. */
  editing: UserDto | null;

  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useTranslations('users');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [initialPassword, setInitialPassword] = useState('');

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});

  // Reset whenever the dialog opens, so it never shows the previous subject's
  // details or a stale error from the last attempt.
  useEffect(() => {
    if (!open) {
      return;
    }

    setUsername(editing?.username ?? '');
    setEmail(editing?.email ?? '');
    setDisplayName(editing?.displayName ?? '');
    setInitialPassword('');
    setError(null);
    setFields({});
  }, [open, editing]);

  async function submit() {
    setBusy(true);
    setError(null);
    setFields({});

    try {
      const response = await fetch(
        editing ? `/api/users/${editing.id}` : '/api/users',
        {
          method: editing ? 'PUT' : 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(
            editing
              ? { email, displayName }
              : { username, email, displayName, initialPassword },
          ),
        },
      );

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as ProblemResponse;
        const mapped = fieldErrors(body.errors);

        setFields(mapped);

        // A form-level message only when nothing could be attached to a field.
        // Otherwise the user reads a banner and a field message saying the same
        // thing, and learns to ignore both.
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
      title={editing ? t('editUser') : t('createUser')}
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
          label={t('username')}
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          error={fields['username']}
          autoComplete="off"
          required
          requiredLabel={tCommon('required')}
        />
      )}

      <Field
        label={t('displayName')}
        value={displayName}
        onChange={(event) => setDisplayName(event.target.value)}
        error={fields['displayName']}
        required
        requiredLabel={tCommon('required')}
      />

      <Field
        label={t('email')}
        type="email"
        value={email}
        onChange={(event) => setEmail(event.target.value)}
        error={fields['email']}
        required
        requiredLabel={tCommon('required')}
      />

      {editing ? null : (
        <Field
          label={t('initialPassword')}
          type="password"
          value={initialPassword}
          onChange={(event) => setInitialPassword(event.target.value)}
          error={fields['initialPassword']}
          hint={t('initialPasswordHint')}
          autoComplete="new-password"
          required
          requiredLabel={tCommon('required')}
        />
      )}
    </FormDialog>
  );
}
