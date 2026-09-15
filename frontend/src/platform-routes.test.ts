import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Every call the portal makes to the Platform names an endpoint that exists,
 * with the method it accepts.
 *
 * The generated types keep request and response shapes honest, but they do not
 * bind paths: `callPlatform` takes a string. A route renamed or re-verbed on the
 * backend would leave a button answering 404 or 405, with typecheck, lint and
 * every other test green — the regenerated contract would change and nothing
 * here would read it.
 *
 * When this was written all 110 calls matched. It exists so that stays a fact
 * rather than a memory.
 */

const sourceRoot = join(import.meta.dirname);
const contractPath = join(sourceRoot, '..', '..', 'contracts', 'platform-api.json');

type Route = { method: string; path: string };

function contractRoutes(): Route[] {
  const contract = JSON.parse(readFileSync(contractPath, 'utf8')) as {
    paths: Record<string, Record<string, unknown>>;
  };

  return Object.entries(contract.paths).flatMap(([path, operations]) =>
    Object.keys(operations)
      .filter((method) => ['get', 'post', 'put', 'patch', 'delete'].includes(method))
      .map((method) => ({ method: method.toUpperCase(), path })),
  );
}

function sourceFiles(directory: string, found: string[] = []): string[] {
  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      sourceFiles(path, found);
    } else if (/\.tsx?$/.test(entry) && !entry.endsWith('.test.ts')) {
      found.push(path);
    }
  }

  return found;
}

/**
 * The path a call names, with its parameters reduced to placeholders.
 *
 * A path may be written as one literal or as several joined with `+` — the
 * first version of this check read only the first piece of
 * `/users/${id}` + `/roles/${assignmentId}` and reported a call that was fine.
 */
function pathOf(expression: string): string {
  const pieces = [...expression.matchAll(/'([^']*)'|`([^`]*)`/g)].map(
    (piece) => piece[1] ?? piece[2] ?? '',
  );

  return (
    pieces
      .join('')
      // A trailing `${query}` directly after a segment carries the query
      // string, not a segment of its own.
      .replace(/([^/])\$\{[^}]*\}$/, '$1')
      .split('?', 1)
      .join('')
      .replace(/\$\{[^}]*\}/g, '{param}')
  );
}

function calls(): { method: string; path: string; file: string }[] {
  return sourceFiles(sourceRoot).flatMap((file) => {
    const code = readFileSync(file, 'utf8');

    return [...code.matchAll(/callPlatform\s*(?:<[^(]*>)?\s*\(\s*\{([\s\S]*?)\}\s*\)/g)].flatMap(
      (call) => {
        const body = call[1] ?? '';
        const path = /path:\s*((?:(?:'[^']*'|`[^`]*`)\s*\+?\s*)+)/.exec(body)?.[1];

        if (!path) {
          return [];
        }

        return [
          {
            method: /method:\s*'([A-Z]+)'/.exec(body)?.[1] ?? 'GET',
            path: pathOf(path),
            file: file.slice(sourceRoot.length + 1).replaceAll('\\', '/'),
          },
        ];
      },
    );
  });
}

function sameShape(cited: string, published: string): boolean {
  const mine = cited.split('/').filter(Boolean);
  const theirs = published.split('/').filter(Boolean);

  return (
    mine.length === theirs.length
    && mine.every(
      (segment, index) => {
        const other = theirs[index] ?? '';

        return segment.startsWith('{') || other.startsWith('{') || segment === other;
      },
    )
  );
}

describe('the portal calls the Platform', () => {
  const routes = contractRoutes();
  const found = calls();

  it('at endpoints that exist, with the method they accept', () => {
    const wrong = found
      .filter(
        (call) =>
          !routes.some((route) => route.method === call.method && sameShape(call.path, route.path)),
      )
      .map((call) => `${call.method} ${call.path}  (${call.file})`);

    expect(wrong).toEqual([]);
  });

  it('and the scan still sees the calls', () => {
    // "Nothing was wrong" is also what the assertion above says once the
    // pattern stops matching. There were 110 calls when this was written.
    expect(found.length).toBeGreaterThan(90);
    expect(routes.length).toBeGreaterThan(90);
  });
});
