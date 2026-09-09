import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests, run in **both languages**.
 *
 * Not a nicety. The Arabic interface is not a translation of the English one —
 * the entire layout mirrors — and a suite that only exercised one direction
 * would leave half the company's experience untested. Every spec therefore runs
 * twice, once per locale, and the locale is a project rather than a parameter so
 * a failure names the language it happened in.
 *
 * These need a running API and a database, so like the integration suite they
 * run in CI rather than on a developer's machine. That is a stated limitation,
 * not a preference.
 */
export default defineConfig({
  testDir: './e2e',

  // Signs in once and stores the cookie. The account is created with
  // MustChangePassword, so the first sign-in must complete that before any
  // session is usable — and the specs share one account, so repeated sign-ins
  // would trip its own progressive lockout.
  globalSetup: './e2e/global-setup.ts',

  // A failing E2E test is usually a real failure, and a retry that hides it is
  // worse than a slow suite. One retry only, for genuine flake in CI.
  retries: process.env.CI ? 1 : 0,

  // Serial in CI. The tests sign in as the same administrator, and parallel
  // sign-ins would trip the account's own progressive lockout — the Platform
  // defending itself against its own test suite.
  workers: 1,

  reporter: process.env.CI ? [['github'], ['list']] : [['list']],

  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'ar',
      use: {
        ...devices['Desktop Chrome'],
        locale: 'ar',
        storageState: 'e2e/.auth/admin.json',
      },
    },
    {
      name: 'en',
      use: {
        ...devices['Desktop Chrome'],
        locale: 'en',
        storageState: 'e2e/.auth/admin.json',
      },
    },
  ],
});
