import { describe, expect, it } from 'vitest';
import ar from './messages/ar.json';
import en from './messages/en.json';

/**
 * The two catalogues must stay in step.
 *
 * A key present in one and missing from the other is a screen that renders its
 * own key name to half the company — visible immediately in testing and easy to
 * miss in review, because the reviewer reads only one language.
 */
function flatten(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) {
    return [prefix];
  }

  return Object.entries(value as Record<string, unknown>).flatMap(([key, child]) =>
    flatten(child, prefix ? `${prefix}.${key}` : key),
  );
}

describe('message catalogues', () => {
  const arabic = flatten(ar).sort();
  const english = flatten(en).sort();

  it('carry exactly the same keys', () => {
    expect(arabic).toEqual(english);
  });

  it('are not empty', () => {
    expect(english.length).toBeGreaterThan(50);
  });

  it('have no untranslated Arabic entries', () => {
    // A value identical in both files is usually a forgotten translation
    // rather than a word that happens to be the same. Proper nouns are the
    // real exception, and there are none in these catalogues yet.
    const shared = flatten(ar)
      .filter((key) => read(ar, key) === read(en, key))
      .filter((key) => typeof read(en, key) === 'string');

    expect(shared).toEqual([]);
  });
});

function read(source: unknown, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>(
      (value, key) =>
        typeof value === 'object' && value !== null
          ? (value as Record<string, unknown>)[key]
          : undefined,
      source,
    );
}
