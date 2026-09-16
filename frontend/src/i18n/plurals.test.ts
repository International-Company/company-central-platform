import { createTranslator } from 'next-intl';
import { describe, expect, it } from 'vitest';
import ar from './messages/ar.json';
import en from './messages/en.json';

/**
 * Every message that counts something, formatted for real numbers.
 *
 * **Arabic has six plural categories** and English has two. "3 إشعارات" and
 * "11 إشعارًا" are different words, so a message that drops a number into one
 * fixed phrase is wrong for most numbers, and the way to be right is an ICU
 * plural with every category the language actually uses.
 *
 * ICU is a small language of its own, written by hand, inside a JSON string.
 * A missing brace throws where the message is rendered, which is a screen going
 * blank on the day a condition first comes true. This formats each of them
 * instead, at the numbers that select each category, so the failure happens
 * here.
 */

/** One number from each Arabic plural category, and a few besides. */
const numbers = [0, 1, 2, 3, 7, 10, 11, 25, 99, 100, 101];

const catalogues = [
  ['ar', ar],
  ['en', en],
] as const;

/** Every string in the catalogue, by its dotted path. */
function messages(node: unknown, prefix = ''): [string, string][] {
  if (typeof node === 'string') {
    return [[prefix, node]];
  }

  if (!node || typeof node !== 'object') {
    return [];
  }

  return Object.entries(node as Record<string, unknown>).flatMap(([key, child]) =>
    messages(child, prefix ? `${prefix}.${key}` : key),
  );
}

/** The names a message expects, where a name can be: before a comma or brace. */
function placeholders(message: string): string[] {
  return [...message.matchAll(/\{(\w+)\s*[,}]/g)].map((match) => match[1] ?? '');
}

describe('every message formats', () => {
  for (const [locale, catalogue] of catalogues) {
    it(`in ${locale}, at every number`, () => {
      const translate = createTranslator({ locale, messages: catalogue });
      const failures: string[] = [];

      for (const [path, message] of messages(catalogue)) {
        const names = placeholders(message);

        for (const value of numbers) {
          const values = Object.fromEntries(
            // Dates and names are given a number too. It is never rendered
            // wrongly by that, and it keeps this from needing to know which
            // placeholder means what.
            names.map((name) => [name, value]),
          );

          let rendered: string;

          try {
            rendered = translate(path as never, values as never);
          } catch (error) {
            failures.push(`${path} at ${value}: ${String(error)}`);

            continue;
          }

          if (rendered.length === 0) {
            failures.push(`${path} at ${value}: rendered nothing`);
          }

          // next-intl returns the key itself when the message cannot be
          // parsed, rather than throwing, so a broken message is otherwise
          // invisible until somebody reads the screen.
          if (rendered === path) {
            failures.push(`${path} at ${value}: rendered its own key`);
          }

          if (/[{}]/.test(rendered)) {
            failures.push(`${path} at ${value}: left a brace in "${rendered}"`);
          }
        }
      }

      expect(failures).toEqual([]);
    });
  }
});

describe('the counted messages', () => {
  // Written out rather than discovered, so deleting the plural from one of them
  // fails here instead of quietly reducing the check to nothing.
  const counted = [
    'dashboard.attention.deadLettered',
    'dashboard.attention.outboxBehind',
    'dashboard.attention.failingJobs',
    'dashboard.attention.suspendedSubscriptions',
    'dashboard.attention.securityEvents',
    'dashboard.attention.unreadNotifications',
  ];

  it('say a different thing for one and for many, in both languages', () => {
    for (const [locale, catalogue] of catalogues) {
      const translate = createTranslator({ locale, messages: catalogue });

      for (const path of counted) {
        const names = placeholders(
          messages(catalogue).find(([key]) => key === path)?.[1] ?? '',
        );

        const at = (value: number) =>
          translate(
            path as never,
            Object.fromEntries(names.map((name) => [name, value])) as never,
          );

        // Arabic distinguishes three of these; English distinguishes the first
        // from the rest. Either way one and eleven may not be the same string.
        expect(at(1), `${locale} ${path}`).not.toEqual(at(11));
      }
    }
  });
});
