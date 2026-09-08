import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import packageJson from '../package.json';

/**
 * The design rules of ARCHITECTURE.md §9.5, checked rather than trusted.
 *
 * Every one of these is an acceptance criterion of Phase 7, and every one of
 * them is the kind that erodes quietly. Nobody decides to add an icon package;
 * somebody installs one to solve a single problem at five o'clock, and a year
 * later every button has a glyph. A test is what makes that a decision again.
 */

const sourceRoot = join(import.meta.dirname);

/**
 * The file's code, with comments removed.
 *
 * These checks match text, and the first run of them flagged the very comments
 * that explain the rules — `session.ts` saying "never in sessionStorage" and
 * `globals.css` saying "no heavy gradient". A guard that reads prose reports the
 * documentation as a violation, which trains people to delete the explanation
 * rather than obey it.
 */
function codeOf(path: string): string {
  return readFileSync(path, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/^\s*\/\/.*$/gm, '');
}

/** Every class name written in a `className` attribute in this file. */
function classTokens(code: string): string[] {
  return [...code.matchAll(/className=(?:"([^"]*)"|'([^']*)'|\{`([^`]*)`\})/g)]
    .flatMap((match) => (match[1] ?? match[2] ?? match[3] ?? '').split(/\s+/))
    .filter(Boolean);
}

/**
 * Whether a utility takes a side rather than a direction.
 *
 * Checked as a whole token with an exact prefix, so `ms-4` and `me-2` pass while
 * `ml-4` and `mr-2` do not — and `min-w-44`, which merely starts with the same
 * letters, is not mistaken for one.
 */
function isPhysicalDirection(token: string): boolean {
  const bare = token.replace(/^-/, '').replace(/^[a-z-]+:/, '');

  return /^(?:ml|mr|pl|pr|left|right|border-l|border-r|rounded-l|rounded-r)-/.test(bare)
    || bare === 'text-left'
    || bare === 'text-right';
}

function sourceFiles(): string[] {
  const found: string[] = [];

  function walk(directory: string) {
    for (const entry of readdirSync(directory)) {
      const path = join(directory, entry);

      if (statSync(path).isDirectory()) {
        walk(path);
      } else if (/\.(tsx|ts|css)$/.test(entry) && !entry.endsWith('.test.ts')) {
        found.push(path);
      }
    }
  }

  walk(sourceRoot);

  return found;
}

describe('no icons by default', () => {
  it('installs no icon package at all', () => {
    const dependencies = {
      ...packageJson.dependencies,
      ...packageJson.devDependencies,
    };

    // Absent, not merely unused. Reaching for a decorative glyph should mean
    // adding a dependency and explaining why — which is a conversation, and the
    // conversation is the control.
    const iconPackages = Object.keys(dependencies).filter((name) =>
      /icon|lucide|heroicons|feather|phosphor|font-awesome/i.test(name),
    );

    expect(iconPackages).toEqual([]);
  });
});

describe('layout mirrors for Arabic', () => {
  it('uses no physical direction utilities', () => {
    // `ml-4` survives the mirror and lands on the wrong side in Arabic; `ms-4`
    // follows the direction. This failure is quiet — the page still renders, it
    // is just wrong for half the company — which is exactly why it is checked
    // mechanically rather than left to review.
    //
    // Tokenised rather than matched with one regex. The first version required
    // whitespace before the class and therefore missed `className="ml-4 ..."`,
    // where the offending utility is the very first thing in the attribute —
    // a guard that passed while the violation sat in the file.
    const offenders = sourceFiles().filter((file) =>
      classTokens(codeOf(file)).some(isPhysicalDirection),
    );

    expect(offenders).toEqual([]);
  });
});

describe('white and blue only', () => {
  it('defines no third hue in the token set', () => {
    const css = codeOf(join(sourceRoot, 'styles', 'globals.css'));

    // Everything permitted: white, the blue ramp, neutrals, and the three
    // status colours that always appear with a word beside them (§9.8).
    const forbidden = /--color-(?:purple|pink|orange|teal|indigo|violet|fuchsia|lime|emerald|cyan)/;

    expect(forbidden.test(css)).toBe(false);
  });

  it('uses no gradients, glow or glassmorphism', () => {
    // Named in §9.5 as the "AI look" and prohibited outright. Checked because
    // each is one utility class away at any moment.
    const forbidden = /gradient|backdrop-blur|drop-shadow-\[|shadow-\[0_0|blur-3xl/;

    const offenders = sourceFiles().filter((file) => forbidden.test(codeOf(file)));

    expect(offenders).toEqual([]);
  });
});

describe('no token reaches the browser', () => {
  it('never touches localStorage or sessionStorage', () => {
    // The reason the BFF exists. Any token a script can read is a token an XSS
    // on any page can steal, and storage JavaScript cannot reach is the only
    // mitigation that does not depend on never having such a bug.
    const forbidden = /localStorage|sessionStorage/;

    const offenders = sourceFiles().filter((file) => forbidden.test(codeOf(file)));

    expect(offenders).toEqual([]);
  });

  it('keeps the Platform client server-only', () => {
    const client = readFileSync(join(sourceRoot, 'lib', 'platform-client.ts'), 'utf8');
    const session = readFileSync(join(sourceRoot, 'lib', 'session.ts'), 'utf8');

    // `server-only` turns a mistaken client import into a build error rather
    // than a token in a browser bundle.
    expect(client).toContain("import 'server-only'");
    expect(session).toContain("import 'server-only'");
  });
});
