/**
 * The Platform's contracts, as this application sees them.
 *
 * **Generated, not written.** Every type here is an alias onto
 * `platform-api.ts`, which `openapi-typescript` produces from
 * `contracts/platform-api.json` — the document the API itself emits at build
 * time. A backend change that alters a response now breaks this build instead
 * of failing in front of a user.
 *
 * That is not a theoretical benefit. The hand-written version this replaces got
 * two fields wrong: it said `expiresIn` where the contract says
 * `expiresInSeconds`, so the session expiry computed to NaN; and it put
 * `mustChangePassword` at the top level where the contract nests it under
 * `user`, so a first sign-in was never sent to the change-password screen. Both
 * compiled. Both shipped.
 *
 * The aliases exist so screens read `UserDto` rather than
 * `components['schemas']['UserDto']`, and so this file stays the single place to
 * look when a name changes.
 */

import type { components } from './platform-api';

type Schemas = components['schemas'];

export type UserDto = Schemas['UserDto'];
export type CurrentUserDto = Schemas['CurrentUserDto'];
export type AuthenticationResultDto = Schemas['AuthenticationResultDto'];
export type EmployeeDto = Schemas['EmployeeDto'];
export type LocalizedNameDto = Schemas['LocalizedNameDto'];
export type RoleDto = Schemas['RoleDto'];
export type AuditEventDto = Schemas['AuditEventDto'];
export type SessionDto = Schemas['SessionDto'];
export type LoginAttemptDto = Schemas['LoginAttemptDto'];
export type SecurityEventDto = Schemas['SecurityEventDto'];

export type CompanyDto = Schemas['CompanyDto'];
export type PositionDto = Schemas['PositionDto'];
export type OrganizationUnitDto = Schemas['OrganizationUnitDto'];
export type OrganizationUnitTreeDto = Schemas['OrganizationUnitTreeDto'];
export type PermissionDto = Schemas['PermissionDto'];
export type RoleDetailDto = Schemas['RoleDetailDto'];
export type UserRoleDto = Schemas['UserRoleDto'];
export type MyPermissionsDto = Schemas['MyPermissionsDto'];
export type MfaStatusDto = Schemas['MfaStatusDto'];
export type MfaEnrolmentDto = Schemas['MfaEnrolmentDto'];
export type MfaVerificationDto = Schemas['MfaVerificationDto'];
export type RecoveryCodesDto = Schemas['RecoveryCodesDto'];

/**
 * The envelope every list endpoint returns (ADR-008).
 *
 * Kept as a generic here because the generated document expresses each closed
 * form separately — `PagedResultOfUserDto`, `PagedResultOfEmployeeDto` — and a
 * screen wants to say "a page of these" once.
 */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNext: boolean;
}

/** RFC 9457, as the Platform returns it on failure. */
export interface ProblemResponse {
  code?: string;
  detail?: string;
  correlationId?: string;
  errors?: { code: string; message: string; field?: string }[];
}
