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
 * **Grouped, because fifteen items in one column cannot be scanned.** The
 * sections are named the way the company would describe the work, and the
 * active page is marked by a rule on its start edge rather than a rounded pill
 * of tint — the pill being the most recognisable trait of a generated sidebar.
 *
 * **The top bar is the application's identity**, set in the deepest blue. It
 * was a white strip with the name in body text, which left nothing on any
 * screen saying which system this was.
 *
 * **Mirrors completely in Arabic.** The sidebar moves to the right and the
 * drawer slides from the right, from `dir="rtl"` on `<html>` plus logical
 * properties (`border-e`, `border-s`, `start-*`) rather than physical
 * `left`/`right`, which survive the mirror and end up on the wrong side.
 */

export interface NavItem {
  href: string;

  /** Already translated. This component holds no strings of its own. */
  label: string;

  /**
   * The section this item belongs to, already translated. Consecutive items
   * with the same section are grouped under one heading; an item with none
   * sits at the top without one.
   */
  section?: string | undefined;
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
    <div className="flex min-h-dvh flex-col">
      {/* First in the tab order, hidden until focused. A keyboard user should
          not have to walk the whole navigation on every page. */}
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:m-2 focus:bg-surface focus:px-3 focus:py-2 focus:text-sm focus:text-text"
      >
        {labels.skipToContent}
      </a>

      <header
        className="flex h-14 shrink-0 items-center justify-between gap-4 bg-primary-900 px-4 text-text-on-primary lg:px-6"
        data-print-hidden
      >
        <div className="flex min-w-0 items-center gap-4">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            aria-expanded={drawerOpen}
            aria-controls="mobile-navigation"
            className="rounded-sm border border-primary-300 px-3 py-1 text-sm lg:hidden"
          >
            {labels.openMenu}
          </button>

          <span className="truncate whitespace-nowrap text-base font-semibold tracking-tight">{labels.appName}</span>
        </div>

        <div className="hidden shrink-0 items-center gap-6 text-sm sm:flex">
          <LocaleSwitch current={locale} label={labels.language} tone="dark" />

          {/* The locale travels with the request. Without it the handler
              fell back to Arabic, so an English reader signing out landed
              on a sign-in page in a language they had not chosen. */}
          <form action={`/api/auth/sign-out?locale=${locale}`} method="post">
            <button
              type="submit"
              className="whitespace-nowrap rounded-sm border border-primary-300 px-3 py-1 hover:border-text-on-primary"
            >
              {labels.signOut}
            </button>
          </form>
        </div>
      </header>

      <div className="flex min-h-0 flex-1">
        {/* Wide screens: a permanent sidebar. `border-e` is the *end* edge, so
            it is on the right in Arabic without a second rule. */}
        <aside
          className="hidden w-64 shrink-0 border-e border-border bg-surface lg:block"
          data-print-hidden
        >
          <SidebarContent navigation={navigation} labels={labels} pathname={pathname} />
        </aside>

        <main id="main" className="min-w-0 flex-1 bg-surface px-4 py-6 lg:px-10 lg:py-8">
          <div className="mx-auto max-w-[1400px]">{children}</div>
        </main>
      </div>

