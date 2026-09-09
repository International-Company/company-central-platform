import type { ReactNode } from 'react';
import { redirect } from 'next/navigation';
import { getTranslations } from 'next-intl/server';
import { AppShell } from '@/components/layout/app-shell';
import { PermissionProvider } from '@/lib/permissions';
import { callPlatform } from '@/lib/platform-client';
import { readSession } from '@/lib/session';
import type { MyPermissionsDto } from '@/types/platform';
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

  // Fetched here, once per navigation, and handed down. Every screen needs it
  // to decide which controls to render, and a screen that fetched its own would
  // make the same request again on each one.
  //
  // Straight to the Platform rather than through this application's own BFF
  // route: this is already the server, and a server component calling its own
  // HTTP endpoint is a round trip through the network to reach code in the same
  // process.
  //
  // A failure leaves the set empty, which hides controls rather than showing
  // ones the Platform would refuse. The screens still work — reading is what
  // most of them do — and the person sees fewer buttons rather than a broken
  // page. Hiding is UX; the Platform decides.
  const permissions = await callPlatform<MyPermissionsDto>({
    path: '/api/v1/me/permissions',

    // Rendering, so no refresh may be attempted: rotating the token here would
    // spend it and then be unable to store what it got back.
    duringRender: true,
  });

  return (
    <PermissionProvider granted={permissions.data?.permissions ?? []}>
      <AppShell
        locale={isLocale(locale) ? locale : 'ar'}
        navigation={[
          { href: '/dashboard', label: t('dashboard') },

          // Second, because it is the one item most people in the company will
          // ever use. Everything below it is administration.
          { href: '/tasks', label: t('tasks') },
          { href: '/notifications', label: t('notifications') },
          { href: '/users', label: t('users') },
          { href: '/employees', label: t('employees') },
          { href: '/organization', label: t('organization') },
          { href: '/workflow', label: t('workflow') },
          { href: '/roles', label: t('roles') },
          { href: '/security', label: t('security') },
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
    </PermissionProvider>
  );
}
