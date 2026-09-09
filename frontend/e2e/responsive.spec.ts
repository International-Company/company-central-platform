import { expect, test } from '@playwright/test';

/**
 * The four widths the design has to survive (ARCHITECTURE.md §9.7).
 *
 * **The rule that matters is that the page never scrolls sideways.** A dense
 * table on a phone has to go somewhere, and the decision was that it scrolls
 * inside its own bounded container while the page does not — because a page
 * that scrolls horizontally hides the left or right edge of every other screen
 * as well, and in Arabic it hides the side the reading starts from.
 *
 * Checked in both locales for the same reason as everything else here: the
 * layout mirrors, and an element pushed past the viewport by a margin escapes
 * in one direction and not the other.
 */

const locale = (project: string) => (project === 'ar' ? 'ar' : 'en');

/** Phone, tablet, small laptop, desktop. */
const Widths = [
  { name: 'phone', width: 360, height: 740 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'laptop', width: 1024, height: 768 },
  { name: 'desktop', width: 1440, height: 900 },
] as const;

const Screens = [
  'dashboard',
  'tasks',
  'notifications',
  'users',
  'employees',
  'organization',
  'workflow',
  'documents',
  'audit',
] as const;

for (const { name, width, height } of Widths) {
  for (const screen of Screens) {
    test(`${screen} does not scroll sideways on a ${name}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height });
      await page.goto(`/${locale(testInfo.project.name)}/${screen}`);
      await page.waitForLoadState('networkidle');

      const overflow = await page.evaluate(() => {
        const root = document.documentElement;

        return {
          scrollWidth: root.scrollWidth,
          clientWidth: root.clientWidth,
        };
      });

      // A pixel or two of rounding is not a defect; a column escaping the
      // viewport is. The tolerance is deliberately tight enough that one
      // escaped element fails.
      expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 2);
    });
  }
}

test('the table becomes a stacked list on a phone', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 360, height: 740 });
  await page.goto(`/${locale(testInfo.project.name)}/users`);
  await page.waitForLoadState('networkidle');

  // Six columns on a phone is a table nobody reads. Below the small breakpoint
  // the same rows render stacked, and the real `<table>` is not shown at all —
  // asserted on what is visible rather than on a class name, because a class
  // name proves it was written and not that it did anything.
  await expect(page.locator('table')).toBeHidden();
});

test('the table is a real table on a desktop', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`/${locale(testInfo.project.name)}/users`);
  await page.waitForLoadState('networkidle');

  // The other half of the same rule. A layout that stacked everywhere would
  // pass the test above and be wrong for the people who use this all day.
  await expect(page.locator('table')).toBeVisible();
});