      {/* Narrow screens: a drawer. It slides from the start edge, which mirrors
          with the document direction for free. */}
      {drawerOpen ? (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button
            type="button"
            aria-label={labels.closeMenu}
            onClick={() => setDrawerOpen(false)}
            className="absolute inset-0 bg-text/40"
          />

          <div
            id="mobile-navigation"
            className="absolute inset-y-0 start-0 w-72 overflow-y-auto border-e border-border bg-surface"
          >
            <div className="flex h-14 items-center justify-between bg-primary-900 px-4 text-text-on-primary">
              <span className="text-base font-semibold">{labels.appName}</span>

              <button
                type="button"
                onClick={() => setDrawerOpen(false)}
                className="rounded-sm border border-primary-300 px-3 py-1 text-sm"
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

            {/* On a phone the top bar has room for the name and the menu only,
                so language and sign-out live here. Shown at every width the
                drawer is, which is below the large breakpoint. */}
            <div className="flex items-center justify-between gap-4 border-t border-border px-5 py-4 text-sm">
              <LocaleSwitch current={locale} label={labels.language} tone="light" />

              <form action={`/api/auth/sign-out?locale=${locale}`} method="post">
                <button
                  type="submit"
                  className="whitespace-nowrap rounded-sm border border-border-strong px-3 py-1 text-text hover:border-primary-700"
                >
                  {labels.signOut}
                </button>
              </form>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}

/** Consecutive items sharing a section, in the order they were given. */
function groups(navigation: NavItem[]): { section: string | undefined; items: NavItem[] }[] {
  const result: { section: string | undefined; items: NavItem[] }[] = [];

  for (const item of navigation) {
    const last = result.at(-1);

    if (last && last.section === item.section) {
      last.items.push(item);
    } else {
      result.push({ section: item.section, items: [item] });
    }
  }

  return result;
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
    <nav aria-label={labels.mainNavigation} className="py-4">
      {groups(navigation).map((group, index) => (
        <div key={group.section ?? `group-${index}`} className={index > 0 ? 'mt-5' : ''}>
          {group.section ? (
            <p className="px-5 pb-1.5 text-xs font-semibold text-text-muted">{group.section}</p>
          ) : null}

          <ul className="flex flex-col">
            {group.items.map((item) => {
              const active = pathname === item.href || pathname.startsWith(`${item.href}/`);

              return (
                <li key={item.href}>
                  <Link
                    href={item.href}
                    onClick={onNavigate}
                    // Marks the current page for assistive technology.
                    // Highlighting it visually and saying nothing would leave a
                    // screen-reader user unable to tell where they are.
                    aria-current={active ? 'page' : undefined}
                    className={
                      'block border-s-[3px] px-5 py-2 text-sm ' +
                      (active
                        ? 'border-primary-700 bg-primary-50 font-semibold text-primary-900'
                        : 'border-transparent text-text-secondary hover:bg-surface-sunken hover:text-text')
                    }
                  >
                    {item.label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}

/**
 * Switches language, keeping the reader where they are.
 *
 * The same path under the other locale — not a jump to the home page, which is
 * what a naive switcher does and which loses whatever the person was reading.
 *
 * **It also remembers.** The portal knows which language to show from the URL;
 * an email arrives with nobody present to have opened a URL, so a notification
 * is in the right language only if the choice was written down. Switching here
 * is the moment somebody expresses that choice, and the only one they would
 * think to look for.
 */
function LocaleSwitch({
  current,
  label,
  tone,
}: {
  current: Locale;
  label: string;

  /** Which surface it sits on: the top bar, or the drawer. */
  tone: 'dark' | 'light';
}) {
  const pathname = usePathname();

  /**
   * Records the choice without getting in the way of the navigation.
   *
   * `keepalive` exists for exactly this: the request outlives the page it was
   * started from, so the link behaves as a link and the preference still
   * arrives. Failures are ignored on purpose — being unable to store a
   * preference must not stop somebody changing the language they are reading
   * in, which is the thing they actually asked for.
   */
  function remember(locale: Locale) {
    void fetch('/api/me/language', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ locale }),
      keepalive: true,
    }).catch(() => undefined);
  }

  return (
    <nav aria-label={label} className="flex items-center gap-4">
      {locales.map((locale) => (
        <Link
          key={locale}
          href={pathname}
          locale={locale}
          hrefLang={locale}
          onClick={() => remember(locale)}
          aria-current={locale === current ? 'true' : undefined}
          className={
            tone === 'dark'
              ? locale === current
                ? 'font-semibold text-text-on-primary'
                : 'text-primary-100 hover:text-text-on-primary hover:underline underline-offset-4'
              : locale === current
                ? 'font-semibold text-text'
                : 'text-primary-700 hover:text-primary-900 hover:underline underline-offset-4'
          }
        >
          {localeLabel[locale]}
        </Link>
      ))}
    </nav>
  );
}
