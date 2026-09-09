'use client';

import { useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Field, FormMessage } from '@/components/ui/field';
import type { DocumentDto } from '@/types/platform';

/**
 * The upload control, on its own so that any screen can embed it.
 *
 * **This is the piece a business system reuses.** A purchase order screen wants
 * "attach a signed contract" without knowing anything about storage, versions or
 * access rules — so it renders this, passes the record to file the document
 * against, and gets a document back.
 *
 * It does not know what a purchase order is. `resourceType` and `resourceId` are
 * two strings it forwards.
 */
export function DocumentUpload({
  resourceType,
  resourceId,
  organizationUnitId,
  onUploaded,
}: {
  /** The kind of record to file this against, in the caller's own vocabulary. */
  resourceType?: string;
  resourceId?: string;
  organizationUnitId?: string;
  onUploaded: (document: DocumentDto) => void;
}) {
  const t = useTranslations('documents');
  const tCommon = useTranslations('common');
  const tErrors = useTranslations('errors');

  const input = useRef<HTMLInputElement>(null);

  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [category, setCategory] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function upload() {
    if (!file) {
      setError(t('chooseAFile'));

      return;
    }

    setBusy(true);
    setError(null);

    try {
      const form = new FormData();

      form.append('file', file);

      // The file name is the title when nobody typed one. A person who picked
      // "signed-contract.pdf" has already said what it is.
      form.append('title', title.trim() === '' ? file.name : title.trim());

      if (category.trim() !== '') {
        form.append('category', category.trim());
      }

      if (organizationUnitId) {
        form.append('organizationUnitId', organizationUnitId);
      }

      const response = await fetch('/api/documents', { method: 'POST', body: form });

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as
          | { code?: string }
          | null;

        // The refusals worth naming are the ones a person can act on: the file
        // is too big, or it is not a kind of file this company stores.
        setError(
          problem?.code === 'DOCUMENTS.FILE_TOO_LARGE'
            ? t('tooLarge')
            : problem?.code === 'DOCUMENTS.FILE_TYPE_NOT_ALLOWED'
              ? t('typeNotAllowed')
              : problem?.code === 'DOCUMENTS.SCAN_REJECTED'
                ? t('scanRejected')
                : tErrors('generic'),
        );

        return;
      }

      const document = (await response.json()) as DocumentDto;

      if (resourceType && resourceId) {
        await fetch(`/api/documents/${document.id}/links`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ resourceType, resourceId }),
        });
      }

      setFile(null);
      setTitle('');
      setCategory('');

      if (input.current) {
        input.current.value = '';
      }

      onUploaded(document);
    } catch {
      setError(tErrors('network'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-4 rounded-md border border-border bg-surface p-4">
      <div className="flex flex-col gap-1.5">
        <label htmlFor="document-file" className="text-sm font-medium text-text">
          {t('file')}
          <span className="ms-1 font-normal text-text-muted">
            ({tCommon('required')})
          </span>
        </label>

        <input
          id="document-file"
          ref={input}
          type="file"
          onChange={(event) => setFile(event.target.files?.[0] ?? null)}
          className="rounded-md border border-border-strong bg-surface px-3 py-2 text-sm text-text file:me-3 file:rounded file:border file:border-border-strong file:bg-surface-sunken file:px-3 file:py-1 file:text-sm file:text-text"
        />
      </div>

      <Field
        label={t('documentTitle')}
        value={title}
        onChange={(event) => setTitle(event.target.value)}
        hint={t('titleHint')}
        maxLength={300}
      />

      <Field
        label={t('category')}
        value={category}
        onChange={(event) => setCategory(event.target.value)}
        hint={t('categoryHint')}
        maxLength={100}
      />

      {error ? <FormMessage tone="error">{error}</FormMessage> : null}

      <div>
        <Button
          type="button"
          onClick={() => void upload()}
          busy={busy}
          busyLabel={t('uploading')}
        >
          {t('upload')}
        </Button>
      </div>
    </div>
  );
}
