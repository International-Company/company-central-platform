import { chromium, type FullConfig } from '@playwright/test';
import { mkdir } from 'node:fs/promises';

/**
 * Signs in once, for every test that follows.
 *
 * **This is the real sign-in coverage**, and it walks the path a first
 * administrator actually walks: the bootstrap account is created with
 * `MustChangePassword`, so signing in lands on the change-password screen and
 * the session is not usable until that is done. The first version of this suite
 * signed in and then looked for the navigation — which is not there, because the
 * person is still on an authentication screen. The test was wrong about the
 * product, not the other way round.
 *
 * Everything after this reuses the cookie. Not only for speed: the specs share
 * one account, and repeated sign-ins would trip its progressive lockout — the
 * Platform defending itself against its own test suite.
 */

export const AdminStatePath = 'e2e/.auth/admin.json';

/** What the account's password becomes once the handover credential is spent. */
export const AdminPassword = 'e2e-changed-not-a-real-secret-2026';

export default async function globalSetup(config: FullConfig) {
  const baseURL = config.projects[0]?.use.baseURL ?? 'http://localhost:3000';
  const username = process.env.E2E_USERNAME ?? 'e2e-admin';
  const initialPassword = process.env.E2E_PASSWORD ?? '';

  await mkdir('e2e/.auth', { recursive: true });

  const browser = await chromium.launch();
  const page = await browser.newPage({ baseURL });

  try {
    await page.goto('/en/login');

    await page.getByLabel('Username').fill(username);
    await page.getByLabel('Password', { exact: true }).fill(initialPassword);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await page.waitForURL(/\/en\/(dashboard|change-password)/, { timeout: 30_000 });

    // Only on the very first run against a fresh database. A re-run reuses the
    // account, whose password has already been changed.
    if (page.url().includes('change-password')) {
      await page.getByLabel('Current password').fill(initialPassword);
      await page.getByLabel('New password').fill(AdminPassword);
      await page.getByLabel('Confirm password').fill(AdminPassword);
      await page.getByRole('button', { name: 'Save' }).click();

      await page.waitForURL(/\/en\/dashboard/, { timeout: 30_000 });
    }

    await page.context().storageState({ path: AdminStatePath });
  } finally {
    await browser.close();
  }
}
