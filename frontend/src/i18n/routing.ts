import { defineRouting } from 'next-intl/routing';
import { createNavigation } from 'next-intl/navigation';
import { defaultLocale, locales } from './config';

export const routing = defineRouting({
  locales,
  defaultLocale,

  // The locale is always in the URL, including for the default. A shared link
  // then carries the language it was read in, which matters in a bilingual
  // company: a colleague opening a teammate's link should see what they saw.
  localePrefix: 'always',
});

export const { Link, redirect, usePathname, useRouter, getPathname } =
  createNavigation(routing);
