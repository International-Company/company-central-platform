'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { IfPermitted } from '@/lib/permissions';
import type { EmployeeAttributeDto, EmployeeDto } from '@/types/platform';

/**
 * What business applications keep about one person.
 *
 * **Everything, from every application.** "What do you hold about me" is a
 * question a company must be able to answer in full, and a screen that showed
 * one application's slice would make the complete answer something only a
 * database query could produce.
 *
 * **The Platform does not interpret any of it.** These are an application's own
 * notes, opaque here — which is why the table shows a key and a value and offers
 * no formatting, no types and no opinion about what any of it means.
 */
export function EmployeeAttributes({
  employee,
  onClose,
}: {
  employee: EmployeeDto;
  onClose: () => void;
}) {
  const t = useTranslations('employees');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [attributes, setAttributes] = useState<EmployeeAttributeDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [key, setKey] = useState('');
  const [value, setValue] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setError(null);

    try {
      const response = await fetch(`/api/employees/${employee.id}/attributes`);

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setAttributes((await response.json()) as EmployeeAttributeDto[]);
    } catch {
      setError(tErrors('network'));
    }
  }, [employee.id, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function save() {
    setBusy(true);
    setError(null);

    try {
      const response = await fetch(
        `/api/employees/${employee.id}/attributes/${encodeURIComponent(key.trim())}`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ value }),
        },
      );

      if (response.ok) {
        setKey('');
        setValue('');
        await load();

        return;
      }

      const problem = (await response.json().catch(() => null)) as { code?: string } | null;

      setError(messageFor(problem?.code));
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function remove(attribute: EmployeeAttributeDto) {
    try {
      const response = await fetch(
        `/api/employees/${employee.id}/attributes/${encodeURIComponent(attribute.key)}`,
        { method: 'DELETE' },
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      await load();
    } catch {
      setError(tErrors('network'));
    }
  }

  /**
   * The refusals worth naming.
   *
   * The credential one most of all: somebody who has just pasted an API key
   * needs to be told it was not stored and why, not that something went wrong.
   */
  function messageFor(code: string | undefined): string {
    switch (code) {
      case 'ORGANIZATION.ATTRIBUTE_LOOKS_LIKE_A_SECRET':
        return t('attributeLooksLikeASecret');
      case 'ORGANIZATION.ATTRIBUTE_KEY_INVALID':
        return t('attributeKeyInvalid');
      case 'ORGANIZATION.ATTRIBUTE_VALUE_REQUIRED':
        return t('attributeValueRequired');
      case 'ORGANIZATION.ATTRIBUTE_LIMIT_REACHED':
        return t('attributeLimitReached');
      default:
        return tErrors('generic');
    }
  }

  const columns: Column<EmployeeAttributeDto>[] = [
    {
      key: 'key',
      header: t('attributeKey'),
      render: (attribute) => (
        <div>
          <p className="font-mono text-sm text-text">{attribute.key}</p>

          {/*
            The namespace, said out loud. It is the whole reason two
            applications can both keep a "status" here, and reading it off the
            key is how somebody knows whose note they are looking at.
          */}
          <p className="mt-0.5 text-xs text-text-secondary">
            {t('attributeOwner', { application: attribute.key.split('.')[0] ?? attribute.key })}
          </p>
        </div>
      ),
    },
    {
      key: 'value',
      header: t('attributeValue'),
      render: (attribute) => (
        <span className="font-mono text-sm text-text">{attribute.value}</span>
      ),
    },
    {
      key: 'setAt',
      header: t('attributeSetAt'),
      render: (attribute) =>
        format.dateTime(new Date(attribute.setAt), { dateStyle: 'short', timeStyle: 'short' }),
      secondary: true,
    },
  ];

  return (
    <section className="rounded-lg border border-border bg-surface p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-text">
          {t('attributesFor', { name: employee.employeeNumber })}
        </h2>

        <Button variant="secondary" onClick={onClose}>
          {tCommon('close')}
        </Button>
      </div>

      <p className="mb-3 text-sm text-text-secondary">{t('attributesHint')}</p>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <DataTable
        columns={columns}
        rows={attributes}
        rowKey={(attribute) => attribute.key}
        caption={t('attributesFor', { name: employee.employeeNumber })}
        labels={{
          noResults: t('noAttributes'),
          noResultsDescription: t('noAttributesDescription'),
          sortAscending: tTable('sortAscending'),
          sortDescending: tTable('sortDescending'),
          actions: tCommon('actions'),
        }}
        rowActions={(attribute) => (
          <IfPermitted permission="platform.employees.manage">
            <Button variant="quiet" size="sm" onClick={() => void remove(attribute)}>
              {tCommon('delete')}
            </Button>
          </IfPermitted>
        )}
      />

      <IfPermitted permission="platform.employees.manage">
        <div className="mt-4 flex flex-wrap items-end gap-3">
          <div className="w-full max-w-xs">
            <Field
              label={t('attributeKey')}
              value={key}
              onChange={(event) => setKey(event.target.value)}
              hint={t('attributeKeyHint')}
              maxLength={100}
            />
          </div>

          <div className="w-full max-w-xs">
            <Field
              label={t('attributeValue')}
              value={value}
              onChange={(event) => setValue(event.target.value)}
              hint={t('attributeValueHint')}
              maxLength={1000}
            />
          </div>

          <Button
            variant="primary"
            disabled={busy || key.trim() === '' || value.trim() === ''}
            onClick={() => void save()}
          >
            {tCommon('save')}
          </Button>
        </div>
      </IfPermitted>
    </section>
  );
}
