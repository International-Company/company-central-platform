import { chromium, type FullConfig, type Page } from '@playwright/test';
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
 *
 * **Failures here report what the server said.** A `waitForURL` timeout on its
 * own says "the page did not navigate" and nothing about why — which is how this
 * setup failed in CI run after run while the reason sat in a response body
 * nobody printed. Every failed call the page makes is recorded, and a failure
 * quotes them alongside whatever the screen put in front of the user.
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

  const failures: string[] = [];

  page.on('response', (response) => {
    if (response.status() < 400) {
      return;
    }

    void response
      .text()
      .catch(() => '')
      .then((body) => {
        failures.push(
          `${response.status()} ${response.request().method()} ` +
            `${new URL(response.url()).pathname} — ${body.slice(0, 400)}`,
        );
      });
  });

  try {
    // The label a person reads is "Password (Required)" — the Field component
    // says so in words rather than with a red asterisk, which is right for a
    // screen reader and means an exact-match locator never lands.
    await page.goto('/en/login');

    await page.getByLabel(/^Username/).fill(username);
    await page.getByLabel(/^Password/).fill(initialPassword);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await settle(page, failures, /\/en\/(dashboard|change-password)/, 'sign in');

    // Only on the very first run against a fresh database. A re-run reuses the
    // account, whose password has already been changed.
    if (page.url().includes('change-password')) {
      await page.getByLabel(/^Current password/).fill(initialPassword);
      await page.getByLabel(/^New password/).fill(AdminPassword);
      await page.getByLabel(/^Confirm password/).fill(AdminPassword);
      await page.getByRole('button', { name: 'Save' }).click();

      await settle(page, failures, /\/en\/dashboard/, 'change the password');
    }

    await page.context().storageState({ path: AdminStatePath });
  } finally {
    await browser.close();
  }
}

/**
 * Waits for the navigation a step should cause, and explains it if it does not.
 *
 * The screens report a failure in place rather than by navigating, so the
 * useful evidence is on the page and in the responses — not in the timeout.
 */
async function settle(
  page: Page,
  failures: readonly string[],
  expected: RegExp,
  step: string,
): Promise<void> {
  try {
    await page.waitForURL(expected, { timeout: 30_000 });
  } catch (cause) {
    // `role="alert"` is how every form here announces a failure, so it is also
    // the reliable place to read one from.
    const shown = await page
      .getByRole('alert')
      .allTextContents()
      .catch(() => []);

    throw new Error(
      [
        `Could not ${step}: the page stayed at ${page.url()}.`,
        shown.length > 0 ? `On screen: ${shown.join(' | ')}` : 'Nothing on screen.',
        failures.length > 0
          ? `Failed calls:\n  ${failures.join('\n  ')}`
          : 'No call failed, so the screen chose not to navigate.',
      ].join('\n'),
      { cause },
    );
  }
}
