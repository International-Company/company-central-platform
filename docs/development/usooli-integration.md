# Connecting the Asset System (أصولي) to the Platform

Written for: whoever maintains أصولي. It assumes you know that repository and
nothing about this one.

أصولي already has the seam: `EapProvider` in
`apps/api/src/modules/eap/eap.types.ts`, with a mock implementation for
development and `EapHttpProvider` left as a deliberate stub that throws
`SERVICE_UNAVAILABLE` because "the EAP REST contract has not been provided yet
(spec §88)".

This is that contract, and a complete implementation of the stub.

## What was decided, and what it costs

أصولي keeps its own sign-in screen. The password is checked against the
Platform, which stays the source of truth for accounts and for employee data.

Two consequences worth stating plainly rather than discovering later:

- **أصولي receives Platform passwords.** They are relayed to the Platform and
  never stored, but they pass through أصولي's process. That is acceptable
  between two first-party systems owned by the same company and it is not the
  best available answer. The better one is redirect-based single sign-on, where
  the person types their password only on the Platform's own domain and أصولي
  never sees it. The Platform does not have that flow yet; it has
  client-credentials for machines only.
- **The fingerprint step cannot be relayed.** A WebAuthn credential is bound by
  the browser to one domain. A passkey registered for the Platform's domain
  cannot be used from أصولي's, and no amount of proxying changes that — it is
  the property that makes passkeys phishing-resistant. If أصولي wants a
  fingerprint step, it registers its **own** passkeys for its own domain. The
  Platform's implementation is in `docs/security/passkeys.md` and is worth
  copying rather than reinventing.

`verifyFingerprint` below therefore has no Platform call behind it, and says so.

## What أصولي needs from the Platform

| Purpose | Endpoint | Auth |
|---|---|---|
| Machine token | `POST /api/v1/oauth/token` | client id + secret |
| Check a password | `POST /api/v1/auth/login` | anonymous |
| Release the session that check created | `POST /api/v1/auth/logout` | the refresh token it returned |
| Employee behind an account | `GET /api/v1/organization/employees/by-user/{userId}` | machine token |
| One employee | `GET /api/v1/organization/employees/{id}` | machine token |
| Search employees | `GET /api/v1/organization/employees?q=&pageSize=` | machine token |
| Health | `GET /api/v1/diagnostics/ping` | anonymous |

The last three lookups did not exist before this integration. Listing employees
was possible and reading one was not, and `EmployeeDto.userId` was a field
nothing could search by.

## Registering أصولي

On the Platform's portal, **Applications → Create application**. Keep the client
secret: it is shown once and cannot be read again, only rotated.

Then grant the application a role holding **`platform.employees.view`** at the
scope you want it to see — all employees, or one branch of the organization.
Nothing else is needed for what is implemented here.

Then, in أصولي's `apps/api/.env`:

```
AUTH_PROVIDER=eap
EAP_API_URL=https://<the Platform>
EAP_CLIENT_ID=<from the portal>
EAP_CLIENT_SECRET=<from the portal, once>
```

أصولي already refuses to start with `AUTH_PROVIDER=eap` unless all three are
set, and refuses the mock provider outside development. Both are correct and
neither needs changing.

## `eapEmployeeId` is the Platform's employee id

Not the user id. أصولي stores it, compares it on every sign-in, and passes it to
`getEmployee` and `searchEmployees` — and a search has to be able to return
employees who have no account at all, which a user id cannot represent.

The consequence: **a Platform account with no employee record cannot sign in to
أصولي**. `verifyPassword` returns `{ ok: false }` for one. That is correct for an
asset system — a person who is not an employee cannot hold or receive an asset —
but it is a policy decision, and it is the one thing below that you might want
to change. It is marked in the code.

## The implementation

Replace `apps/api/src/modules/eap/eap-http.provider.ts` with this. It needs
nothing that is not already in أصولي.

