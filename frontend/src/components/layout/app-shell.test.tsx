import { fireEvent, render, screen } from '@testing-library/react';
import type { AnchorHTMLAttributes } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AppShell } from './app-shell';

/**
 * The navigation sections open and close, checked by clicking them.
 *
 * A collapsing menu is the kind of thing that reads correctly and behaves
 * wrongly: the state is right and nothing is hidden, or the current page is
 * folded away behind a heading nobody thinks to open. Both were possible here,
 * and neither is visible in the source.
 */

let currentPath = '/dashboard';

vi.mock('@/i18n/routing', () => ({
  usePathname: () => currentPath,
  Link: ({ href, children, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement>) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

const labels = {
  appName: 'Platform',
  mainNavigation: 'Main navigation',
  openMenu: 'Open menu',
  closeMenu: 'Close menu',
  signOut: 'Sign out',
  language: 'Language',
  skipToContent: 'Skip to content',
  showSection: 'Show',
  hideSection: 'Hide',
};

const navigation = [
  { href: '/dashboard', label: 'Dashboard' },
  { href: '/users', label: 'Users', section: 'People' },
  { href: '/employees', label: 'Employees', section: 'People' },
  { href: '/roles', label: 'Roles', section: 'Access' },
];

function show(path: string) {
  currentPath = path;

  return render(
    <AppShell navigation={navigation} labels={labels} locale="en">
      <p>Page</p>
    </AppShell>,
  );
}

/** The heading of a section, in the permanent sidebar. */
function heading(name: string): HTMLElement {
  const buttons = screen.getAllByRole('button', { name: new RegExp(`^${name}`) });

  // The sidebar is first in the document; the drawer, when open, is second.
  return buttons[0] as HTMLElement;
}

describe('the navigation sections', () => {
  it('start closed, and open when their heading is clicked', () => {
    show('/dashboard');

    // An ungrouped item belongs to no section and is always there.
    expect(screen.getByRole('link', { name: 'Dashboard' })).toBeDefined();

    // getByRole reads the accessibility tree, so an item inside a closed
    // section is not merely styled away: it is not reachable at all.
    expect(screen.queryByRole('link', { name: 'Users' })).toBeNull();
    expect(heading('People').getAttribute('aria-expanded')).toBe('false');

    fireEvent.click(heading('People'));

    expect(screen.getByRole('link', { name: 'Users' })).toBeDefined();
    expect(screen.getByRole('link', { name: 'Employees' })).toBeDefined();
    expect(heading('People').getAttribute('aria-expanded')).toBe('true');

    // One section opening does not open the rest.
    expect(screen.queryByRole('link', { name: 'Roles' })).toBeNull();
  });

  it('closes again on a second click', () => {
    show('/dashboard');

    fireEvent.click(heading('People'));
    fireEvent.click(heading('People'));

    expect(screen.queryByRole('link', { name: 'Users' })).toBeNull();
  });

  it('opens the section holding the current page, without a click', () => {
    // The failure this prevents: signing in on a page whose section is closed,
    // so nothing in the sidebar says where the reader is.
    show('/users');

    expect(screen.getByRole('link', { name: 'Users' })).toBeDefined();
    expect(heading('People').getAttribute('aria-expanded')).toBe('true');

    // A child route counts as being in the section.
    screen.getByRole('link', { name: 'Users' });

    expect(screen.queryByRole('link', { name: 'Roles' })).toBeNull();
  });

  it('counts a page underneath an item as that item', () => {
    show('/users/42');

    expect(heading('People').getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByRole('link', { name: 'Users' }).getAttribute('aria-current')).toBe('page');
  });

  it('says in a word what clicking will do', () => {
    // The whole affordance. There is no caret, so if the word goes the heading
    // looks like a label and nobody clicks it.
    show('/dashboard');

    expect(heading('People').textContent).toContain('Show');

    fireEvent.click(heading('People'));

    expect(heading('People').textContent).toContain('Hide');
  });

  it('gives the sidebar and the drawer separate lists', () => {
    show('/dashboard');

    // Both are in the document once the drawer is open. Sharing an id would
    // leave every aria-controls pointing at whichever the browser found first,
    // and the drawer heading would announce the sidebar's state.
    fireEvent.click(screen.getByRole('button', { name: 'Open menu' }));

    const controlled = screen
      .getAllByRole('button', { name: /^People/ })
      .map((button) => button.getAttribute('aria-controls'));

    expect(controlled).toHaveLength(2);
    expect(new Set(controlled).size).toBe(2);

    for (const id of controlled) {
      expect(id).not.toBeNull();
      expect(document.getElementById(id as string)).not.toBeNull();
    }
  });
});
