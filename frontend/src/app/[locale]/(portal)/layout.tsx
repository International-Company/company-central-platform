import type { ReactNode } from 'react';
import { redirect } from 'next/navigation';
import { getTranslations } from 'next-intl/server';
import { AppShell } from '@/components/layout/app-shell';
import { readSession } from '@/lib/session';
import { isLocale } from '@/i18n/config';

/**
 * Rendered per request, never prerendered.
 *
 * Without this the portal pages were generated at build time — when there is no
 * session — so the redirect to sign-in was baked into static HTML and served to
 * everyone, signed in or not. A user could authenticate successfully and still
 * be bounced back to the login page forever.
 *
 * `generateStaticParams` on the locale layout above enables static rendering for
 * the whole subtree, which is right for the public pages and wrong for these.
 * Every page here depends on who is asking.
 */
export const dynamic = 'force-dynamic';

/**
 * Everything behind sign-in.
 *
 * The session is checked on the server, before anything renders. A client-side
 * check would ship the page to the browser first and hide it afterwards, which
 * is a curtain rather than a lock.
 */
export default async function PortalLayout({
  children,
  params,
}: {
  children: ReactNode;
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  const session = await readSession();

  if (!session) {
    redirect(`/${locale}/login`);
  }

  const t = await getTranslations('nav');
  const tCommon = await getTranslations('common');

  return (
    <AppShell
      locale={isLocale(locale) ? locale : 'ar'}
      navigation={[
        { href: '/dashboard', label: t('dashboard') },
        { href: '/users', label: t('users') },
        { href: '/employees', label: t('employees') },
        { href: '/organization', label: t('organization') },
        { href: '/roles', label: t('roles') },
        { href: '/audit', label: t('audit') },
      ]}
      labels={{
        appName: (await getTranslations('app'))('shortName'),
        mainNavigation: t('mainNavigation'),
        openMenu: t('openMenu'),
        closeMenu: t('closeMenu'),
        signOut: tCommon('signOut'),
        language: tCommon('language'),
        skipToContent: tCommon('search'),
      }}
    >
      {children}
    </AppShell>
  );
}
