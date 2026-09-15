import type { ReactNode } from 'react';
import { getTranslations } from 'next-intl/server';

/**
 * The frame for sign-in, MFA and password recovery.
 *
 * **Two plain halves, not a card.** This was a small card centred on a grey
 * page, which is the single most recognisable layout of a generated login
 * screen. The start half now says which system this is, in the deepest blue
 * and in words; the end half is the form on white. Still no illustration, no
 * marketing copy and no gradient — the person is here to get to work
 * (ARCHITECTURE.md §9.5), and one sentence of what the system is for is the
 * most a sign-in page should ever say.
 *
 * On a narrow screen the panel becomes a band across the top.
 */
export default async function AuthLayout({ children }: { children: ReactNode }) {
  const t = await getTranslations('app');

  return (
    <div className="flex min-h-dvh flex-col bg-surface lg:flex-row">
      <section className="bg-primary-900 px-6 py-5 text-text-on-primary lg:flex lg:w-[40%] lg:flex-col lg:justify-center lg:px-16">
        <h1 className="text-lg font-semibold lg:text-3xl lg:leading-snug">{t('name')}</h1>

        <p className="mt-4 hidden max-w-md text-base leading-relaxed text-primary-100 lg:block">
          {t('description')}
        </p>
      </section>

      <main className="flex flex-1 items-start justify-center px-6 py-10 lg:items-center lg:px-16">
        <div className="w-full max-w-sm">{children}</div>
      </main>
    </div>
  );
}
