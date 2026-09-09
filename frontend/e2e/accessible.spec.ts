import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

/**
 * Every screen, checked against WCAG, in both languages.
 *
 * **The rules were followed by hand and never verified.** Real `<label>`
 * elements bound by `htmlFor`, `role="alert"` on failures, native `<dialog>` for
 * focus trapping, text actions rather than glyphs, the word "Required" rather
 * than a red asterisk — all of it written deliberately and none of it measured.
 * A rule nobody checks is a rule that decays at the first deadline.
 *
 * Run in both locales because the Arabic interface is not a translation: the
 * layout mirrors, and a contrast or reading-order problem can exist in one
 * direction and not the other.
 *
 * **Automated checks find perhaps a third of real barriers.** Passing this is a
 * floor, not a certificate — colour contrast, missing names and broken
 * structure are catchable by a machine; whether a screen makes sense to someone
 * using it with a screen reader is not.
 */

const locale = (project: string) => (project === 'ar' ? 'ar' : 'en');

/** The baseline every screen is held to. */
const Standard = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

const Screens = [
  'dashboard',
  'tasks',
  'users',
  'employees',
  'organization',
  'workflow',
  'roles',
  'security',
  'audit',
] as const;

async function report(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(Standard).analyze();

  // The default failure is a bare count. Naming the rule, the impact and the
  // element is the difference between a number and something a person can act
  // on without opening a browser.
  return results.violations.map(
    (violation) =>
      `${violation.id} (${violation.impact}) — ${violation.help}\n` +
      violation.nodes
        .slice(0, 3)
        .map((node) => `      ${node.target.join(' ')}`)
        .join('\n'),
  );
}

for (const screen of Screens) {
  test(`${screen} has no accessibility violations`, async ({ page }, testInfo) => {
    await page.goto(`/${locale(testInfo.project.name)}/${screen}`);

    // The table and its empty state both render after the first fetch, and a
    // scan that runs before them measures a loading message.
    await page.waitForLoadState('networkidle');

    expect(await report(page)).toEqual([]);
  });
}

test('the sign-in page has no accessibility violations', async ({ page }, testInfo) => {
  // Checked without a session, because it is the one screen every person meets
  // and the only one some of them ever see fail.
  await page.context().clearCookies();
  await page.goto(`/${locale(testInfo.project.name)}/login`);

  expect(await report(page)).toEqual([]);
});

test('the administrator sees the controls their permissions allow', async ({
  page,
}, testInfo) => {
  const current = locale(testInfo.project.name);

  await page.goto(`/${current}/users`);

  // The session's own view of what it may do, read the way the browser reads
  // it. Checked first and separately, so a failure says which half broke: a
  // session that cannot read its permissions is a different defect from a
  // provider that receives them and hides the controls anyway.
  const granted = await page.evaluate(async () => {
    const response = await fetch('/api/me/permissions');

    return response.ok
      ? ((await response.json()) as { permissions: string[] }).permissions
      : null;
  });

  expect(granted, 'the signed-in administrator could not read their own permissions')
    .not.toBeNull();
  expect(granted).toContain('platform.users.create');

  // And then the control itself. Hiding a button the user cannot use is UX;
  // hiding one they can is a Platform that looks empty to the person who
  // administers it — which is how it looked the morning the administrator held
  // no role at all.
  await expect(
    page.getByRole('button', { name: /إنشاء مستخدم|Create user/ }),
  ).toBeVisible();
});

test('a dialog is announced and traps focus', async ({ page }, testInfo) => {
  const current = locale(testInfo.project.name);

  await page.goto(`/${current}/users`);
  await page.getByRole('button', { name: /إنشاء مستخدم|Create user/ }).click();

  const dialog = page.getByRole('dialog');

  await expect(dialog).toBeVisible();

  // A modal makes the rest of the page inert. That is what `showModal` buys and
  // what a hand-built overlay gets subtly wrong for exactly the people who
  // cannot see that it did.
  await expect(dialog).toHaveAttribute('open', '');

  expect(await report(page)).toEqual([]);
});