```ts
import { AppError } from '../../common/errors/app-error';
import type { EapEmployee, EapProvider, PasswordResult, ProviderHealth } from './eap.types';

/**
 * Real EAP integration: the Company Central Platform.
 *
 * The Platform is the source of truth for accounts and for employee data. This
 * provider holds a machine token for reading employees, and relays password
 * checks to the Platform's own sign-in.
 *
 * **Nothing is cached but the machine token.** Employee data changes on the
 * Platform and a stale copy here would be a copy nobody knows is stale.
 */
export class EapHttpProvider implements EapProvider {
  readonly name = 'eap' as const;

  /** The machine token, and the moment it stops being worth trying. */
  private token: { value: string; expiresAt: number } | null = null;

  constructor(
    private readonly config: { baseUrl: string; clientId: string; clientSecret: string },
  ) {}

  // --- Step 2: the password ------------------------------------------------

  async verifyPassword(username: string, password: string): Promise<PasswordResult> {
    const response = await this.call('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    // The Platform answers a wrong username and a wrong password identically,
    // on purpose: any difference an attacker can observe is a difference that
    // tells them which accounts exist. Passed through unchanged.
    if (response.status === 401 || response.status === 422) {
      return { ok: false };
    }

    if (response.status === 429) {
      throw new AppError('SERVICE_UNAVAILABLE', 'محاولات كثيرة. حاول بعد قليل.', {
        internal: 'Platform rate limit on /auth/login',
      });
    }

    if (!response.ok) {
      throw this.platformFailed(response.status, 'auth/login');
    }

    const session = (await response.json()) as {
      accessToken: string;
      user: { id: string };
    };

    // The Platform issued a session for this check. أصولي does not use it — it
    // issues its own — so it is ended immediately. Without this, every sign-in
    // to أصولي would leave a live Platform session behind it, for ever.
    await this.releaseSession(session.accessToken);

    const employee = await this.employeeByUser(session.user.id);

    if (!employee) {
      // A valid Platform account with no employee record: a contractor, a
      // service account, the bootstrap administrator. They cannot hold an
      // asset, so they cannot sign in here.
      //
      // Change this line if أصولي should admit them instead — it is the one
      // policy decision in this file.
      return { ok: false };
    }

    return { ok: true, eapEmployeeId: employee.eapEmployeeId, providerRef: null };
  }

  // --- Step 3: the fingerprint ---------------------------------------------

  /**
   * **Not the Platform's.** A WebAuthn credential is bound by the browser to
   * one domain, so a passkey registered on the Platform cannot be used from
   * أصولي's origin — that binding is exactly what makes passkeys resistant to
   * phishing, and proxying the assertion does not work around it, it breaks it.
   *
   * If أصولي wants a fingerprint step it registers its own passkeys, for its
   * own domain, and verifies them here. Until then this refuses rather than
   * pretending: returning true would make a step the specification calls a
   * second factor into a formality.
   */
  verifyFingerprint(): Promise<boolean> {
    throw new AppError('SERVICE_UNAVAILABLE', 'خطوة البصمة غير مهيأة في هذا النظام.', {
      internal:
        'WebAuthn credentials are origin-bound; a Platform passkey cannot be asserted '
        + "from أصولي's domain. Register passkeys here instead.",
    });
  }

  // --- Employees -----------------------------------------------------------

  async getEmployee(eapEmployeeId: string): Promise<EapEmployee | null> {
    const response = await this.call(
      `/api/v1/organization/employees/${encodeURIComponent(eapEmployeeId)}`,
      { headers: { Authorization: `Bearer ${await this.machineToken()}` } },
    );

    if (response.status === 404) {
      return null;
    }

    if (!response.ok) {
      throw this.platformFailed(response.status, 'organization/employees/{id}');
    }

    return this.toEmployee(await response.json());
  }

  async searchEmployees(query: string, limit: number): Promise<EapEmployee[]> {
    const url = `/api/v1/organization/employees`
      + `?q=${encodeURIComponent(query)}&page=1&pageSize=${Math.min(Math.max(limit, 1), 100)}`;

    const response = await this.call(url, {
      headers: { Authorization: `Bearer ${await this.machineToken()}` },
    });

    if (!response.ok) {
      throw this.platformFailed(response.status, 'organization/employees');
    }

    const page = (await response.json()) as { items: unknown[] };

    return page.items.map((item) => this.toEmployee(item));
  }

  private async employeeByUser(userId: string): Promise<EapEmployee | null> {
    const response = await this.call(
      `/api/v1/organization/employees/by-user/${encodeURIComponent(userId)}`,
      { headers: { Authorization: `Bearer ${await this.machineToken()}` } },
    );

    if (response.status === 404) {
      return null;
    }

    if (!response.ok) {
      throw this.platformFailed(response.status, 'organization/employees/by-user');
    }

    return this.toEmployee(await response.json());
  }

  /**
   * The Platform's employee, in أصولي's shape.
   *
   * `fullName` is bilingual on the Platform; Arabic is taken, because that is
   * what أصولي's screens are in.
   */
  private toEmployee(raw: unknown): EapEmployee {
    const employee = raw as {
      id: string;
      fullName: { ar: string; en: string };
      positionCode: string | null;
      workEmail: string | null;
      workPhone: string | null;
      isActive: boolean;
    };

    return {
      eapEmployeeId: employee.id,
      fullName: employee.fullName.ar || employee.fullName.en,
      jobTitle: employee.positionCode,
      email: employee.workEmail,
      phone: employee.workPhone,
      isActive: employee.isActive,
    };
  }

  // --- The machine token ---------------------------------------------------

  /**
   * A token for أصولي itself, cached until shortly before it expires.
   *
   * Refreshed early rather than on the first refusal: a token that expires
   * between the check and the call is a request that fails for no reason the
   * logs explain.
   */
  private async machineToken(): Promise<string> {
    if (this.token && Date.now() < this.token.expiresAt) {
      return this.token.value;
    }

    // Form-encoded and snake_case, because this is OAuth 2.0 and the rest of
    // the Platform's JSON is not. Both halves of that caught me out: the
    // request is not JSON and the response is not camelCase.
    const response = await this.call('/api/v1/oauth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: this.config.clientId,
        client_secret: this.config.clientSecret,
      }),
    });

    if (!response.ok) {
      this.token = null;

      throw new AppError('SERVICE_UNAVAILABLE', 'تعذّر الاتصال بمنصة الشركة.', {
        internal: `Platform rejected the client credentials (${response.status})`,
      });
    }

    const issued = (await response.json()) as { access_token: string; expires_in: number };

    this.token = {
      value: issued.access_token,
      expiresAt: Date.now() + (Number(issued.expires_in) - 60) * 1000,
    };

    return this.token.value;
  }

  /**
   * Ends the Platform session created by a password check.
   *
   * Failure is swallowed: أصولي's own sign-in has already succeeded or failed
   * on its own terms, and refusing somebody entry because a tidy-up call did
   * not answer would be reporting our problem as theirs. It is logged by the
   * Platform either way.
   */
  private async releaseSession(accessToken: string): Promise<void> {
    try {
      // No body. The Platform reads whose session to end from the token
      // itself, deliberately: taking a user id from the request would let
      // anyone end anyone else's session.
      await this.call('/api/v1/auth/logout', {
        method: 'POST',
        headers: { Authorization: `Bearer ${accessToken}` },
      });
    } catch {
      // Nothing to do, and nothing worth failing a sign-in over.
    }
  }

  // --- Plumbing ------------------------------------------------------------

  async health(): Promise<ProviderHealth> {
    const started = Date.now();

    try {
      const response = await this.call('/api/v1/diagnostics/ping');

      return {
        status: response.ok ? 'up' : 'down',
        latencyMs: Date.now() - started,
      };
    } catch {
      return { status: 'down', latencyMs: Date.now() - started };
    }
  }

  private async call(path: string, init: RequestInit = {}): Promise<Response> {
    try {
      return await fetch(new URL(path, this.config.baseUrl), {
        ...init,
        signal: AbortSignal.timeout(10_000),
      });
    } catch (cause) {
      throw new AppError('SERVICE_UNAVAILABLE', 'تعذّر الاتصال بمنصة الشركة.', {
        internal: `Platform unreachable at ${path}`,
        cause,
      });
    }
  }

  private platformFailed(status: number, what: string): AppError {
    return new AppError('SERVICE_UNAVAILABLE', 'تعذّر الاتصال بمنصة الشركة.', {
      internal: `Platform returned ${status} from ${what}`,
    });
  }
}
```

