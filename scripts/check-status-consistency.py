#!/usr/bin/env python3
"""Checks DEVELOPMENT_STATUS.md against itself and against the source tree.

This file is the first thing anybody reads to find out where the project is, and
its summary sections drifted further than anything else in the repository --
because the detail sections get edited every phase and the summaries get edited
when somebody remembers.

What that looked like when it was found, at Phase 20:

  * Section 2 opened with "No capability module has been implemented". Eleven
    were built, deployed and had screens. Five were marked not implemented, not
    tested, not documented and without a UI.
  * Section 1's summary line said "Completed: 1 of 22 phases" directly beneath a
    table showing eleven complete.
  * Phase 19, Cloud Deployment, said "not started" twenty-seven lines below the
    header recording the URLs the Platform was serving from.

Every one of those was found by a person re-reading. This checks the parts a
build can check:

  1. The phase summary line agrees with the phase table above it.
  2. Every module under src/Modules has a row in the module table.

What it cannot check is the rest of it -- whether a note beside a phase is still
true, whether "core complete" still means what it meant. Those are claims about
the world, and the only thing that catches them is somebody reading. The two
checks here are the ones with a shape.

Usage:
    python scripts/check-status-consistency.py

Exit code 0 means the document does not contradict itself in the ways that can
be mechanically detected.
"""

import os
import re
import sys

# A Windows console defaults to a code page that cannot print the characters
# this repository's documentation is full of.
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATUS = os.path.join(ROOT, 'DEVELOPMENT_STATUS.md')
MODULES = os.path.join(ROOT, 'src', 'Modules')

COMPLETE = ('✅', '\U0001F7E2')   # white heavy check mark, green circle
IN_PROGRESS = ('\U0001F7E1',)          # yellow circle
NOT_STARTED = ('⬜',)              # white large square

# "| 19 | Cloud Deployment | <status> | <note> |" -- the phase table's rows, and
# only those: the leading number is what distinguishes them from the module
# table and from the debt register further down.
PHASE_ROW = re.compile(r'^\|\s*(\d{1,2})\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|')

# "**11 of 22 phases complete** ... **9 in progress**, **2 not started**"
SUMMARY = re.compile(
    r'\*\*(\d+) of (\d+) phases complete\*\*.*?\*\*(\d+) in progress\*\*.*?\*\*(\d+) not started\*\*',
    re.DOTALL)


def section(text, heading, following):
    """The text between two headings."""
    start = text.index(heading)
    end = text.index(following, start)

    return text[start:end]


def phase_counts(text):
    """Counts the phase table's rows by the symbol in their status cell."""
    complete = in_progress = not_started = 0
    rows = 0

    for line in section(text, '## 1. Overall progress', '## 2. Module status').splitlines():
        match = PHASE_ROW.match(line)

        if not match:
            continue

        status = match.group(3)
        rows += 1

        if any(symbol in status for symbol in COMPLETE):
            complete += 1
        elif any(symbol in status for symbol in IN_PROGRESS):
            in_progress += 1
        elif any(symbol in status for symbol in NOT_STARTED):
            not_started += 1
        else:
            print(f'  UNREADABLE  phase {match.group(1)} ({match.group(2)}) has no status symbol')
            rows -= 1

    return complete, in_progress, not_started, rows


def check_phase_summary(text):
    """The sentence that counts the table must count the table."""
    complete, in_progress, not_started, rows = phase_counts(text)

    # A parser that matches nothing reports no problems for ever. Every version
    # of this document has had a phase table with more than a dozen rows.
    if rows < 12:
        print(f'  BROKEN  only {rows} phase rows were parsed, so the phase table is not being '
              f'read. Fix this script rather than trusting it.', file=sys.stderr)
        return 1

    summary = SUMMARY.search(section(text, '## 1. Overall progress', '## 2. Module status'))

    if summary is None:
        print('  MISSING  section 1 has no summary line of the form "**N of M phases complete** '
              '... **N in progress**, **N not started**". It had one, and it disagreed with the '
              'table; the answer is to correct it, not to delete it.', file=sys.stderr)
        return 1

    claimed = (int(summary.group(1)), int(summary.group(3)), int(summary.group(4)))
    actual = (complete, in_progress, not_started)

    if claimed != actual:
        print(f'  CONTRADICTION  the summary line says {claimed[0]} complete, {claimed[1]} in '
              f'progress, {claimed[2]} not started. The table above it says {actual[0]}, '
              f'{actual[1]}, {actual[2]}.', file=sys.stderr)
        return 1

    total = int(summary.group(2))

    if total < rows:
        print(f'  CONTRADICTION  the summary line says {total} phases; the table has {rows} rows.',
              file=sys.stderr)
        return 1

    return 0


def check_module_inventory(text):
    """Every module in the source tree has a row in the module table."""
    if not os.path.isdir(MODULES):
        print(f'  BROKEN  {MODULES} was not found, so no module could be checked.',
              file=sys.stderr)
        return 1

    built = sorted(name for name in os.listdir(MODULES)
                   if os.path.isdir(os.path.join(MODULES, name)))

    if not built:
        print('  BROKEN  src/Modules contains no modules, so the check is passing on an empty set.',
              file=sys.stderr)
        return 1

    table = section(text, '## 2. Module status', '## 3.')
    missing = [name for name in built if f'| {name} |' not in table]

    if missing:
        print('  MISSING  these modules are in src/Modules and have no row in the module table. '
              'That table was once frozen for nineteen phases while five complete modules sat in '
              'it marked as not built, so a module arriving without a row is exactly the event '
              'worth failing on:', file=sys.stderr)

        for name in missing:
            print(f'            {name}', file=sys.stderr)

        return 1

    return 0


def main():
    if not os.path.exists(STATUS):
        print(f'{STATUS} was not found.', file=sys.stderr)
        return 1

    with open(STATUS, encoding='utf-8') as handle:
        text = handle.read()

    failures = check_phase_summary(text) + check_module_inventory(text)

    if failures:
        print('\nDEVELOPMENT_STATUS.md contradicts itself. It is the first thing anybody reads to '
              'find out where this project is.', file=sys.stderr)
        return 1

    complete, in_progress, not_started, rows = phase_counts(text)
    print(f'DEVELOPMENT_STATUS.md agrees with itself: {rows} phases '
          f'({complete} complete, {in_progress} in progress, {not_started} not started), '
          f'and every module in src/Modules has a row.')

    return 0


if __name__ == '__main__':
    sys.exit(main())
