import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { DataTable, EmptyState } from './data-table';

/**
 * The table every list screen is built from.
 *
 * The check that matters here is the shape of the document rather than the
 * look of it. The separator between row actions is drawn by a rule on every
 * child after the first, so it appears only while the actions really are
 * siblings — and the first attempt at it produced none anywhere, because every
 * screen wraps its actions in a permission guard and the separators were being
 * inserted by walking React's children, where a guard is one child.
 */

interface Row {
  id: string;
  name: string;
  count: number;
}

const rows: Row[] = [
  { id: '1', name: 'Finance', count: 12 },
  { id: '2', name: 'Legal', count: 3 },
];

const labels = {
  noResults: 'No results',
  noResultsDescription: 'Nothing matches.',
  sortAscending: 'Ascending',
  sortDescending: 'Descending',
  actions: 'Actions',
};

const columns = [
  { key: 'name', header: 'Name', render: (row: Row) => row.name },
  { key: 'count', header: 'Count', numeric: true, render: (row: Row) => String(row.count) },
];

/** Stands in for the permission guard every screen wraps its actions in. */
function Guard({ children }: { children: ReactNode }) {
  return <>{children}</>;
}

function show(rowActions?: (row: Row) => ReactNode) {
  return render(
    <DataTable
      columns={columns}
      rows={rows}
      rowKey={(row) => row.id}
      labels={labels}
      caption="Units"
      {...(rowActions ? { rowActions } : {})}
    />,
  );
}

describe('row actions', () => {
  it('are siblings in the document even behind a guard', () => {
    show(() => (
      <Guard>
        <>
          <button type="button">Edit</button>
          <Guard>
            <button type="button">Disable</button>
          </Guard>
        </>
      </Guard>
    ));

    // Two rows, and the phone list renders the same actions again, so each
    // button appears four times. Any of them will do: what is being asked is
    // whether the two share a parent.
    const edit = screen.getAllByRole('button', { name: 'Edit' })[0];
    const disable = screen.getAllByRole('button', { name: 'Disable' })[0];

    expect(edit).toBeDefined();
    expect(disable).toBeDefined();

    // The guards and the fragment must have left nothing between them. If a
    // wrapper comes back the parents differ, the rule lands on a container
    // instead of on an action, and every row silently loses its separators.
    expect(disable?.parentElement).toBe(edit?.parentElement);

    // And the container is the one carrying the rule, not some div a screen
    // introduced inside it.
    expect(edit?.parentElement?.className).toContain('[&>*+*]:before:w-px');
  });

  it('are left out entirely when a screen offers none', () => {
    show();

    expect(screen.queryByText('Actions')).toBeNull();
  });
});

describe('the table', () => {
  it('renders every row and marks the numeric cells', () => {
    show();

    expect(screen.getAllByText('Finance').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Legal').length).toBeGreaterThan(0);

    // Tabular figures come from the attribute, so a column that stops being
    // marked numeric stops lining up and nothing says so.
    const numeric = document.querySelectorAll('td[data-numeric]');

    expect(numeric.length).toBe(rows.length);
  });

  it('says so when there is nothing, instead of showing an empty table', () => {
    render(
      <DataTable
        columns={columns}
        rows={[]}
        rowKey={(row) => row.id}
        labels={labels}
        caption="Units"
      />,
    );

    expect(screen.getByText('No results')).toBeDefined();
    expect(screen.queryByRole('table')).toBeNull();
  });
});

describe('the empty state', () => {
  it('is centred rather than boxed', () => {
    // It was a bordered block with its text on the start edge, which is the
    // shape of an error banner. "No results" is not an error.
    const { container } = render(<EmptyState title="Nothing here" />);

    expect(container.firstElementChild?.className).toContain('text-center');
    expect(container.firstElementChild?.className).not.toContain('border border-border');
  });
});
