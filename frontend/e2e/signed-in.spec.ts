import { expect, test } from '@playwright/test';

/**
 * Every screen, in both languages, with a real session.
 *
 * **This is the suite that was missing.** Four defects reached the deployed
 * application in one day and every one of them would have failed here:
 *
 *   - the portal pages were prerendered, so a successful sign-in bounced
 *     straight back to the login page;
 *   - no custom colour rendered at all, because the class syntax produced no
 *     CSS rule;
 *   - the employees screen called a path that does not exist;
 *   - concurrent requests each refreshed the token, and the Platform's reuse
 *     detection revoked the session — correctly.
 *
 * Every unit test passed throughout. None of these lives in a unit.
 *
 * The session comes from global setup, which performs the real sign-in
 * including the mandatory first password change.
 */

const locale = (project: string) => (project === 'ar' ? 'ar' : 'en');

const failureBanner = /حدث خطأ|Something went wrong|لا تملك صلاحية|do not have permission/;

test.describe('the portal', () => {
  test('opens the dashboard and stays there', async ({ page }, testInfo) => {
    await page.goto(`/${locale(testInfo.project.name)}/dashboard`);

    // The regression that mattered most: sign-in succeeded and a prerendered
    // redirect sent the person back to the login page, forever.
    await expect(page).not.toHaveURL(/\/login/);
  });

  test('mirrors the layout for the locale', async ({ page }, testInfo) => {
    const expected = testInfo.project.name === 'ar' ? 'rtl' : 'ltr';

    await page.goto(`/${locale(testInfo.project.name)}/dashboard`);

    // `dir` on the root is what mirrors the whole layout rather than only the
    // text. If it is wrong, every logical property points the wrong way.
    await expect(page.locator('html')).toHaveAttribute('dir', expected);
    await expect(page.locator('html')).toHaveAttribute(
      'lang',
      locale(testInfo.project.name),
    );
  });

  test('renders the design tokens', async ({ page }, testInfo) => {
    await page.goto(`/${locale(testInfo.project.name)}/dashboard`);

    // A computed colour, because a class name in the markup proves it was
    // written and not that it did anything — which is exactly how the missing
    // styles survived a local build, a production build and a deployment.
    const background = await page
      .locator('header')
      .first()
      .evaluate((element) => getComputedStyle(element).backgroundColor);

    expect(background).not.toBe('rgba(0, 0, 0, 0)');
    expect(background).not.toBe('transparent');
  });

  for (const screen of ['users', 'employees', 'roles', 'audit'] as const) {
    test(`opens ${screen} and can read it`, async ({ page }, testInfo) => {
      await page.goto(`/${locale(testInfo.project.name)}/${screen}`);

      await expect(page).toHaveURL(new RegExp(`/${screen}$`));

      // No failure banner — which covers a wrong path, a dead session and a
      // missing permission at once. The employees screen shipped calling a path
      // that did not exist, and the administrator shipped holding no role at
      // all; both show here and nowhere else.
      await expect(page.getByText(failureBanner)).toHaveCount(0);
    });
  }

  test('survives several screens in a row', async ({ page }, testInfo) => {
    const current = locale(testInfo.project.name);

    // Navigating quickly is what set off the refresh stampede: several requests
    // in flight, all seeing an expired access token, all refreshing, and the
    // Platform revoking the family for reuse.
    for (const screen of ['users', 'roles', 'employees', 'users', 'audit']) {
      await page.goto(`/${current}/${screen}`);
    }

    await expect(page).not.toHaveURL(/\/login/);
    await expect(page.getByText(failureBanner)).toHaveCount(0);
  });

  test('keeps no token where a script can read it', async ({ page }, testInfo) => {
    await page.goto(`/${locale(testInfo.project.name)}/dashboard`);

    const stored = await page.evaluate(() =>
      JSON.stringify({
        local: Object.entries(localStorage),
        session: Object.entries(sessionStorage),
      }),
    );

    // The reason the BFF exists, checked in the browser rather than argued for
    // in a comment. Any token a script can read is one an XSS can steal.
    expect(stored).not.toContain('eyJ');
    expect(stored.toLowerCase()).not.toContain('token');
  });
});

test.describe('without a session', () => {
  // A clean context: this is about what an anonymous visitor sees.
  test.use({ storageState: { cookies: [], origins: [] } });

  test('the portal redirects to sign-in', async ({ page }, testInfo) => {
    const current = locale(testInfo.project.name);

    await page.goto(`/${current}/users`);

    // The other half of the prerendering bug: the redirect must happen because
    // there is no session, not because it was baked into static HTML.
    await expect(page).toHaveURL(new RegExp(`/${current}/login`));
  });
});
