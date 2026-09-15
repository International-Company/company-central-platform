'use client';

import { useTranslations } from 'next-intl';

/**
 * A value that is not there, said in words.
 *
 * Empty cells used to show an em dash. It is the one symbol that crept into
 * every table in the portal, and it asks the reader to know a typographic
 * convention to understand that a field is blank. The word does not, reads the
 * same to a screen reader as to everyone else, and is quiet enough not to
 * compete with the values that are present.
 */
export function EmptyValue() {
  const t = useTranslations('common');

  return <span className="text-text-muted">{t('none')}</span>;
}
