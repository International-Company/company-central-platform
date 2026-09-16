'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { DataTable, type Column } from '@/components/shared/data-table';
import { StatusBadge } from '@/components/shared/status-badge';
import { inKilobytes } from './format';
import type {
  DocumentAccessLogDto,
  DocumentAccessRuleDto,
  DocumentDto,
  DocumentLinkDto,
  DocumentVersionDto,
  PagedResult,
} from '@/types/platform';

/**
 * One document, in full: its versions, who can see it, what it is filed
 * against, and who has opened it.
 *
 * **The access log is only fetched for somebody who manages the document.** It
 * is a list of names against times, and a document shared with forty people
 * would otherwise tell each of them what the other thirty-nine had been reading.
 * The Platform refuses it either way; not asking keeps a 403 out of the console
 * for a person who has done nothing wrong.
 */
export function DocumentDetail({
  document,
  onClose,
  onChanged,
}: {
  document: DocumentDto;
  onClose: () => void;
  onChanged: () => void;
}) {
  const t = useTranslations('documents');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const canManage = document.accessLevel === 'Manage';

  const [rules, setRules] = useState<DocumentAccessRuleDto[]>([]);
  const [links, setLinks] = useState<DocumentLinkDto[]>([]);
  const [log, setLog] = useState<DocumentAccessLogDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [subjectId, setSubjectId] = useState('');
  const [subjectKind, setSubjectKind] = useState('User');
  const [level, setLevel] = useState('Read');

  const load = useCallback(async () => {
    setError(null);

    try {
      const [accessResponse, linksResponse] = await Promise.all([
        fetch(`/api/documents/${document.id}/access`),
        fetch(`/api/documents/${document.id}/links`),
      ]);

      if (accessResponse.ok) {
        setRules((await accessResponse.json()) as DocumentAccessRuleDto[]);
      }

      if (linksResponse.ok) {
        setLinks((await linksResponse.json()) as DocumentLinkDto[]);
      }

      if (canManage) {
        const logResponse = await fetch(
          `/api/documents/${document.id}/access-log?page=1&pageSize=25`,
        );

        if (logResponse.ok) {
          const page = (await logResponse.json()) as PagedResult<DocumentAccessLogDto>;

          setLog(page.items);
        }
      }
    } catch {
      setError(tErrors('network'));
    }
  }, [document.id, canManage, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function share() {
    if (subjectId.trim() === '') {
      setError(t('subjectRequired'));

      return;
    }

    setBusy(true);
    setError(null);

    try {
      const response = await fetch(`/api/documents/${document.id}/access`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          subjectKind,
          subjectId: subjectId.trim(),
          level,
          includesSubUnits: subjectKind === 'OrganizationUnit',
        }),
      });

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setSubjectId('');
      await load();
      onChanged();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  async function revoke(rule: DocumentAccessRuleDto) {
    setBusy(true);

    try {
      const response = await fetch(
        `/api/documents/${document.id}/access/${rule.id}`,
        { method: 'DELETE' },
      );

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      await load();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  const versionColumns: Column<DocumentVersionDto>[] = [
    {
      key: 'number',
      header: t('version'),
      render: (version) => String(version.versionNumber),
      numeric: true,
    },
    { key: 'fileName', header: t('fileName'), render: (version) => version.fileName },
    {
      key: 'size',
      header: t('size'),
      render: (version) => inKilobytes(version.sizeInBytes),
      numeric: true,
      secondary: true,
    },
    {
      key: 'uploaded',
      header: t('uploadedAt'),
      render: (version) =>
        format.dateTime(new Date(version.uploadedAt), { dateStyle: 'medium' }),
      secondary: true,
    },
    {
      key: 'state',
      header: t('statusHeading'),
      render: (version) => (
        <StatusBadge tone={version.contentRemoved ? 'neutral' : 'success'}>
          {version.contentRemoved ? t('contentRemoved') : t('stored')}
        </StatusBadge>
      ),
    },
  ];

  const ruleColumns: Column<DocumentAccessRuleDto>[] = [
    {
      key: 'subject',
      header: t('subject'),
      render: (rule) => (
        <div>
          <p className="font-medium text-text">
            {t(`subjectKind.${rule.subjectKind}` as never)}
          </p>
          <p className="mt-0.5 font-mono text-xs text-text-secondary">{rule.subjectId}</p>
        </div>
      ),
    },
    {
      key: 'level',
      header: t('levelHeading'),
      render: (rule) => t(`level.${rule.level}` as never),
    },
    {
      key: 'subUnits',
      header: t('includesSubUnits'),
      render: (rule) => (rule.includesSubUnits ? tCommon('yes') : tCommon('no')),
      secondary: true,
    },
  ];

  const logColumns: Column<DocumentAccessLogDto>[] = [
    {
      key: 'when',
      header: t('when'),
      render: (entry) =>
        format.dateTime(new Date(entry.occurredAt), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }),
    },
    {
      key: 'actor',
      header: t('actor'),
      render: (entry) => (
        <span className="font-mono text-xs">{entry.actorUserId}</span>
      ),
    },
    { key: 'action', header: t('action'), render: (entry) => entry.action },
    {
      key: 'outcome',
      header: t('outcome'),
      render: (entry) => (
        // The refused attempts are the half of this log worth reading.
        <StatusBadge tone={entry.wasAllowed ? 'success' : 'danger'}>
          {entry.wasAllowed ? t('allowed') : t('refused')}
        </StatusBadge>
      ),
    },
    {
      key: 'detail',
      header: t('detail'),
      render: (entry) => entry.detail ?? '',
      secondary: true,
    },
  ];

  const tableLabels = {
    noResults: tTable('noResults'),
    noResultsDescription: tTable('noResultsDescription'),
    sortAscending: tTable('sortAscending'),
    sortDescending: tTable('sortDescending'),
    actions: tCommon('actions'),
  };

  return (
    <section className="flex flex-col gap-6 rounded-md border border-border bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-text">{document.title}</h2>
          <p className="mt-0.5 text-sm text-text-secondary">
            {t('accessLevelIs', { level: t(`level.${document.accessLevel}` as never) })}
          </p>
        </div>

        <Button type="button" variant="secondary" onClick={onClose}>
          {tCommon('close')}
        </Button>
      </div>

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <div>
        <h3 className="mb-2 text-sm font-semibold text-text">{t('versions')}</h3>

        <DataTable
          columns={versionColumns}
          rows={document.versions}
          rowKey={(version) => String(version.versionNumber)}
          caption={t('versions')}
          labels={tableLabels}
          rowActions={(version) =>
            version.contentRemoved ? null : (
              <a
                className="text-sm font-medium text-primary-700 hover:underline"
                href={`/api/documents/${document.id}/content?version=${version.versionNumber}`}
              >
                {t('download')}
              </a>
            )
          }
        />
      </div>

      <div>
        <h3 className="mb-2 text-sm font-semibold text-text">{t('access')}</h3>

        <DataTable
          columns={ruleColumns}
          rows={rules}
          rowKey={(rule) => rule.id}
          caption={t('access')}
          labels={{ ...tableLabels, noResults: t('noRules') }}
          rowActions={(rule) =>
            canManage ? (
              <button
                type="button"
                className="text-sm font-medium text-attention hover:underline"
                onClick={() => void revoke(rule)}
              >
                {t('revoke')}
              </button>
            ) : null
          }
        />

        {canManage ? (
          <div className="mt-4 flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1.5 text-sm font-medium text-text">
              {t('subjectKindLabel')}
              <select
                value={subjectKind}
                onChange={(event) => setSubjectKind(event.target.value)}
                className="rounded-md border border-border-strong bg-surface px-3 py-2 text-sm text-text"
              >
                <option value="User">{t('subjectKind.User')}</option>
                <option value="Role">{t('subjectKind.Role')}</option>
                <option value="OrganizationUnit">
                  {t('subjectKind.OrganizationUnit')}
                </option>
              </select>
            </label>

            <div className="w-full max-w-sm">
              <Field
                label={t('subjectId')}
                value={subjectId}
                onChange={(event) => setSubjectId(event.target.value)}
                hint={t('subjectIdHint')}
              />
            </div>

            <label className="flex flex-col gap-1.5 text-sm font-medium text-text">
              {t('levelHeading')}
              <select
                value={level}
                onChange={(event) => setLevel(event.target.value)}
                className="rounded-md border border-border-strong bg-surface px-3 py-2 text-sm text-text"
              >
                <option value="Read">{t('level.Read')}</option>
                <option value="Write">{t('level.Write')}</option>
                <option value="Manage">{t('level.Manage')}</option>
              </select>
            </label>

            <Button
              type="button"
              onClick={() => void share()}
              busy={busy}
              busyLabel={tCommon('loading')}
            >
              {t('share')}
            </Button>
          </div>
        ) : null}
      </div>

      {links.length > 0 ? (
        <div>
          <h3 className="mb-2 text-sm font-semibold text-text">{t('filedAgainst')}</h3>

          <ul className="flex flex-col gap-1 text-sm text-text-secondary">
            {links.map((link) => (
              <li key={link.id}>
                <span className="font-medium text-text">{link.resourceType}</span>
                <span className="ms-3 font-mono text-xs">{link.resourceId}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {canManage ? (
        <div>
          <h3 className="mb-2 text-sm font-semibold text-text">{t('accessLog')}</h3>

          <p className="mb-2 text-sm text-text-secondary">{t('accessLogHint')}</p>

          <DataTable
            columns={logColumns}
            rows={log}
            rowKey={(entry) => entry.id}
            caption={t('accessLog')}
            labels={tableLabels}
          />
        </div>
      ) : null}
    </section>
  );
}
