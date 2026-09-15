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

    // Measured rather than listed by name. The earlier version forbade a list
    // of colour words and explicitly allowed green, amber and red for status,
    // so a token called --color-success could be any hue at all and pass.
    // Every colour is now converted to hue and saturation: it must be a neutral
    // (barely saturated) or a blue.
    const offenders = [...css.matchAll(/--color-([a-z0-9-]+):\s*(#[0-9a-f]{6})/gi)]
      .map((match) => ({ name: match[1] ?? '', hex: match[2] ?? '' }))
      .filter(({ hex }) => {
        const [r, g, b] = [1, 3, 5].map((at) => parseInt(hex.slice(at, at + 2), 16) / 255) as [
          number,
          number,
          number,
        ];
        const max = Math.max(r, g, b);
        const min = Math.min(r, g, b);
        const lightness = (max + min) / 2;
        const delta = max - min;

        if (delta === 0) {
          return false;
        }

        const saturation = delta / (1 - Math.abs(2 * lightness - 1));
        const hue =
          (max === r ? ((g - b) / delta) % 6 : max === g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60;
        const degrees = (hue + 360) % 360;

        const neutral = saturation < 0.2;
        const blue = degrees >= 195 && degrees <= 235;

        return !(neutral || blue);
      })
      .map(({ name, hex }) => `--color-${name}: ${hex}`);

    expect(offenders).toEqual([]);
  });

  it('uses no gradients, glow or glassmorphism', () => {
    // Named in §9.5 as the "AI look" and prohibited outright. Checked because
    // each is one utility class away at any moment.
    const forbidden = /gradient|backdrop-blur|drop-shadow-\[|shadow-\[0_0|blur-3xl/;

    const offenders = sourceFiles().filter((file) => forbidden.test(codeOf(file)));

    expect(offenders).toEqual([]);
  });
});

describe('the design tokens actually reach the page', () => {
  it('uses the utilities Tailwind generates, not arbitrary variable syntax', () => {
    // Every custom colour was missing from the deployed site, and the failure
    // was silent: `bg-[--color-primary-600]` produces no rule in Tailwind v4,
    // so the class was emitted into the HTML and styled nothing. Standard
    // utilities like `flex` and `gap-4` still worked, which made the page look
    // merely unfinished rather than broken.
    //
    // `@theme { --color-primary-600: ... }` generates `bg-primary-600`. That is
    // the form to use, and this is what notices if the other one comes back.
    const arbitraryToken = /\[--(?:color|radius|shadow)-[a-z0-9-]+\]/;

    const offenders = sourceFiles().filter((file) =>
      arbitraryToken.test(codeOf(file)),
    );

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

describe('rendering never refreshes the session', () => {
  it('passes duringRender from every page and layout that calls the Platform', () => {
    // Refreshing rotates the token: the old one is spent the instant the
    // Platform answers, and the new pair must be written to the cookie or the
    // session is gone. Next.js forbids writing a cookie during render, so a
    // refresh started from a page or layout consumes the refresh token and then
    // throws — signing the person out for loading a page.
    //
    // Route handlers are exempt: writing a cookie is exactly what they are
    // allowed to do, and refreshing there is the whole point.
    const renderFiles = sourceFiles().filter(
      (file) =>
        /\.tsx$/.test(file)
        && !file.includes(join('app', 'api'))
        && codeOf(file).includes('callPlatform'),
    );

    const offenders = renderFiles.filter((file) => {
      const code = codeOf(file);

      // One `duringRender: true` per call. Counting rather than merely looking
      // for the phrase, because a page that adds a fifth call and forgets it is
      // exactly how this returns.
      const calls = code.match(/callPlatform\s*</g)?.length ?? 0;
      const guards = code.match(/duringRender:\s*true/g)?.length ?? 0;

      return guards < calls;
    });

    expect(offenders).toEqual([]);
  });
});

describe('text is readable', () => {
  /** WCAG relative luminance, from the sRGB definition. */
  function luminance(hex: string): number {
    const channels = [1, 3, 5].map((at) => parseInt(hex.slice(at, at + 2), 16) / 255);

    const linear = channels.map((channel) =>
      channel <= 0.04045
        ? channel / 12.92
        : ((channel + 0.055) / 1.055) ** 2.4,
    );

    return (
      0.2126 * (linear[0] ?? 0) +
      0.7152 * (linear[1] ?? 0) +
      0.0722 * (linear[2] ?? 0)
    );
  }

  function contrast(a: string, b: string): number {
    const first = luminance(a);
    const second = luminance(b);

    return (
      (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05)
    );
  }

  /** Every `--color-x: #hex` in the token set. */
  function tokens(): Map<string, string> {
    const css = readFileSync(join(sourceRoot, 'styles', 'globals.css'), 'utf8');

    return new Map(
      [...css.matchAll(/--color-([a-z0-9-]+):\s*(#[0-9a-f]{6})/gi)].map(
        (match) => [match[1] ?? '', match[2] ?? ''],
      ),
    );
  }

  it('meets WCAG AA against both surfaces', () => {
    // `--color-text-muted` was #7b8794 — 3.66:1 on white, comfortably under
    // the 4.5:1 AA needs, and it was the colour of the word "(Required)" beside
    // every field label. The design says the rule in words rather than with a
    // red asterisk precisely so it can be read; setting it too faint to read
    // undid the decision it was serving.
    //
    // Computed rather than eyeballed, because "it looks fine" is exactly the
    // judgement that produced the failing value.
    const palette = tokens();
    const surfaces = ['surface', 'surface-sunken'] as const;

    const failures: string[] = [];

    for (const [name, value] of palette) {
      if (!name.startsWith('text') || name === 'text-on-primary') {
        continue;
      }

      for (const surfaceName of surfaces) {
        const surface = palette.get(surfaceName);

        if (!surface) {
          continue;
        }

        const ratio = contrast(value, surface);

        if (ratio < 4.5) {
          failures.push(
            `${name} ${value} on ${surfaceName} ${surface}: ${ratio.toFixed(2)}:1`,
          );
        }
      }
    }

    expect(failures).toEqual([]);
  });
});

describe('no symbols stand in for words', () => {
  // Dashes, dots, arrows, triangles, checks, crosses, bullets, ellipses and
  // guillemets. Each one was in the portal: an em dash in every empty table
  // cell and between clauses in the catalogue, a middle dot between two facts,
  // an arrow joining workflow steps, a triangle beside a sorted column. Each
  // asks the reader to decode a convention the words would have stated.
  const symbols = /[\u2013\u2014\u2022\u00b7\u2026\u00d7\u00ab\u00bb\u2190-\u21ff\u25a0-\u25ff\u2713-\u2718]/u;

  it('uses none in the code people see', () => {
    // Comments are stripped by codeOf, so an explanation may still use an
    // em dash; what renders may not.
    const offenders = sourceFiles()
      .filter((file) => !file.endsWith('.css') && !file.endsWith('platform-api.ts'))
      .filter((file) => symbols.test(codeOf(file)));

    expect(offenders).toEqual([]);
  });

  it('uses none in either message catalogue', () => {
    const offenders: string[] = [];

    for (const [language, catalogue] of [['ar', readFileSync(join(sourceRoot, 'i18n', 'messages', 'ar.json'), 'utf8')], ['en', readFileSync(join(sourceRoot, 'i18n', 'messages', 'en.json'), 'utf8')]] as const) {
      (function walk(node: unknown, key: string) {
        if (typeof node === 'string') {
          if (symbols.test(node)) {
            offenders.push(`${language}:${key}`);
          }
        } else if (node && typeof node === 'object') {
          for (const [child, value] of Object.entries(node)) {
            walk(value, key ? `${key}.${child}` : child);
          }
        }
      })(JSON.parse(catalogue), '');
    }

    expect(offenders).toEqual([]);
  });
});

describe('corners stay square', () => {
  it('uses no large or pill-shaped radius', () => {
    // Heavily rounded corners on every surface are the most recognisable
    // trait of a generated interface. The token radii are two and three
    // pixels; nothing reaches past them.
    const forbidden = /\brounded-(?:xl|2xl|3xl|full)\b/;

    const offenders = sourceFiles().filter((file) => forbidden.test(codeOf(file)));

    expect(offenders).toEqual([]);
  });
});
