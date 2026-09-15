import { notFound } from 'next/navigation';
import { hasLocale } from 'next-intl';
import { routing } from '@/i18n/routing';
import { DesignPreview } from './design-preview';

/**
 * The shared components with sample data, for looking at the design.
 *
 * The real screens need a database and a signed-in administrator, which no
 * developer machine here has. This renders the same components the screens are
 * built from — shell, header, table, statuses, buttons, fields, messages,
 * dialogs — so a change to the design can be seen before it ships.
 *
 * **Not reachable in production.** A page of invented users behind no sign-in
 * is harmless as data and still has no business being served.
 */
export default async function DesignPreviewPage({
  params,
  searchParams,
}: {
  params: Promise<{ locale: string }>;
  searchParams: Promise<{ dialog?: string; view?: string }>;
}) {
  if (process.env.NODE_ENV === 'production') {
    notFound();
  }

  const { locale } = await params;
  const { dialog, view } = await searchParams;

  if (!hasLocale(routing.locales, locale)) {
    notFound();
  }

  return <DesignPreview locale={locale} dialog={dialog ?? null} view={view ?? null} />;
}
