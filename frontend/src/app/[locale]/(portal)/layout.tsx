import type { ReactNode } from 'react';
import { redirect } from 'next/navigation';
import { getTranslations } from 'next-intl/server';
import { AppShell } from '@/components/layout/app-shell';
import { readSession } from '@/lib/session';
import { isLocale } from '@/i18n/config';

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
