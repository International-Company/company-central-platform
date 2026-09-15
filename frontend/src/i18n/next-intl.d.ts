import type en from './messages/en.json';
import type { routing } from './routing';

/**
 * Tells next-intl what the message catalogue contains, so a key that does not
 * exist is a type error rather than a key name rendered on screen.
 *
 * **Without this, tsc checked nothing about keys.** Six calls across six
 * screens asked for messages that were never written, and every build passed:
 * wherever a value was empty the configuration, integrations and operations
 * screens printed "common.none", and the button that reactivates a position
 * read "organization.activateRole". The request configuration's comment claimed
 * a missing key was already a build error. Declaring the type is what makes
 * that true — declaring it found exactly those six and nothing else.
 *
 * English is the reference shape; the catalogue test keeps Arabic identical to
 * it, so checking against one checks both.
 */
declare module 'next-intl' {
  interface AppConfig {
    Locale: (typeof routing.locales)[number];
    Messages: typeof en;
  }
}
