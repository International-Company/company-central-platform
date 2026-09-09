'use client';

import { useState } from 'react';
import type { ReactNode } from 'react';
import { Link, usePathname } from '@/i18n/routing';
import { localeLabel, locales, type Locale } from '@/i18n/config';

/**
 * The application shell: navigation, header, and the region pages render into.
 *
 * **No icons in the navigation** (ARCHITECTURE.md §9.5). Every item is its name,
 * and the name is what people scan for. A column of glyphs beside a column of
 * words is decoration that costs width and gives nothing back.
 *
 * **Mirrors completely in Arabic.** The sidebar moves to the right, the drawer
 * slides from the right, the breadcrumb separators reverse — all of it from
 * `dir="rtl"` on `<html>` plus logical properties (`border-e`, `ms-*`, `start-*`)
 * rather than physical `left`/`right`. That is the whole reason physical
 * utilities are prohibited and linted against: they survive the mirror and end
 * up on the wrong side.
 */

export interface NavItem {
  href: string;

  /** Already translated. This component holds no strings of its own. */
  label: string;
}

export interface AppShellLabels {
  appName: string;
  mainNavigation: string;
  openMenu: string;
  closeMenu: string;
  signOut: string;
  language: string;
  skipToContent: string;
}

export function AppShell({
  navigation,
  labels,
  locale,
  children,
}: {
  navigation: NavItem[];
  labels: AppShellLabels;
  locale: Locale;
  children: ReactNode;
}) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const pathname = usePathname();

  return (
    <div className="min-h-dvh">
      {/* First in the tab order, hidden until focused. A keyboard user should
          not have to walk the whole navigation on every page. */}
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:m-2 focus:rounded-md focus:bg-surface focus:px-3 focus:py-2 focus:text-sm"
      >
        {labels.skipToContent}
      </a>

      <div className="flex min-h-dvh">
        {/* Wide screens: a permanent sidebar. `border-e` is the *end* edge, so
            it is on the right in Arabic without a second rule. */}
        <aside
          className="hidden w-60 shrink-0 border-e border-border bg-surface lg:block"
          data-print-hidden
        >
          <SidebarContent
            navigation={navigation}
            labels={labels}
            pathname={pathname}
          />
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <header
            className="flex h-14 items-center justify-between gap-3 border-b border-border bg-surface px-4"
            data-print-hidden
          >
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={() => setDrawerOpen(true)}
                aria-expanded={drawerOpen}
                aria-controls="mobile-navigation"
                className="rounded-md border border-border-strong px-3 py-1.5 text-sm lg:hidden"
              >
                {labels.openMenu}
              </button>

              <span className="text-sm font-semibold text-text">
                {labels.appName}
              </span>
            </div>

            <div className="flex items-center gap-3">
              <LocaleSwitch current={locale} label={labels.language} />

              {/* The locale travels with the request. Without it the handler
                  fell back to Arabic, so an English reader signing out landed
                  on a sign-in page in a language they had not chosen. */}
              <form
                action={`/api/auth/sign-out?locale=${locale}`}
                method="post"
              >
                <button
                  type="submit"
                  className="text-sm text-primary-700 underline underline-offset-2"
                >
                  {labels.signOut}
                </button>
              </form>
            </div>
          </header>

          <main id="main" className="min-w-0 flex-1 p-4 lg:p-6">
            {children}
          </main>
        </div>
      </div>

      {/* Narrow screens: a drawer. It slides from the start edge, which mirrors
          with the document direction for free. */}
      {drawerOpen ? (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button
            type="button"
            aria-label={labels.closeMenu}
            onClick={() => setDrawerOpen(false)}
            className="absolute inset-0 bg-text/25"
          />

          <div
            id="mobile-navigation"
            className="absolute inset-y-0 start-0 w-64 border-e border-border bg-surface"
          >
            <div className="flex h-14 items-center justify-end px-4">
              <button
                type="button"
                onClick={() => setDrawerOpen(false)}
                className="rounded-md border border-border-strong px-3 py-1.5 text-sm"
              >
                {labels.closeMenu}
              </button>
            </div>

            <SidebarContent
              navigation={navigation}
              labels={labels}
              pathname={pathname}
              onNavigate={() => setDrawerOpen(false)}
            />
          </div>
        </div>
      ) : null}
    </div>
  );
}

function SidebarContent({
  navigation,
  labels,
  pathname,
  onNavigate,
}: {
  navigation: NavItem[];
  labels: AppShellLabels;
  pathname: string;
  onNavigate?: (() => void) | undefined;
}) {
  return (
    <nav aria-label={labels.mainNavigation} className="p-3">
      <ul className="flex flex-col gap-0.5">
        {navigation.map((item) => {
          const active =
            pathname === item.href || pathname.startsWith(`${item.href}/`);

          return (
            <li key={item.href}>
              <Link
                href={item.href}
                onClick={onNavigate}
                // Marks the current page for assistive technology. Highlighting
                // it visually and saying nothing would leave a screen-reader
                // user unable to tell where they are.
                aria-current={active ? 'page' : undefined}
                className={
                  'block rounded-md px-3 py-2 text-sm ' +
                  (active
                    ? 'bg-primary-50 font-medium text-primary-700'
                    : 'text-text-secondary hover:bg-surface-sunken')
                }
              >
                {item.label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

/**
 * Switches language, keeping the reader where they are.
 *
 * The same path under the other locale — not a jump to the home page, which is
 * what a naive switcher does and which loses whatever the person was reading.
 */
function LocaleSwitch({ current, label }: { current: Locale; label: string }) {
  const pathname = usePathname();

  return (
    <nav aria-label={label} className="flex items-center gap-2 text-sm">
      {locales.map((locale) => (
        <Link
          key={locale}
          href={pathname}
          locale={locale}
          hrefLang={locale}
          aria-current={locale === current ? 'true' : undefined}
          className={
            locale === current
              ? 'font-medium text-text'
              : 'text-primary-700 underline underline-offset-2'
          }
        >
          {localeLabel[locale]}
        </Link>
      ))}
    </nav>
  );
}