## Before you trust it

Every shape above was read out of the Platform's own source and contract rather
than remembered, and three of them were not what they looked like:

- **`/oauth/token` is form-encoded, and answers in snake_case.** It is OAuth
  2.0, so `grant_type` / `client_id` / `client_secret` go as a form, and what
  comes back is `access_token` and `expires_in` — not the camelCase the rest of
  the Platform's JSON uses. The first draft of this file had both wrong.
- **`/auth/logout` takes no body.** It reads whose session to end from the
  access token, so the call carries `Authorization: Bearer`, not a refresh
  token in JSON. Sending a body would have silently done nothing.
- **`EmployeeDto.fullName` is `{ ar, en }`**, not a string.

What is still worth checking on your side:

- **The `AppError` constructor.** This was written from the stub it replaces,
  which uses `new AppError(code, arabicMessage, { internal })`. If yours takes
  a `cause` differently, the two places that pass one need adjusting.
- **Point it at a Platform you can break.** Run both locally first: the Platform
  on `localhost:5080`, أصولي's `EAP_API_URL=http://localhost:5080`.
- **Generate the types.** `contracts/platform-api.json` in the Platform
  repository is the contract; running `openapi-typescript` over it would let
  أصولي's compiler check this file instead of you.

## Acting for a person, later

The token endpoint also takes `on_behalf_of`. An application that holds a
permission *and* the user does may act as them, and the audit trail records
both. أصولي does not need it yet — it reads employees as itself — but it is
how a write on somebody's behalf should be made when that comes.

## What is still missing, honestly

- **No redirect single sign-on.** أصولي handles passwords. Worth revisiting.
- **No step-up.** The Platform can demand a second factor for privileged
  actions; أصولي has no way to ask for one.
- **No events.** The Platform publishes `identity.user.*` and
  `organization.employee.*` to webhook subscribers. أصولي polls instead: an
  employee deactivated on the Platform stays active in أصولي until something
  reads them again. Subscribing is a small piece of work and closes a real gap.
- **No audit bridge.** أصولي's own audit trail and the Platform's are separate
  records of the same person's actions.
