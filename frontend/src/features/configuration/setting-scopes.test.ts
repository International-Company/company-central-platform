import { describe, expect, it } from 'vitest';
import {
  describeScope,
  PlatformToken,
  scopeChoices,
  valueAt,
  type ScopeChoice,
} from './setting-scopes';
import type { CompanyDto, RegisteredApplicationDto, SettingDto } from '@/types/platform';

/**
 * Where a setting's value applies.
 *
 * **This is the half of narrowest-wins resolution that had no screen.** The API
 * has always taken a scope and a scope id; the portal sent `Platform` and null,
 * so an override for one application could be neither set nor cleared from
 * anywhere a person could reach — which is precisely the case the resolution
 * order exists for.
 *
 * The assertions worth having are about the pairing. A scope and its identifier
 * are a trap apart: `Company` with no id, or `Platform` carrying one, are both
 * things a form produces by accident, and the second resolves to nothing for
 * ever without failing.
 */
describe('scope choices', () => {
  const labels = { platform: 'Everywhere', company: 'Company', application: 'Application' };

  it('offers the platform even when nothing else exists', () => {
    const choices = scopeChoices(null, [], labels, false);

    expect(choices).toHaveLength(1);
    expect(choices[0]).toMatchObject({
      token: PlatformToken,
      scope: 'Platform',
      scopeId: null,
    });
  });

  it('carries each scope with its own identifier', () => {
    const choices = scopeChoices(company(), [application('a1', 'Payroll')], labels, false);

    expect(choices.map((choice) => [choice.scope, choice.scopeId])).toEqual([
      ['Platform', null],
      ['Company', 'c1'],
      ['Application', 'a1'],
    ]);
  });

  it('names things in the reader language', () => {
    const english = scopeChoices(company(), [], labels, false);
    const arabic = scopeChoices(company(), [], labels, true);

    expect(english[1]!.label).toContain('Acme');
    expect(arabic[1]!.label).toContain('أكمي');
  });
});

describe('the value at a scope', () => {
  const choices = scopeChoices(
    company(),
    [application('a1', 'Payroll')],
    { platform: 'Everywhere', company: 'Company', application: 'Application' },
    false,
  );

  /**
   * The assertion that keeps the dialog honest. Opening it on one scope and
   * seeing another scope's value is how somebody copies an override from one
   * place to another without meaning to.
   */
  it('is the override at that exact scope and identifier', () => {
    const setting = settingWith([
      { scope: 'Platform', scopeId: null, value: '30', setAt: '2026-01-01T00:00:00Z' },
      { scope: 'Application', scopeId: 'a1', value: '90', setAt: '2026-01-01T00:00:00Z' },
    ]);

    expect(valueAt(setting, choices[0]!)).toBe('30');
    expect(valueAt(setting, choices[2]!)).toBe('90');
  });

  it('is nothing where no override exists', () => {
    const setting = settingWith([
      { scope: 'Platform', scopeId: null, value: '30', setAt: '2026-01-01T00:00:00Z' },
    ]);

    expect(valueAt(setting, choices[1]!)).toBeNull();
  });

  /**
   * An override for a different application is not this application's. Matching
   * on the scope alone would show one application's value under another's name
   * and save it there on submit.
   */
  it('does not match another identifier at the same scope', () => {
    const setting = settingWith([
      { scope: 'Application', scopeId: 'a2', value: '90', setAt: '2026-01-01T00:00:00Z' },
    ]);

    expect(valueAt(setting, choices[2]!)).toBeNull();
  });
});

describe('describing a scope', () => {
  const choices: ScopeChoice[] = scopeChoices(
    company(),
    [application('a1', 'Payroll')],
    { platform: 'Everywhere', company: 'Company', application: 'Application' },
    false,
  );

  it('uses the name somebody chose it by', () => {
    expect(describeScope('Application', 'a1', choices)).toContain('Payroll');
  });

  /**
   * An application can be removed while an override naming it survives and goes
   * on resolving. The raw identifier is not elegant, but it is the only string
   * that lets somebody work out what the row is about — and printing nothing
   * would make a live override invisible.
   */
  it('falls back to the identifier when the thing it names is gone', () => {
    expect(describeScope('Application', 'deleted', choices)).toBe('Application: deleted');
  });
});

// --- Fixtures ---------------------------------------------------------------

function company(): CompanyDto {
  return {
    id: 'c1',
    code: 'ACME',
    name: { ar: 'أكمي', en: 'Acme' },
    defaultLocale: 'en',
    isActive: true,
  } as CompanyDto;
}

function application(id: string, name: string): RegisteredApplicationDto {
  return { id, code: name.toLowerCase(), name } as RegisteredApplicationDto;
}

function settingWith(values: SettingDto['values']): SettingDto {
  return {
    key: 'platform.session.idle-timeout',
    applicationCode: 'platform',
    valueType: 'Integer',
    description: null,
    defaultValue: '15',
    isSensitive: false,
    minimum: null,
    maximum: null,
    allowedValues: [],
    values,
  } as SettingDto;
}
