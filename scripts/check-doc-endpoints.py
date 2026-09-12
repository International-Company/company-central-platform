#!/usr/bin/env python3
"""Verifies that every endpoint the documentation cites actually exists.

Documentation drift is this project's most common defect by a wide margin. In one
afternoon, four documents were found describing a Platform that had stopped
existing phases earlier -- endpoints listed as missing that had shipped, a table
of six unbuilt things of which five were built, and an operational warning
telling readers not to deploy because of a limitation fixed twenty phases ago.

Every one of those was found by a person reading. None of them was found by the
build, because nothing checked. This checks the half that can be checked: when a
document says

    POST /api/v1/applications/permissions

the contract is asked whether that endpoint is really there, with that method.

What this cannot check is the opposite drift -- a document saying something is
*not* built when it is. That is a claim about absence, and absence has no shape a
scan can match. It stays a human job, which is the honest limit and the reason
the register is worth re-reading rather than only appending to.

Usage:
    python scripts/check-doc-endpoints.py

Exit code 0 means every cited endpoint exists in contracts/platform-api.json.
"""

import json
import os
import re
import sys

# A Windows console defaults to a code page that cannot print the characters this
# repository's documentation is full of. Without this the script dies reporting a
# finding rather than reporting it.
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTRACT = os.path.join(ROOT, 'contracts', 'platform-api.json')

SKIP_DIRECTORIES = {'.git', 'node_modules', 'bin', 'obj', '.next'}

# "POST /api/v1/..." wherever it appears -- in a fenced block, a table cell or a
# sentence. Code fences are deliberately *not* skipped here: an example request
# in the integration guide is exactly the kind of citation that goes stale, and
# it is the one a reader is most likely to copy.
CITATION = re.compile(
    # The path characters are spelled out rather than excluded, because a
    # documented route is ASCII, and an exclusion list runs happily on into
    # the box-drawing characters of whatever diagram follows.
    r'\b(GET|POST|PUT|PATCH|DELETE)\s+(/api/v\d+[A-Za-z0-9/_\-.{}]*)',
    re.IGNORECASE)

# Citations that name something deliberately absent, each with the reason. A
# document arguing that an endpoint does not exist has to be able to name it.
ALLOWED_ABSENT = {
    # ARCHITECTURE.md describes the intended design, so it may legitimately name
    # something not yet built. That is the only document with the standing to,
    # and each case needs a reason here and a note in the document itself -- a
    # reader should not have to cross-reference to learn that an endpoint listed
    # beside two real ones does not exist.
    ('POST', '/api/v1/audit/exports'):
        'Asynchronous signed export, designed in Phase 6 and not built (task 7). '
        'Recorded in DEVELOPMENT_STATUS.md and in docs/audit/README.md section 9, '
        'and marked as unbuilt at the point ARCHITECTURE.md names it.',
}


def contract_routes():
    """Every method and path the Platform actually publishes."""
    with open(CONTRACT, encoding='utf-8') as handle:
        document = json.load(handle)

    routes = []

    for path, operations in document['paths'].items():
        for method in operations:
            if method.lower() in ('get', 'post', 'put', 'patch', 'delete'):
                routes.append((method.upper(), path))

    return routes


def matches(cited, published):
    """Whether a cited path is the published one, allowing for parameters.

    A document writes what a caller would actually request -- a concrete key, a
    real resource id -- where the contract writes a template. Comparing the two
    literally would reject every useful example, so a templated segment matches
    anything that is not a slash.
    """
    cited_segments = [s for s in cited.split('/') if s]
    published_segments = [s for s in published.split('/') if s]

    if len(cited_segments) != len(published_segments):
        return False

    for mine, theirs in zip(cited_segments, published_segments):
        if theirs.startswith('{') and theirs.endswith('}'):
            continue

        # The document may itself write a template, and often does.
        if mine.startswith('{') and mine.endswith('}'):
            continue

        if mine.lower() != theirs.lower():
            return False

    return True


def markdown_files():
    for directory, subdirectories, files in os.walk(ROOT):
        subdirectories[:] = [d for d in subdirectories if d not in SKIP_DIRECTORIES]

        for name in files:
            if name.endswith('.md'):
                yield os.path.join(directory, name)


def unknown_citations_in(path, routes):
    with open(path, encoding='utf-8') as handle:
        text = handle.read()

    for line_number, line in enumerate(text.splitlines(), start=1):
        for method, cited in CITATION.findall(line):
            method = method.upper()

            # A query string is the caller's business, not the route's.
            cited = cited.split('?', 1)[0].rstrip('/.,;:')

            if (method, cited) in ALLOWED_ABSENT:
                continue

            if not any(method == published_method and matches(cited, published_path)
                       for published_method, published_path in routes):
                yield line_number, method, cited


def main():
    if not os.path.exists(CONTRACT):
        print(f'{CONTRACT} was not found. Run scripts/regenerate-contract.sh first.',
              file=sys.stderr)
        return 1

    routes = contract_routes()

    if not routes:
        print('The contract published no routes, so nothing could be checked.',
              file=sys.stderr)
        return 1

    failures = 0
    checked = 0

    for path in sorted(markdown_files()):
        relative = os.path.relpath(path, ROOT).replace('\\', '/')

        with open(path, encoding='utf-8') as handle:
            checked += len(CITATION.findall(handle.read()))

        for line_number, method, cited in unknown_citations_in(path, routes):
            print(f'  UNKNOWN  {relative}:{line_number}  {method} {cited}')
            failures += 1

    if checked == 0:
        # A check that matched nothing is a check that has stopped working.
        # Every version of this repository has documented endpoints.
        print('No endpoint citations were found at all, so the scan has broken.',
              file=sys.stderr)
        return 1

    if failures == 0:
        print(f'All {checked} documented endpoints exist in the contract.')
        return 0

    print(f'\n{failures} documented endpoint(s) do not exist. Either the document is '
          f'stale or the endpoint was renamed; if it names something deliberately '
          f'absent, add it to ALLOWED_ABSENT with the reason.', file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main())
