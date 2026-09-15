import type { ReactNode } from 'react';
import { notFound } from 'next/navigation';
import { NextIntlClientProvider, hasLocale } from 'next-intl';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { direction } from '@/i18n/config';
import { routing } from '@/i18n/routing';
import { IBM_Plex_Sans, IBM_Plex_Sans_Arabic } from 'next/font/google';
import '@/styles/globals.css';

// Self-hosted at build time: the files are downloaded once by `next build` and
// served from this application, so no visitor's browser asks Google for
// anything. The token set named these faces for many phases and nothing ever
// loaded them, so every screen rendered in whatever the machine had instead.
const plexSans = IBM_Plex_Sans({
  subsets: ['latin'],
  weight: ['400', '500', '600', '700'],
  variable: '--font-plex-sans',
  display: 'swap',
});

const plexArabic = IBM_Plex_Sans_Arabic({
  subsets: ['arabic'],
  weight: ['400', '500', '600', '700'],
  variable: '--font-plex-arabic',
  display: 'swap',
});

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  if (!hasLocale(routing.locales, locale)) {
    return {};
  }

  const t = await getTranslations({ locale, namespace: 'app' });

  return { title: t('name') };
}

export default async function LocaleLayout({
  children,
  params,
}: {
  children: ReactNode;
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  if (!hasLocale(routing.locales, locale)) {
    notFound();
  }

  setRequestLocale(locale);

  return (
    // `dir` here is what mirrors the entire layout, not just the text. Every
    // component then uses logical properties and needs no direction-specific
    // rule of its own (ARCHITECTURE.md §9.4).
    <html lang={locale} dir={direction[locale]} className={`${plexSans.variable} ${plexArabic.variable}`}>
      <body>
        <NextIntlClientProvider>{children}</NextIntlClientProvider>
      </body>
    </html>
  );
}
