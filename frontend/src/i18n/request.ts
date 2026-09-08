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
    // Typed rather than left as `any`. An untyped catalogue import means a
    // missing key is a runtime surprise instead of a build error.
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
