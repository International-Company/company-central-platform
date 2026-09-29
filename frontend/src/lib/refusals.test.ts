import { describe, expect, it } from 'vitest';
import en from '@/i18n/messages/en.json';
import { refusalMessage, type RefusalKey } from './refusals';

/**
 * Reads a key the way next-intl would, so a table entry pointing at a message
 * nobody wrote fails here rather than printing its own key on screen.
 */
function translate(key: RefusalKey): string {
  const value = key
    .split('.')
    .reduce<unknown>(
      (node, part) =>
        typeof node === 'object' && node !== null
          ? (node as Record<string, unknown>)[part]
          : undefined,
      en.errors,
    );

  if (typeof value !== 'string') {
    throw new Error(`No message for ${key}`);
  }

  return value;
}

describe('refusalMessage', () => {
  it('says why a grant was refused, rather than that something went wrong', () => {
    // The refusal that started this: the Platform said exactly what was wrong
    // and the screen printed "something went wrong, try again".
    expect(refusalMessage({ code: 'AUTHZ.ALREADY_GRANTED' }, translate)).toBe(
      'This user already holds that role at that scope. Nothing is missing.',
    );
  });

  it('has a message for every code in the table', () => {
    // Every entry resolves, so the table cannot point at a key that was
    // renamed or never written. `translate` throws when one does not.
    const codes = [
      'AUTHZ.ALREADY_GRANTED',
      'AUTHZ.APPLICATION_ALREADY_GRANTED',
      'AUTHZ.ASSIGNMENT_ALREADY_REVOKED',
      'AUTHZ.ASSIGNMENT_NOT_FOUND',
      'AUTHZ.CANNOT_GRANT_TO_SELF',
      'AUTHZ.CANNOT_GRANT_UNHELD_PERMISSION',
      'AUTHZ.CANNOT_GRANT_WIDER_SCOPE',
      'AUTHZ.EXPIRY_IN_THE_PAST',
      'AUTHZ.ROLE_INACTIVE',
      'AUTHZ.ROLE_NOT_FOUND',
      'AUTHZ.SCOPE_UNIT_NOT_APPLICABLE',
      'AUTHZ.SCOPE_UNIT_REQUIRED',
      'AUTHZ.TOO_MANY_LIVE_CREDENTIALS',
      'AUTHZ.WOULD_STRAND_THE_PLATFORM',
    ];

    for (const code of codes) {
      expect(refusalMessage({ code }, translate)).toBeTypeOf('string');
    }
  });

  it('leaves the fallback to the caller for a code it does not know', () => {
    // Null, not the generic sentence: one screen has a rule of its own worth
    // naming, and this file cannot see which.
    expect(refusalMessage({ code: 'WORKFLOW.SOMETHING_NEW' }, translate)).toBeNull();
    expect(refusalMessage({}, translate)).toBeNull();
    expect(refusalMessage(null, translate)).toBeNull();
  });

  it('prefers a validation failure’s own field-level text', () => {
    // More specific than anything a table holds: it names the input that was
    // wrong, which is what the reader has to go and change.
    expect(
      refusalMessage(
        {
          code: 'ORGANIZATION.UNIT_CODE_TAKEN',
          errors: [{ code: 'ORGANIZATION.UNIT_CODE_TAKEN', message: 'That code is in use.', field: 'code' }],
        },
        translate,
      ),
    ).toBe('That code is in use.');
  });

  it('still prefers the table when a known code also carries field errors', () => {
    // The table is written for a reader; the Platform's own text is written
    // for an operator and is English whatever language the screen is in.
    expect(
      refusalMessage(
        {
          code: 'AUTHZ.EXPIRY_IN_THE_PAST',
          errors: [{ code: 'AUTHZ.EXPIRY_IN_THE_PAST', message: 'The expiry must be in the future.' }],
        },
        translate,
      ),
    ).toBe('The expiry has to be in the future.');
  });
});
