import type { ProblemResponse } from '@/types/platform';

/**
 * Saying, in the reader's own language, why the Platform refused.
 *
 * **The portal threw these away.** Somebody pressed "grant role" for a user who
 * already held that role at that scope. The Platform answered exactly that —
 * `AUTHZ.ALREADY_GRANTED`, "The user already holds this role at this scope" —
 * and the screen said "something went wrong, try again". So they tried again,
 * and got the same refusal, reported the same way.
 *
 * The cause sat between two decisions that are each correct. The Platform puts
 * field-level `errors` in the body only for validation failures, because
 * anywhere else they would leak internal field names; and this portal's proxy
 * strips the Platform's `detail`, because that sentence is written for an
 * operator. What survives both is `code` — and nothing was reading it. Every
 * refusal that was not a validation failure came out as the generic sentence.
 *
 * A table here rather than a sentence from the server, deliberately: what a
 * person is told should be in their language, and the Platform's own text is
 * English by design. An unlisted code still falls back to the generic sentence,
 * so adding one is safe and leaving one out changes nothing.
 */
const SPOKEN = {
  'AUTHZ.ALREADY_GRANTED': 'codes.alreadyGranted',
  'AUTHZ.APPLICATION_ALREADY_GRANTED': 'codes.applicationAlreadyGranted',
  'AUTHZ.ASSIGNMENT_ALREADY_REVOKED': 'codes.assignmentAlreadyRevoked',
  'AUTHZ.ASSIGNMENT_NOT_FOUND': 'codes.assignmentNotFound',
  'AUTHZ.CANNOT_GRANT_TO_SELF': 'codes.cannotGrantToSelf',
  'AUTHZ.CANNOT_GRANT_UNHELD_PERMISSION': 'codes.cannotGrantUnheld',
  'AUTHZ.CANNOT_GRANT_WIDER_SCOPE': 'codes.cannotGrantWiderScope',
  'AUTHZ.EXPIRY_IN_THE_PAST': 'codes.expiryInThePast',
  'AUTHZ.ROLE_INACTIVE': 'codes.roleInactive',
  'AUTHZ.ROLE_NOT_FOUND': 'codes.roleNotFound',
  'AUTHZ.SCOPE_UNIT_NOT_APPLICABLE': 'codes.scopeUnitNotApplicable',
  'AUTHZ.SCOPE_UNIT_REQUIRED': 'codes.scopeUnitRequired',
  'AUTHZ.TOO_MANY_LIVE_CREDENTIALS': 'codes.tooManyLiveCredentials',
  'AUTHZ.WOULD_STRAND_THE_PLATFORM': 'codes.wouldStrandThePlatform',
} as const;

/** The message keys this table can ask for, so a typo is a build error. */
export type RefusalKey = (typeof SPOKEN)[keyof typeof SPOKEN];

/**
 * The sentence for a refusal, or `null` when there is nothing better to say
 * than the caller's own fallback.
 *
 * `null` rather than the generic sentence, because the fallback differs by
 * screen — one of them has a rule of its own worth naming — and deciding that
 * here would be deciding something this file cannot see.
 */
export function refusalMessage(
  problem: ProblemResponse | null | undefined,
  translate: (key: RefusalKey) => string,
): string | null {
  const code = problem?.code;
  const key = code === undefined ? undefined : SPOKEN[code as keyof typeof SPOKEN];

  if (key !== undefined) {
    return translate(key);
  }

  // A validation failure carries its own field-level text, which names the
  // input that was wrong and is more specific than anything a table can hold.
  return problem?.errors?.[0]?.message ?? null;
}
