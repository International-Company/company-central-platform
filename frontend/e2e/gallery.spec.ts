import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { test } from '@playwright/test';

// `process.cwd()`, not the module's own directory: Playwright compiles these
// specs to CommonJS, and a single `import.meta` reference makes its file ESM and
// breaks the whole run before one test starts. Playwright runs from the frontend
// directory, which is where the gallery belongs anyway.

/**
 * A picture of every screen, in both languages, from the real application.
 *
 * Not an assertion. A design is judged by looking at it, and the portal cannot
 * be looked at on a machine without a database and a signed-in administrator —
 * which is every developer machine this project has had. CI has both, so CI
 * takes the pictures and uploads them, and a change to the design can be
 * compared with what it replaced rather than imagined.
 *
 * Written outside `test-results/`, which Playwright empties at the start of
 * every run.
 */

const screens = [
  'dashboard',
  'tasks',
  'notifications',
  'users',
  'employees',
  'organization',
  'workflow',
  'documents',
  'roles',
  'applications',
  'integrations',
  'configuration',
  'security',
  'operations',
  'audit',
] as const;

const directory = join(process.cwd(), 'e2e-gallery');

test.describe('gallery', () => {
  test.beforeAll(() => {
    mkdirSync(directory, { recursive: true });
  });

  for (const screen of screens) {
    test(`pictures ${screen}`, async ({ page }, testInfo) => {
      const locale = testInfo.project.name === 'ar' ? 'ar' : 'en';

      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(`/${locale}/${screen}`);
      await page.waitForLoadState('networkidle');

      await page.screenshot({
        path: join(directory, `${locale}-${screen}.png`),
        fullPage: true,
      });
    });
  }

  test('pictures the sign-in page', async ({ browser }, testInfo) => {
    const locale = testInfo.project.name === 'ar' ? 'ar' : 'en';

    // A fresh context with no session, so the page is the one a signed-out
    // person actually sees.
    const context = await browser.newContext({ locale });
    const page = await context.newPage();

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`/${locale}/login`);
    await page.waitForLoadState('networkidle');

    await page.screenshot({ path: join(directory, `${locale}-login.png`), fullPage: true });

    await context.close();
  });

  test('pictures a narrow screen', async ({ page }, testInfo) => {
    const locale = testInfo.project.name === 'ar' ? 'ar' : 'en';

    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/${locale}/users`);
    await page.waitForLoadState('networkidle');

    await page.screenshot({ path: join(directory, `${locale}-users-phone.png`), fullPage: true });
  });
});
