#!/usr/bin/env python3
"""Verifies that every relative link in the documentation points at something.

A broken link in a runbook is discovered by somebody at three in the morning who
needed the page it pointed at. Documentation rots silently -- a file gets renamed,
a section gets folded into another, and nothing anywhere complains -- so this is
checked by the build rather than by whoever last happened to click one.

External links (http, https, mailto) are not followed: that would make the build
depend on somebody else's uptime, and a red build caused by an unrelated website
being down is a build people learn to ignore.

Usage:
    python scripts/check-doc-links.py

Exit code 0 means every relative link resolves.
"""

import os
import re
import sys
from urllib.parse import unquote

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Directories whose contents are not ours to check.
SKIP_DIRECTORIES = {'.git', 'node_modules', 'bin', 'obj', '.next'}

# [text](target) -- deliberately not matching image syntax differently, since a
# broken image is a broken link.
LINK = re.compile(r'\[[^\]]*\]\(([^)]+)\)')

# A fenced code block. Links inside one are illustrations, not navigation: the
# integration guide shows example URLs a caller would request, and asserting
# those exist on disk would be nonsense.
FENCE = re.compile(r'```.*?```', re.DOTALL)


def markdown_files():
    for directory, subdirectories, files in os.walk(ROOT):
        subdirectories[:] = [d for d in subdirectories if d not in SKIP_DIRECTORIES]

        for name in files:
            if name.endswith('.md'):
                yield os.path.join(directory, name)


def broken_links_in(path):
    with open(path, encoding='utf-8') as handle:
        text = handle.read()

    text = FENCE.sub('', text)

    for target in LINK.findall(text):
        target = target.strip()

        if target.startswith(('http://', 'https://', 'mailto:', '#', '<')):
            continue

        # Strip an anchor: the file has to exist, and checking that a heading
        # exists means parsing every heading in every file for a payoff that is
        # mostly noise.
        target = target.split('#', 1)[0]

        if not target:
            continue

        resolved = os.path.normpath(
            os.path.join(os.path.dirname(path), unquote(target)))

        if not os.path.exists(resolved):
            yield target


def main():
    failures = 0

    for path in sorted(markdown_files()):
        relative = os.path.relpath(path, ROOT).replace(os.sep, '/')

        for target in broken_links_in(path):
            print(f'  BROKEN  {relative} -> {target}')
            failures += 1

    if failures == 0:
        print('Every relative documentation link resolves.')
        return 0

    print(f'\n{failures} broken link(s).', file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main())
