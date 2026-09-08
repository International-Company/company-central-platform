/**
 * Locales and direction.
 *
 * Arabic is listed first and is the default. The Platform is for a company that
 * works in Arabic; English is the second language, not the assumed one. That
 * ordering is a decision, not an accident of the alphabet.
 */
export const locales = ['ar', 'en'] as const;

export type Locale = (typeof locales)[number];

export const defaultLocale: Locale = 'ar';

/**
 * Text direction per locale.
 *
 * This drives `dir` on `<html>`, which is what makes the *whole layout* mirror
 * — not just the text. Combined with logical CSS properties (`ms-*`, `me-*`,
 * `start-*`, `end-*`) it means a single stylesheet serves both directions.
 * Physical `left`/`right` utilities are prohibited and linted against for
 * exactly this reason: they survive the mirror and end up on the wrong side.
 */
export const direction: Record<Locale, 'rtl' | 'ltr'> = {
  ar: 'rtl',
  en: 'ltr',
};

/** The language's own name, in that language. Never translated. */
export const localeLabel: Record<Locale, string> = {
  ar: 'العربية',
  en: 'English',
};

export function isLocale(value: string): value is Locale {
  return (locales as readonly string[]).includes(value);
}
