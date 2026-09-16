'use client';

import { useId, useState } from 'react';
import type { ReactNode } from 'react';
import { Link, usePathname } from '@/i18n/routing';
import { LocaleSwitch } from './locale-switch';
import type { Locale } from '@/i18n/config';

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
 * **The sections collapse.** Fifteen items is a column that has to be read
 * rather than scanned; four headings is a column that can be. A section opens
 * when its heading is clicked, and the one holding the current page is open
 * already, so the reader always sees where they are without opening anything.
 *
 * **The state is said in a word, not in a caret.** Every heading carries the
 * word that acts on it, because a glyph is a convention the reader has to know
 * and the word is not (ARCHITECTURE.md §9.5). Assistive technology is told
 * the same thing through `aria-expanded` rather than by hearing the word.
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

  /** On a collapsed section heading: the word that opens it. */
  showSection: string;

  /** On an open section heading: the word that closes it. */
  hideSection: string;
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

/** Whether a page is the one being shown, or lives underneath it. */
function isCurrent(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`);
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
  /**
   * Which sections the reader has opened or closed by hand.
   *
   * Only the exceptions are stored. A section with no entry follows the page:
   * open when it holds the current one, closed otherwise. That is what makes
   * navigation work without an effect watching the path — arriving at a page
   * opens the section it lives in, because nothing was ever said about it.
   *
   * Not persisted. Browser storage is forbidden to this application, and a
   * navigation preference is not worth a round trip to the Platform.
   */
  const [openedByHand, setOpenedByHand] = useState<Record<string, boolean>>({});

  // The sidebar and the phone drawer are two instances of this component, and
  // both are in the document at once. Without a unique prefix they would emit
  // the same list ids twice, and every aria-controls would point at whichever
  // one the browser found first.
  const prefix = useId();

  return (
    <nav aria-label={labels.mainNavigation} className="py-4">
      {groups(navigation).map((group, index) => {
        const section = group.section;
        const holdsCurrent = group.items.some((item) => isCurrent(pathname, item.href));
        const open = section === undefined ? true : openedByHand[section] ?? holdsCurrent;
        const listId = `${prefix}-section-${index}`;

        return (
          <div key={section ?? `group-${index}`} className={index > 0 ? 'mt-5' : ''}>
            {section !== undefined ? (
              <button
                type="button"
                onClick={() =>
                  setOpenedByHand((current) => ({ ...current, [section]: !open }))
                }
                aria-expanded={open}
                aria-controls={listId}
                className="flex w-full items-center justify-between gap-3 px-5 py-2 text-start text-xs font-semibold text-text-muted hover:bg-surface-sunken hover:text-text"
              >
                <span className="truncate">{section}</span>

                {/* For the eye only. A screen reader is told the same thing by
                    aria-expanded, and would otherwise hear it twice. */}
                <span aria-hidden="true" className="shrink-0 font-normal">
                  {open ? labels.hideSection : labels.showSection}
                </span>
              </button>
            ) : null}

            {/* Always in the document, hidden by display. A collapsed section
                that rendered nothing would leave aria-controls pointing at an
                element that does not exist.

                Hidden by the attribute as well as the class: the attribute is
                what takes it out of the accessibility tree and out of the tab
                order, and it still holds if the stylesheet never arrives. */}
            <ul id={listId} hidden={!open} className={open ? 'flex flex-col' : 'hidden'}>
              {group.items.map((item) => {
                const active = isCurrent(pathname, item.href);

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
        );
      })}
    </nav>
  );
}
