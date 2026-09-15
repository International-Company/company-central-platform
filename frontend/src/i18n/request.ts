import { getRequestConfig } from 'next-intl/server';
import { hasLocale } from 'next-intl';
import { routing } from './routing';

export default getRequestConfig(async ({ requestLocale }) => {
  const requested = await requestLocale;
  const locale = hasLocale(routing.locales, requested)
    ? requested
    : routing.defaultLocale;

  return {
    locale,
    // Typed rather than left as `any`. On its own that did not make a missing
    // key a build error, though this comment said it did: tsc checked nothing
    // about keys, and six calls across six screens asked for messages that
    // did not exist, rendering "common.none" and "organization.activateRole"
    // to the people using them. The keys are checked because next-intl.d.ts
    // declares the catalogue's type to next-intl; this cast is not what does it.
    messages: (
      (await import(`./messages/${locale}.json`)) as {
        default: Record<string, unknown>;
      }
    ).default,

    // Fixed, not the browser's. A date rendered differently for two colleagues
    // reading the same audit record is a support call, and in an investigation
    // it is worse than that.
    timeZone: 'Asia/Riyadh',
  };
});
