import { expect, test } from '@playwright/test';

/**
 * The Platform as an installed application.
 *
 * Every part of this only exists in a production build served from a real
 * origin: the worker is not registered in development, the manifest is
 * rendered by a route handler, and the icons and the offline page are files on
 * disk that the runtime image has to have been given. The last of those is why
 * this suite matters more than it looks — `public/` was named in the
 * Dockerfile's comments and never copied into the image, so all of it would
 * have worked here, in `next start`, and in every developer's browser, and
 * none of it in production.
 */

const locale = (project: string) => (project === 'ar' ? 'ar' : 'en');

test.describe('installing the Platform', () => {
  test('serves a manifest in the language of the pages it was linked from', async ({
    page,
  }, testInfo) => {
    const current = locale(testInfo.project.name);

    const response = await page.request.get(`/${current}/manifest.webmanifest`);

    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('application/manifest+json');

    const manifest = (await response.json()) as Record<string, unknown>;

    // The name on a home screen cannot be translated afterwards, so it is
    // chosen by where the person installed from.
    expect(manifest['lang']).toBe(current);
    expect(manifest['dir']).toBe(current === 'ar' ? 'rtl' : 'ltr');
    expect(manifest['start_url']).toBe(`/${current}/dashboard`);

    // One application in both languages: installing from the other set of
    // pages updates this one rather than leaving a second copy on the device.
    expect(manifest['id']).toBe('/');

    expect(manifest['display']).toBe('standalone');
    expect(manifest['name']).toBeTruthy();
    expect(manifest['short_name']).toBeTruthy();

    // A browser offers to install only when it has a large icon and a
    // maskable one; without the second, Android pads the square into a
    // smaller square on a white disc.
    const icons = manifest['icons'] as { src: string; sizes: string; purpose: string }[];

    expect(icons.some((icon) => icon.sizes === '512x512' && icon.purpose === 'any')).toBe(true);
    expect(icons.some((icon) => icon.purpose === 'maskable')).toBe(true);
  });

  test('links that manifest from the page, and the files it names exist', async ({
    page,
  }, testInfo) => {
    const current = locale(testInfo.project.name);

    await page.goto(`/${current}/dashboard`);

    const href = await page.locator('link[rel="manifest"]').getAttribute('href');

    expect(href).toBe(`/${current}/manifest.webmanifest`);

    // The colour the system paints around the application, which should be the
    // colour of the bar at the top of it.
    await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute(
      'content',
      '#13304d',
    );

    // Every file the install depends on, fetched. These are served from disk
    // rather than rendered, which is exactly the class of thing that is
    // present in development and missing from a deployment.
    for (const path of [
      '/sw.js',
      '/offline.html',
      '/icons/icon-192.png',
      '/icons/icon-512.png',
      '/icons/icon-maskable-512.png',
      '/icons/apple-touch-icon.png',
    ]) {
      const file = await page.request.get(path);

      expect(file.status(), path).toBe(200);
    }
  });

  test('registers a worker that takes control of the page', async ({ page }, testInfo) => {
    const current = locale(testInfo.project.name);

    await page.goto(`/${current}/dashboard`);

    // Registration is deliberately deferred to the load event so it never
    // competes with the screen, so this waits rather than reads.
    const script = await page.evaluate(async () => {
      const registration = await navigator.serviceWorker.ready;

      return registration.active?.scriptURL ?? null;
    });

    expect(script).toContain('/sw.js');
  });
});
