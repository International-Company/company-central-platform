import { expect, test } from '@playwright/test';
import { AdminPassword } from './global-setup';

/**
 * Signing in with a fingerprint, end to end.
 *
 * **This is the only test that exercises the whole chain.** The verifier's unit
 * tests prove it accepts what Chrome produces; these prove that the challenge
 * the Platform issues reaches the browser, that what the browser signs reaches
 * the Platform, that the credential is stored against the right account, and
 * that presenting it later starts a session. Every one of those is a seam
 * between two systems that agree on base64url and nothing else.
 *
 * Chrome's virtual authenticator stands in for the sensor: it is the real
 * WebAuthn implementation, with a simulated finger. There is no fingerprint in
 * a headless browser, and there is none in a real one either as far as this
 * Platform is concerned — the device verifies the person and says that it did.
 */

const locale = (project: string) => (project === 'ar' ? 'ar' : 'en');

const signIn = /تسجيل الدخول|^Sign in$/;
const signOut = /تسجيل الخروج|Sign out/;
const addDevice = /إضافة جهاز|Add this device/;
const withFingerprint = /الدخول بالبصمة|Sign in with your fingerprint/;
const devicePassword = /^(كلمة المرور الحالية|Current password)/;
const deviceName = /^(اسم الجهاز|Device name)/;

test.describe('signing in with a passkey', () => {
  // Its own session, signed in by hand: this test signs out, and the shared
  // cookie every other spec uses would go with it.
  test.use({ storageState: { cookies: [], origins: [] } });

  test('registers a device, then uses it instead of a password', async ({
    page,
    context,
  }, testInfo) => {
    const current = locale(testInfo.project.name);

    // A platform authenticator with a fingerprint reader, which the person
    // always passes. `hasResidentKey` is what makes the credential
    // discoverable, and discoverable is what lets signing in ask for no
    // username.
    const session = await context.newCDPSession(page);

    await session.send('WebAuthn.enable');
    await session.send('WebAuthn.addVirtualAuthenticator', {
      options: {
        protocol: 'ctap2',
        ctap2Version: 'ctap2_1',
        transport: 'internal',
        hasResidentKey: true,
        hasUserVerification: true,
        isUserVerified: true,
        automaticPresenceSimulation: true,
      },
    });

    // --- signed in the ordinary way, to begin with ------------------------
    await page.goto(`/${current}/login`);
    await page.getByLabel(/^(اسم المستخدم|Username)/).fill(
      process.env.E2E_USERNAME ?? 'e2e-admin',
    );
    await page.getByLabel(/^(كلمة المرور|Password)/).fill(AdminPassword);
    await page.getByRole('button', { name: signIn }).click();

    await page.waitForURL(new RegExp(`/${current}/dashboard`), { timeout: 30_000 });

    // --- register the device ----------------------------------------------
    await page.goto(`/${current}/security`);

    // The button appears only after the browser has been asked whether this
    // machine can verify a person, which is why this waits rather than clicks.
    const add = page.getByRole('button', { name: addDevice }).first();

    await expect(add).toBeVisible({ timeout: 15_000 });
    await add.click();

    await page.getByLabel(deviceName).fill('End to end laptop');
    await page.getByLabel(devicePassword).fill(AdminPassword);

    // Scoped to the open dialog. The heading above carries the same words, and
    // "the second one on the page" is a selector that breaks the first time
    // anything is added between them.
    await page.locator('dialog[open]').getByRole('button', { name: addDevice }).click();

    // The row, which only exists if the Platform verified the attestation and
    // stored the key.
    //
    // `.first()`, because the table renders each row twice: once as a table for
    // a wide screen and once as a stacked list for a phone. The first version
    // of this line failed on a strict-mode violation with two matches, which
    // was the feature working and the selector not knowing it.
    await expect(page.getByText('End to end laptop').first()).toBeVisible({ timeout: 20_000 });

    // --- sign out, and sign back in with it -------------------------------
    await page.getByRole('button', { name: signOut }).first().click();
    await page.waitForURL(new RegExp(`/${current}/login$`), { timeout: 30_000 });

    const fingerprint = page.getByRole('button', { name: withFingerprint });

    await expect(fingerprint).toBeVisible({ timeout: 15_000 });
    await fingerprint.click();

    // No username was typed, and none was sent. Landing here means the
    // signature alone identified the account and started a session.
    await page.waitForURL(new RegExp(`/${current}/dashboard`), { timeout: 30_000 });

    await expect(page.getByText(/حدث خطأ|Something went wrong/)).toHaveCount(0);
  });

  test('a device that does not verify the person is refused', async ({
    page,
    context,
  }, testInfo) => {
    // The check that makes a passkey worth a password and a code together. An
    // authenticator that only proves possession — a key in a pocket, with no
    // finger, face or passcode — must not be accepted, because accepting it
    // would quietly make the strongest credential on the Platform the weakest.
    const current = locale(testInfo.project.name);

    const session = await context.newCDPSession(page);

    await session.send('WebAuthn.enable');
    await session.send('WebAuthn.addVirtualAuthenticator', {
      options: {
        protocol: 'ctap2',
        ctap2Version: 'ctap2_1',
        transport: 'internal',
        hasResidentKey: true,

        // No user verification at all.
        hasUserVerification: false,
        isUserVerified: false,
        automaticPresenceSimulation: true,
      },
    });

    await page.goto(`/${current}/login`);
    await page.getByLabel(/^(اسم المستخدم|Username)/).fill(
      process.env.E2E_USERNAME ?? 'e2e-admin',
    );
    await page.getByLabel(/^(كلمة المرور|Password)/).fill(AdminPassword);
    await page.getByRole('button', { name: signIn }).click();
    await page.waitForURL(new RegExp(`/${current}/dashboard`), { timeout: 30_000 });

    await page.goto(`/${current}/security`);

    // The browser is asked to require verification, so an authenticator that
    // cannot do it either refuses outright or returns a response the Platform
    // refuses. Either way no passkey appears.
    const add = page.getByRole('button', { name: addDevice }).first();

    if (await add.isVisible().catch(() => false)) {
      await add.click();
      await page.getByLabel(deviceName).fill('Key with no sensor');
      await page.getByLabel(devicePassword).fill(AdminPassword);
      await page.locator('dialog[open]').getByRole('button', { name: addDevice }).click();

      await page.waitForTimeout(3_000);
    }

    await expect(page.getByText('Key with no sensor')).toHaveCount(0);
  });
});
