import { request as playwrightRequest, type APIRequestContext } from '@playwright/test';

/**
 * Puts a handful of rows into a freshly started Platform, so the sweeps look at
 * populated screens.
 *
 * **An empty table passes almost everything.** The accessibility sweep ran green
 * on WCAG 2.2 against a Platform with no data in it, and failed the moment one
 * screen had four real rows — on `target-size`, which cannot fire when there are
 * no row actions to measure. Every other assertion in the suite was asking the
 * same easier question: a table renders its empty state, no sort control has
 * anything to sort, and no row action exists to be too small or badly labelled.
 *
 * **It runs from the global setup, after the mandatory password change, and that
 * placement is not incidental.** The first attempt was a shell script in a CI
 * step before the browsers opened, and the Platform refused it — correctly. A
 * temporary password now buys exactly one thing, which is the chance to replace
 * it, so nothing can be seeded until that has happened. Rather than weaken the
 * setup that walks the first-administrator path, the seeding moved to after it.
 *
 * Everything created is obviously fake and lives only in the test database.
 */

/**
 * Two of each where it can, not one: one row proves a table renders, two prove
 * it renders a list.
 */
export async function seed(apiBaseUrl: string, token: string): Promise<void> {
  const api = await playwrightRequest.newContext({
    baseURL: apiBaseUrl,
    extraHTTPHeaders: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
  });

  try {
    // Every read in Organization resolves the company first, so nothing else
    // can be created until it exists.
    await post(api, '/api/v1/organization/company', {
      code: 'E2E',
      nameAr: 'شركة الاختبار',
      nameEn: 'End To End Company',
      defaultLocale: 'ar',
    });

    const division = await post(api, '/api/v1/organization/units', {
      parentId: null,
      unitType: 'Division',
      code: 'OPS',
      nameAr: 'العمليات',
      nameEn: 'Operations',
    });

    const finance = division
      ? await post(api, '/api/v1/organization/units', {
          parentId: division,
          unitType: 'Department',
          code: 'FIN',
          nameAr: 'المالية',
          nameEn: 'Finance',
        })
      : null;

    if (division) {
      await post(api, '/api/v1/organization/units', {
        parentId: division,
        unitType: 'Department',
        code: 'HR',
        nameAr: 'الموارد البشرية',
        nameEn: 'People',
      });
    }

    if (finance) {
      for (const n of [1, 2, 3]) {
        await post(api, '/api/v1/organization/employees', {
          employeeNumber: `E00${n}`,
          fullNameAr: `موظف تجريبي ${n}`,
          fullNameEn: `Test Employee ${n}`,
          unitId: finance,
        });
      }
    }

    // No user accounts. Creating one is a step-up endpoint -- it needs a second
    // factor confirmed in the last few minutes -- and the seed holds a bearer
    // token and nothing else. That is the Platform being right, for the second
    // time in two days: the first attempt at this seed was refused because it
    // ran before the mandatory password change.
    //
    // The Users screen therefore shows the one bootstrap account, which is
    // enough for what this seed is for. A single row has row actions to
    // measure, an empty table does not, and that gap is the whole reason any of
    // this exists.

    for (const role of [
      { code: 'e2e-reader', nameAr: 'قارئ', nameEn: 'Reader' },
      { code: 'e2e-editor', nameAr: 'محرر', nameEn: 'Editor' },
    ]) {
      await post(api, '/api/v1/roles', {
        ...role,
        description: 'Created by the end-to-end seed.',
      });
    }
  } finally {
    await api.dispose();
  }
}

/**
 * Creates something, and returns its id.
 *
 * **Tolerant of a conflict, and of nothing else.** A re-run against a database
 * that already holds this is the seed having already happened, which is
 * success. Any other refusal is a real failure and is thrown with what the
 * server said — the first version of this swallowed everything, and a 403 that
 * should have stopped it went unnoticed until a sweep failed for an unrelated
 * reason.
 */
async function post(
  api: APIRequestContext,
  path: string,
  body: unknown,
): Promise<string | null> {
  const response = await api.post(path, { data: body });

  if (response.ok()) {
    const created = (await response.json().catch(() => null)) as { id?: string } | null;

    return created?.id ?? null;
  }

  if (response.status() === 409 || response.status() === 422) {
    return null;
  }

  throw new Error(
    `Seeding failed: POST ${path} answered ${response.status()} — ` +
      `${(await response.text()).slice(0, 300)}`,
  );
}
