import { describe, expect, it, vi } from 'vitest';

// `bff.ts` reaches session.ts, which is `server-only` and reads cookies. Neither
// belongs in this test: what is under test is how a Platform response becomes a
// browser response, and nothing here goes near a session.
vi.mock('server-only', () => ({}));
vi.mock('../session', () => ({ clearSession: vi.fn() }));

const { relay } = await import('../bff');

/**
 * Turning a Platform response into one for the browser.
 *
 * **These exist because the no-content case was not handled and it broke every
 * action in the application.** The Platform answers 204 to a command with
 * nothing to return — changing a password, moving a unit, granting a role,
 * disabling an account. A 204 carries no body, so the old check for "data
 * arrived" was false, and the failure branch built an error body at status 204,
 * which `NextResponse` refuses to construct. The handler threw, the browser saw
 * 500, and the screen reported a failure for work that had already succeeded.
 *
 * It survived every unit test, because there were none, and every end-to-end
 * run, because the suite never got past the password change it caused.
 */

function platformResponse<T>(overrides: Partial<{
  status: number;
  data: T | null;
  problem: { code?: string; status?: number } | null;
  correlationId: string | null;
  sessionExpired: boolean;
}>) {
  return {
    status: 200,
    data: null,
    problem: null,
    correlationId: null,
    ...overrides,
  } as Parameters<typeof relay>[0];
}

describe('relaying a Platform response', () => {
  it('passes a 204 through with no body rather than throwing', async () => {
    const response = await relay(platformResponse({ status: 204 }));

    expect(response.status).toBe(204);
    expect(await response.text()).toBe('');
  });

  it('treats every other empty success the same way', async () => {
    // 205 is the other status the fetch spec forbids a body on. Included so the
    // rule is "no body means no body", not "204 is special".
    const response = await relay(platformResponse({ status: 205 }));

    expect(response.status).toBe(205);
  });

  it('returns the body when there is one', async () => {
    const response = await relay(
      platformResponse({ status: 200, data: { id: 'a', name: 'Finance' } }),
    );

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ id: 'a', name: 'Finance' });
  });

  it('keeps a created response and its body', async () => {
    const response = await relay(platformResponse({ status: 201, data: { id: 'b' } }));

    expect(response.status).toBe(201);
    expect(await response.json()).toEqual({ id: 'b' });
  });

  it('passes a failure through with its code and status', async () => {
    const response = await relay(
      platformResponse({
        status: 403,
        problem: { code: 'SECURITY.STEP_UP_REQUIRED' },
        correlationId: 'abc',
      }),
    );

    expect(response.status).toBe(403);

    // The code is what a screen acts on — it is how "confirm your identity" is
    // told apart from "you may not do this", which arrive with the same status.
    expect(await response.json()).toMatchObject({
      code: 'SECURITY.STEP_UP_REQUIRED',
      correlationId: 'abc',
    });
  });

  it('never passes on the Platform detail', async () => {
    const response = await relay(
      platformResponse({
        status: 500,
        problem: { code: 'PLATFORM.INTERNAL_ERROR' },
      }),
    );

    // `detail` is written for an operator and may name internals. The
    // correlation id is what a user quotes to an engineer.
    expect(await response.text()).not.toContain('detail');
  });
});
