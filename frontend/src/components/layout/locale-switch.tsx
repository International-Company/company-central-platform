'use client';

import { Link, usePathname } from '@/i18n/routing';
import { localeLabel, locales, type Locale } from '@/i18n/config';

/**
 * Switches language, keeping the reader where they are.
 *
 * The same path under the other locale — not a jump to the home page, which is
 * what a naive switcher does and which loses whatever the person was reading.
 *
 * **It also remembers.** The portal knows which language to show from the URL;
 * an email arrives with nobody present to have opened a URL, so a notification
 * is in the right language only if the choice was written down. Switching here
 * is the moment somebody expresses that choice, and the only one they would
 * think to look for.
 */
export function LocaleSwitch({
  current,
  label,
  tone,
  remember: shouldRemember = true,
}: {
  current: Locale;
  label: string;

  /** Which surface it sits on: the top bar, the drawer, or a white page. */
  tone: 'dark' | 'light';

  /**
   * Whether to write the choice down.
   *
   * Off before sign-in, where there is nobody to write it down for: the
   * request would be refused, and asking the Platform to remember a preference
   * for an anonymous visitor is a question with no answer.
   */
  remember?: boolean;
}) {
  const pathname = usePathname();

  /**
   * Records the choice without getting in the way of the navigation.
   *
   * `keepalive` exists for exactly this: the request outlives the page it was
   * started from, so the link behaves as a link and the preference still
   * arrives. Failures are ignored on purpose — being unable to store a
   * preference must not stop somebody changing the language they are reading
   * in, which is the thing they actually asked for.
   */
  function remember(locale: Locale) {
    void fetch('/api/me/language', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ locale }),
      keepalive: true,
    }).catch(() => undefined);
  }

  return (
    <nav aria-label={label} className="flex items-center gap-4">
      {locales.map((locale) => (
        <Link
          key={locale}
          href={pathname}
          locale={locale}
          hrefLang={locale}
          onClick={() => {
            if (shouldRemember) {
              remember(locale);
            }
          }}
          aria-current={locale === current ? 'true' : undefined}
          className={
            tone === 'dark'
              ? locale === current
                ? 'font-semibold text-text-on-primary'
                : 'text-primary-100 hover:text-text-on-primary hover:underline underline-offset-4'
              : locale === current
                ? 'font-semibold text-text'
                : 'text-primary-700 hover:text-primary-900 hover:underline underline-offset-4'
          }
        >
          {localeLabel[locale]}
        </Link>
      ))}
    </nav>
  );
}
