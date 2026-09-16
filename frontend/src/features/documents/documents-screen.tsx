'use client';

import { useCallback, useEffect, useState } from 'react';
import { useFormatter, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import { ConfirmDialog } from '@/components/shared/confirm-dialog';
import { DataTable, type Column } from '@/components/shared/data-table';
import { PageHeader } from '@/components/shared/page-header';
import { Pagination } from '@/components/shared/pagination';
import { StatusBadge } from '@/components/shared/status-badge';
import { DocumentDetail } from './document-detail';
import { DocumentUpload } from './document-upload';
import { inKilobytes } from './format';
import type { DocumentDto, PagedResult } from '@/types/platform';

/**
 * The documents a person may see.
 *
 * **The list is what the Platform returned, not what was filtered here.** The
 * search is scoped on the server by the caller's access rules, so this screen
 * never receives a document it would have to hide — which is the only way a page
 * count can be honest.
 */
export function DocumentsScreen() {
  const t = useTranslations('documents');
  const tCommon = useTranslations('common');
  const tTable = useTranslations('table');
  const tErrors = useTranslations('errors');
  const format = useFormatter();

  const [term, setTerm] = useState('');
  const [page, setPage] = useState(1);
  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [result, setResult] = useState<PagedResult<DocumentDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [uploading, setUploading] = useState(false);
  const [selected, setSelected] = useState<DocumentDto | null>(null);
  const [deleting, setDeleting] = useState<DocumentDto | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    const query = new URLSearchParams({
      page: String(page),
      pageSize: '25',
      includeDeleted: String(includeDeleted),
    });

    if (term.trim() !== '') {
      query.set('term', term.trim());
    }

    try {
      const response = await fetch(`/api/documents?${query.toString()}`);

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setResult((await response.json()) as PagedResult<DocumentDto>);
    } catch {
      setError(tErrors('network'));
    } finally {
      setLoading(false);
    }
  }, [page, term, includeDeleted, tErrors]);

  useEffect(() => {
    void load();
  }, [load]);

  async function act(document: DocumentDto, path: string, method: string) {
    setBusy(true);

    try {
      const response = await fetch(`/api/documents/${document.id}${path}`, { method });

      if (!response.ok) {
        setError(tErrors('generic'));

        return;
      }

      setDeleting(null);
      await load();
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  const columns: Column<DocumentDto>[] = [
    {
      key: 'title',
      header: t('documentTitle'),
      render: (document) => (
        <div>
          <p className="font-medium text-text">{document.title}</p>
          <p className="mt-0.5 text-sm text-text-secondary">{document.fileName}</p>
        </div>
      ),
    },
    {
      key: 'category',
      header: t('category'),
      render: (document) => document.category ?? t('uncategorised'),
      secondary: true,
    },
    {
      key: 'version',
      header: t('version'),
      render: (document) => String(document.currentVersionNumber),
      numeric: true,
      secondary: true,
    },
    {
      key: 'size',
      header: t('size'),
      render: (document) =>
        document.sizeInBytes === null || document.sizeInBytes === undefined
          ? t('unknown')
          : inKilobytes(document.sizeInBytes),
      numeric: true,
      secondary: true,
    },
    {
      key: 'status',
      header: t('statusHeading'),
      render: (document) => (
        <StatusBadge
          tone={
            document.status === 'Active'
              ? 'success'
              : document.status === 'MarkedForDeletion'
                ? 'warning'
                : 'neutral'
          }
        >
          {t(`documentStatus.${document.status}` as never)}
        </StatusBadge>
      ),
    },
    {
      key: 'updated',
      header: t('updated'),
      render: (document) =>
        format.dateTime(new Date(document.updatedAt ?? document.createdAt), {
          dateStyle: 'medium',
        }),
      secondary: true,
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        action={
          <Button
            type="button"
            variant="secondary"
            onClick={() => setUploading((open) => !open)}
          >
            {uploading ? tCommon('cancel') : t('upload')}
          </Button>
        }
      />

      {uploading ? (
        <DocumentUpload
          onUploaded={(document) => {
            setUploading(false);
            setNotice(t('uploaded', { title: document.title }));
            void load();
          }}
        />
      ) : null}

      {notice ? <FormMessage tone="success">{notice}</FormMessage> : null}
      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <div className="flex flex-wrap items-end gap-4">
        <div className="w-full max-w-sm">
          <Field
            label={t('search')}
            value={term}
            onChange={(event) => {
              setPage(1);
              setTerm(event.target.value);
            }}
            hint={t('searchHint')}
          />
        </div>

        <label className="flex items-center gap-2 pb-2 text-sm text-text">
          <input
            type="checkbox"
            checked={includeDeleted}
            onChange={(event) => {
              setPage(1);
              setIncludeDeleted(event.target.checked);
            }}
            className="size-4 rounded border-border-strong"
          />
          {t('includeDeleted')}
        </label>
      </div>

      {loading && result === null ? (
        <p className="text-sm text-text-secondary">{tCommon('loading')}</p>
      ) : (
        <>
          <DataTable
            columns={columns}
            rows={result?.items ?? []}
            rowKey={(document) => document.id}
            caption={t('title')}
            labels={{
              noResults: t('noDocuments'),
              noResultsDescription: t('noDocumentsDescription'),
              sortAscending: tTable('sortAscending'),
              sortDescending: tTable('sortDescending'),
              actions: tCommon('actions'),
            }}
            rowActions={(document) => (
              <>
                <button
                  type="button"
                  className="text-sm font-medium text-primary-700 hover:underline"
                  onClick={() => setSelected(document)}
                >
                  {tCommon('viewDetails')}
                </button>

                {document.status === 'Active' ? (
                  <a
                    className="text-sm font-medium text-primary-700 hover:underline"
                    href={`/api/documents/${document.id}/content`}
                  >
                    {t('download')}
                  </a>
                ) : null}

                {document.accessLevel === 'Manage' &&
                document.status === 'MarkedForDeletion' ? (
                  <button
                    type="button"
                    className="text-sm font-medium text-primary-700 hover:underline"
                    onClick={() => void act(document, '/restore', 'POST')}
                  >
                    {t('restore')}
                  </button>
                ) : null}

                {document.accessLevel === 'Manage' && document.status === 'Active' ? (
                  <button
                    type="button"
                    className="text-sm font-medium text-attention hover:underline"
                    onClick={() => setDeleting(document)}
                  >
                    {tCommon('delete')}
                  </button>
                ) : null}
              </>
            )}
          />

          {result ? (
            <Pagination
              page={result.page}
              pageSize={result.pageSize}
              totalItems={result.totalItems}
              onPageChange={setPage}
              labels={{
                showing: (values) => tTable('showing', values),
                previous: tTable('previous'),
                next: tTable('next'),
              }}
            />
          ) : null}
        </>
      )}

      {selected ? (
        <DocumentDetail
          document={selected}
          onClose={() => setSelected(null)}
          onChanged={() => void load()}
        />
      ) : null}

      <ConfirmDialog
        open={deleting !== null}
        title={t('deleteTitle')}
        // The wording says what actually happens. "Delete" that means "hidden
        // for thirty days and then destroyed" is a promise the system keeps and
        // the sentence should too.
        description={t('deleteDescription', { title: deleting?.title ?? '' })}
        confirmLabel={tCommon('delete')}
        cancelLabel={tCommon('cancel')}
        destructive
        busy={busy}
        busyLabel={tCommon('confirm')}
        onConfirm={() => {
          if (deleting) {
            void act(deleting, '', 'DELETE');
          }
        }}
        onCancel={() => setDeleting(null)}
      />
    </div>
  );
}
