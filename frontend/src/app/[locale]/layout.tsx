import type { ReactNode } from 'react';
import { notFound } from 'next/navigation';
import { NextIntlClientProvider, hasLocale } from 'next-intl';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { ServiceWorker } from '@/components/pwa/service-worker';
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

  return {
    title: t('name'),
    description: t('description'),
    applicationName: t('shortName'),

    // Per locale, so the name on the home screen is the name of the pages the
    // person was reading when they installed it.
    manifest: `/${locale}/manifest.webmanifest`,

    appleWebApp: {
      capable: true,
      title: t('shortName'),

      // The status bar takes the application's own colour rather than
      // floating white text over whatever is underneath it.
      statusBarStyle: 'black-translucent',
    },

    icons: {
      icon: [
        { url: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
        { url: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
      ],
      apple: [{ url: '/icons/apple-touch-icon.png', sizes: '180x180', type: 'image/png' }],
    },
  };
}

/**
 * The colour the browser paints its own chrome, so the frame around the
 * application matches the bar at the top of it. It is the same deepest blue
 * the top bar is set in.
 */
export const viewport = {
  themeColor: '#13304d',

  // The application is installed and used on phones, and a table that has to
  // be zoomed is a table nobody reads. Zoom is never disabled: a person who
  // needs to magnify text is the person who most needs to.
  width: 'device-width',
  initialScale: 1,
};

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

        {/* Registers after load, and only in a production build. Nothing on
            any screen waits for it. */}
        <ServiceWorker />
      </body>
    </html>
  );
}
