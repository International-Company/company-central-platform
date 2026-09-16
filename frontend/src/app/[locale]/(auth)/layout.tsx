import type { ReactNode } from 'react';
import { getLocale, getTranslations } from 'next-intl/server';
import { LocaleSwitch } from '@/components/layout/locale-switch';
import { isLocale } from '@/i18n/config';

/**
 * The frame for sign-in, MFA and password recovery.
 *
 * **Two plain halves, not a card.** This was a small card centred on a grey
 * page, which is the single most recognisable layout of a generated login
 * screen. The start half says which system this is, in the deepest blue and in
 * words; the end half is the form on white. Still no illustration, no marketing
 * copy and no gradient — the person is here to get to work (ARCHITECTURE.md
 * §9.5), and one sentence of what the system is for is the most a sign-in page
 * should ever say.
 *
 * **The language can be changed here.** It could not be, anywhere in the
 * sign-in area: the switch lived in the application shell, which is behind
 * sign-in. Somebody sent a link to the Arabic sign-in page who reads English
 * had no way out of it, and the one page in the Platform that everybody
 * reaches was the one page with no way to change the language it was in.
 *
 * On a narrow screen the panel becomes a band across the top.
 */
export default async function AuthLayout({ children }: { children: ReactNode }) {
  const t = await getTranslations('app');
  const tCommon = await getTranslations('common');
  const locale = await getLocale();

  return (
    <div className="flex min-h-dvh flex-col bg-surface lg:flex-row">
      <section className="flex flex-col justify-center bg-primary-900 px-6 py-6 text-text-on-primary lg:w-[42%] lg:px-16 lg:py-12">
        <h1 className="text-lg font-semibold tracking-tight lg:text-3xl lg:leading-snug">
          {t('name')}
        </h1>

        <p className="mt-5 hidden max-w-md text-base leading-relaxed text-primary-100 lg:block">
          {t('description')}
        </p>
      </section>

      <div className="flex flex-1 flex-col">
        {/* Quiet, at the end of the top edge, where a person looks for it and
            where it is out of the way of the form. */}
        <div className="flex justify-end px-6 py-4 text-sm lg:px-12" data-print-hidden>
          <LocaleSwitch
            current={isLocale(locale) ? locale : 'ar'}
            label={tCommon('language')}
            tone="light"
            // Nobody is signed in yet, so there is nobody to remember it for.
            remember={false}
          />
        </div>

        <main className="flex flex-1 items-start justify-center px-6 pb-12 lg:items-center lg:px-12 lg:pb-20">
          <div className="w-full max-w-[26rem]">{children}</div>
        </main>
      </div>
    </div>
  );
}
