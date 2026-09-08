import type { ReactNode } from 'react';
import { getTranslations } from 'next-intl/server';

/**
 * The frame for sign-in, MFA and password recovery.
 *
 * A centred card on a quiet background. No illustration, no marketing panel,
 * no gradient — the person is here to get to work (ARCHITECTURE.md §9.5).
 */
export default async function AuthLayout({ children }: { children: ReactNode }) {
  const t = await getTranslations('app');

  return (
    <div className="flex min-h-dvh items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <h1 className="mb-6 text-center text-base font-semibold text-text">
          {t('name')}
        </h1>

        <div className="rounded-lg border border-border bg-surface p-6">
          {children}
        </div>
      </div>
    </div>
  );
}
