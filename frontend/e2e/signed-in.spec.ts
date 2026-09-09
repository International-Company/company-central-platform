import { expect, test, type Page } from '@playwright/test';

/**
 * Sign in, then open every screen.
 *
 * **This is the test that was missing.** Four defects reached the deployed
 * application in one day, and every one of them would have failed here:
 *
 *   - the portal pages were prerendered, so a successful sign-in bounced
 *     straight back to the login page;
 *   - no custom colour rendered at all, because the class syntax produced no
 *     CSS rule;
 *   - the employees route called a path that does not exist;
 *   - concurrent requests each refreshed the token, and the Platform's reuse
 *     detection revoked the session — correctly.
 *
 * Unit tests passed throughout. None of these lives in a unit.
 */

const locale = (project: string) => (project === 'ar' ? 'ar' : 'en');

async function signIn(page: Page, project: string) {
  const username = process.env.E2E_USERNAME ?? 'e2e-admin';
  const password = process.env.E2E_PASSWORD ?? '';

  await page.goto(`/${locale(project)}/login`);

  // By label, not by CSS selector or test id. A label a person can read is a
  // label a screen reader announces, so a test that cannot find the field by
  // its label has found an accessibility problem — which is worth failing over.
  await page.getByLabel(/اسم المستخدم|Username/).fill(username);
  await page.getByLabel(/كلمة المرور|Password/).first().fill(password);

  await page.getByRole('button', { name: /تسجيل الدخول|Sign in/ }).click();

  await page.waitForURL(new RegExp(`/${locale(project)}/(dashboard|change-password)`), {
    timeout: 15_000,
  });
}

test.describe('signed in', () => {
  test('reaches the dashboard and stays there', async ({ page }, testInfo) => {
    await signIn(page, testInfo.project.name);

    // The regression that mattered most: sign-in succeeded and the prerendered
    // redirect sent the person back to the login page, forever.
    await expect(page).not.toHaveURL(/\/login/);
  });

  test('mirrors the layout for the locale', async ({ page }, testInfo) => {
    const expected = testInfo.project.name === 'ar' ? 'rtl' : 'ltr';

    await signIn(page, testInfo.project.name);

    // `dir` on the root is what mirrors the whole layout rather than only the
    // text. If this is wrong, every logical property in the stylesheet points
    // the wrong way.
    await expect(page.locator('html')).toHaveAttribute('dir', expected);
    await expect(page.locator('html')).toHaveAttribute(
      'lang',
      testInfo.project.name === 'ar' ? 'ar' : 'en',
    );
  });

  test('renders the design tokens', async ({ page }, testInfo) => {
    await signIn(page, testInfo.project.name);

    const sidebar = page.getByRole('navigation', {
      name: /التنقل الرئيسي|Main navigation/,
    });

    // Every custom colour was missing from the deployed site and nothing
    // noticed: the classes reached the HTML and styled nothing, so the page
    // looked unfinished rather than broken. A computed colour is the only
    // honest check — a class name in the markup proves only that it was
    // written, not that it did anything.
    const background = await sidebar.evaluate(
      (element) => getComputedStyle(element).backgroundColor,
    );

    expect(background).not.toBe('rgba(0, 0, 0, 0)');
  });

  for (const screen of ['users', 'employees', 'roles', 'audit'] as const) {
    test(`opens ${screen} without an error`, async ({ page }, testInfo) => {
      await signIn(page, testInfo.project.name);

      await page.goto(`/${locale(testInfo.project.name)}/${screen}`);

      // The generic failure banner. Its presence means the screen called the
      // Platform and the Platform said no — a wrong path, a dead session, a
      // missing permission. The employees screen shipped calling a path that
      // did not exist, and nothing but opening it would have said so.
      const failure = page.getByText(/حدث خطأ|Something went wrong/);

      await expect(failure).toHaveCount(0);

      // And the page is the one that was asked for, not a redirect to sign-in.
      await expect(page).toHaveURL(new RegExp(`/${screen}$`));
    });
  }

  test('survives several screens in a row', async ({ page }, testInfo) => {
    const current = locale(testInfo.project.name);

    await signIn(page, testInfo.project.name);

    // Navigating quickly is what set off the refresh stampede: several requests
    // in flight, all seeing an expired access token, all refreshing, and the
    // Platform revoking the family for reuse. This walks the same path.
    for (const screen of ['users', 'roles', 'employees', 'users', 'audit']) {
      await page.goto(`/${current}/${screen}`);
    }

    await expect(page).not.toHaveURL(/\/login/);
    await expect(page.getByText(/حدث خطأ|Something went wrong/)).toHaveCount(0);
  });
});

test.describe('signed out', () => {
  test('the portal is not reachable without a session', async ({ page }, testInfo) => {
    const current = locale(testInfo.project.name);

    await page.goto(`/${current}/users`);

    // The other half of the prerendering bug: the redirect must happen because
    // there is no session, not because it was baked into static HTML.
    await expect(page).toHaveURL(new RegExp(`/${current}/login`));
  });

  test('no token is stored where a script can read it', async ({ page }, testInfo) => {
    await signIn(page, testInfo.project.name);

    const stored = await page.evaluate(() => ({
      local: Object.entries(localStorage),
      session: Object.entries(sessionStorage),
    }));

    // The reason the BFF exists, checked in the browser rather than argued for
    // in a comment. Any token a script can read is a token an XSS on any page
    // can steal.
    const serialised = JSON.stringify(stored);

    expect(serialised).not.toContain('eyJ');
    expect(serialised.toLowerCase()).not.toContain('token');
  });
});
