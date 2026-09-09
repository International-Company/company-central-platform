'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { FormDialog } from '@/components/shared/form-dialog';
import type { EmployeeDto, ProblemResponse, UserDto } from '@/types/platform';

/**
 * Connecting an employee record to an account that signs in.
 *
 * **They are separate things, and the separation is deliberate.** An employee
 * may have no account — a warehouse worker who never touches the software — and
 * an account may belong to nobody on the payroll: a contractor, an auditor, an
 * integration. Modelling them as one would have forced a fake employee record for
 * every service account, and a fake account for everyone paid.
 *
 * The link is what lets the Platform answer "which unit is this signed-in person
 * in", which is what every organizational permission scope depends on.
 */
export function EmployeeAccountDialog({
  employee,
  users,
  onClose,
  onLinked,
}: {
  /** The employee whose account is being set, or null when closed. */
  employee: EmployeeDto | null;

  users: readonly UserDto[];
  onClose: () => void;
  onLinked: () => void;
}) {
  const t = useTranslations('employees');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const [userId, setUserId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (employee) {
      setUserId(employee.userId ?? '');
      setError(null);
    }
  }, [employee]);

  async function submit() {
    if (!employee) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/employees/${employee.id}/user`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },

        // Null unlinks. The same endpoint for both, because "who is this
        // employee's account" is one fact with one answer, sometimes nobody.
        body: JSON.stringify({ userId: userId === '' ? null : userId }),
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

      onLinked();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog
      open={employee !== null}
      title={t('linkTitle')}
      description={t('linkDescription')}
      submitLabel={tCommon('save')}
      cancelLabel={tCommon('cancel')}
      busy={busy}
      busyLabel={tCommon('loading')}
      error={error}
      onSubmit={() => void submit()}
      onCancel={onClose}
    >
      <div className="flex flex-col gap-1.5">
        <label htmlFor="employee-account" className="text-sm font-medium text-text">
          {t('account')}
        </label>

        <select
          id="employee-account"
          value={userId}
          onChange={(event) => setUserId(event.target.value)}
          className="h-10 rounded-md border border-border-strong bg-surface px-3 text-sm text-text"
        >
          <option value="">{t('unlink')}</option>

          {users.map((user) => (
            <option key={user.id} value={user.id}>
              {user.displayName} ({user.username})
            </option>
          ))}
        </select>
      </div>
    </FormDialog>
  );
}
