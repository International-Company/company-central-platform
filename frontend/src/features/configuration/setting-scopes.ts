import type { CompanyDto, RegisteredApplicationDto, SettingDto } from '@/types/platform';

/**
 * One place a setting can be given a value.
 *
 * **The scope and its identifier travel together, because separately they are a
 * trap.** `Company` with a null id and `Platform` with a company's id are both
 * things a form can produce by accident, and the second one resolves to nothing
 * for ever without failing.
 */
export interface ScopeChoice {
  /** Stable value for the `<option>`, and the key the form holds. */
  readonly token: string;

  readonly scope: 'Platform' | 'Company' | 'Application';

  readonly scopeId: string | null;

  /** What the person choosing reads. A name, never a GUID. */
  readonly label: string;
}

export const PlatformToken = 'Platform';

/**
 * Every scope this deployment can actually aim a setting at.
 *
 * <p>
 * Platform always. The company, when one has been set up — this Platform serves
 * exactly one, so "Company" is a single choice rather than a search. Then each
 * registered application, because an application-scoped override is the case
 * where narrowest-wins resolution earns its complexity: one integration wants a
 * longer timeout and nothing else should move.
 * </p>
 */
export function scopeChoices(
  company: CompanyDto | null,
  applications: readonly RegisteredApplicationDto[],
  labels: { platform: string; company: string; application: string },
  arabic: boolean,
): ScopeChoice[] {
  const choices: ScopeChoice[] = [
    { token: PlatformToken, scope: 'Platform', scopeId: null, label: labels.platform },
  ];

  if (company) {
    choices.push({
      token: `Company:${company.id}`,
      scope: 'Company',
      scopeId: company.id,
      label: `${labels.company}: ${arabic ? company.name.ar : company.name.en}`,
    });
  }

  for (const application of applications) {
    choices.push({
      token: `Application:${application.id}`,
      scope: 'Application',
      scopeId: application.id,
      label: `${labels.application}: ${application.name}`,
    });
  }

  return choices;
}

/** The override at this scope, if there is one. */
export function valueAt(setting: SettingDto, choice: ScopeChoice): string | null {
  const match = setting.values.find(
    (value) => value.scope === choice.scope && (value.scopeId ?? null) === choice.scopeId,
  );

  return match?.value ?? null;
}

/**
 * How an override reads in a list: the scope's name, or its identifier when the
 * thing it names is gone.
 *
 * <p>
 * A company or an application can be removed while an override that named it
 * survives. Printing the raw id then is not elegant, but it is the truth, and it
 * is the only string that lets somebody work out what the row is about. Printing
 * nothing would make the override invisible while it went on resolving.
 * </p>
 */
export function describeScope(
  scope: string,
  scopeId: string | null,
  choices: readonly ScopeChoice[],
): string {
  const match = choices.find(
    (choice) => choice.scope === scope && choice.scopeId === (scopeId ?? null),
  );

  return match?.label ?? (scopeId ? `${scope}: ${scopeId}` : scope);
}
