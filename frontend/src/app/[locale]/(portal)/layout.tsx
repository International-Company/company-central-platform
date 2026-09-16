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
          { href: '/tasks', label: t('tasks') },
          { href: '/notifications', label: t('notifications') },

          { href: '/users', label: t('users'), section: t('sectionPeople') },
          { href: '/employees', label: t('employees'), section: t('sectionPeople') },
          { href: '/organization', label: t('organization'), section: t('sectionPeople') },

          { href: '/workflow', label: t('workflow'), section: t('sectionOperations') },
          { href: '/documents', label: t('documents'), section: t('sectionOperations') },

          { href: '/roles', label: t('roles'), section: t('sectionAccess') },
          { href: '/security', label: t('security'), section: t('sectionAccess') },
          { href: '/audit', label: t('audit'), section: t('sectionAccess') },

          { href: '/applications', label: t('applications'), section: t('sectionPlatform') },
          { href: '/integrations', label: t('integrations'), section: t('sectionPlatform') },
          { href: '/configuration', label: t('configuration'), section: t('sectionPlatform') },
          { href: '/operations', label: t('operations'), section: t('sectionPlatform') },
        ]}
        labels={{
          appName: (await getTranslations('app'))('shortName'),
          mainNavigation: t('mainNavigation'),
          openMenu: t('openMenu'),
          closeMenu: t('closeMenu'),
          signOut: tCommon('signOut'),
          language: tCommon('language'),
          // Was tCommon('search'), so the first thing a keyboard user heard
          // on every page was the word for a search box that is not there.
          skipToContent: tCommon('skipToContent'),
          showSection: t('showSection'),
          hideSection: t('hideSection'),
        }}
      >
        {children}
      </AppShell>
    </PermissionProvider>
  );
}
