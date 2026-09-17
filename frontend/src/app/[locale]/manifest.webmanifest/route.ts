import { getTranslations } from 'next-intl/server';
import { hasLocale } from 'next-intl';
import { direction } from '@/i18n/config';
import { routing } from '@/i18n/routing';

/**
 * The installed application's manifest, one per language.
 *
 * **Per locale, because the name on a home screen is not translatable later.**
 * A single static manifest would mean every employee in the company installing
 * an application called by its English name, or every one of them installing
 * it by its Arabic name, and whichever was chosen the other half would be
 * looking at a word they did not ask for. This is served from the page the
 * person is reading, so what they install is what they were reading.
 *
 * **One application, not two.** `id` is the same in both, so installing from
 * the English pages after installing from the Arabic ones updates the
 * application already there rather than leaving a second copy on the device.
 *
 * `start_url` is the dashboard rather than the root: the root only redirects,
 * and a launcher that begins with a redirect shows a blank frame while it
 * happens. Somebody without a session is sent to sign in from there, which is
 * the same thing that happens in a browser tab.
 */
export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ locale: string }> },
) {
  const { locale } = await params;

  if (!hasLocale(routing.locales, locale)) {
    return new Response(null, { status: 404 });
  }

  const t = await getTranslations({ locale, namespace: 'app' });
  const nav = await getTranslations({ locale, namespace: 'nav' });

  const manifest = {
    id: '/',
    name: t('name'),
    short_name: t('shortName'),
    description: t('description'),

    lang: locale,
    dir: direction[locale],

    scope: '/',
    start_url: `/${locale}/dashboard`,

    display: 'standalone',

    // No orientation lock. This is a screen full of tables that somebody will
    // want to turn sideways, and an application that refuses to is one they
    // stop using on a phone.
    background_color: '#ffffff',

    // The blue of the top bar, so the system chrome above the application is
    // the same colour as the application.
    theme_color: '#13304d',

    categories: ['business', 'productivity'],

    icons: [
      { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
      { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },

      // Cut to a circle or a squircle by the platform, so the mark is drawn
      // well inside the edges. Without one, Android pads the square icon into
      // a smaller square on a white disc.
      { src: '/icons/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],

    // Long-pressing the installed application offers these. Both are the
    // places somebody opens the Platform *to get to*, rather than to look
    // around in.
    shortcuts: [
      {
        name: nav('tasks'),
        url: `/${locale}/tasks`,
        icons: [{ src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' }],
      },
      {
        name: nav('notifications'),
        url: `/${locale}/notifications`,
        icons: [{ src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' }],
      },
    ],
  };

  return new Response(JSON.stringify(manifest, null, 2), {
    headers: {
      'Content-Type': 'application/manifest+json; charset=utf-8',

      // Revalidated every time. The manifest carries the application's name
      // and its start page, and a stale one is a home-screen icon that opens
      // the wrong thing long after the deployment that changed it.
      'Cache-Control': 'public, max-age=0, must-revalidate',
    },
  });
}
