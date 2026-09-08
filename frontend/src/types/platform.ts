/**
 * The Platform's contracts, as this application sees them.
 *
 * Hand-written for now, and that is a stated gap rather than a preference:
 * Phase 7 task 11 is to generate these from the OpenAPI document, so a backend
 * change that breaks a screen fails the build instead of failing in front of a
 * user. Until then these are kept in one file, so there is exactly one place to
 * correct when the contract moves.
 */

/** The envelope every list endpoint returns (ADR-008). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNext: boolean;
}

export interface UserDto {
  id: string;
  username: string;
  email: string;
  emailVerified: boolean;
  displayName: string;

  /** `Active`, `Disabled` or `Locked`, as the Platform spells it. */
  status: string;

  mustChangePassword: boolean;
  lastLoginAt: string | null;
  createdAt: string;
}

export interface AuditEventDto {
  id: string;
  occurredAt: string;
  application: string;
  module: string;
  action: string;

  /** `Success`, `Failure` or `Denied`. */
  result: string;

  resourceType: string | null;
  resourceId: string | null;
  actorUserId: string | null;

  /** Denormalized, so the trail stays readable after a rename. */
  actorUsername: string | null;

  onBehalfOfUserId: string | null;
  ipAddress: string | null;
  correlationId: string | null;
  oldValue: string | null;
  newValue: string | null;
  metadata: string | null;
}

/** RFC 9457, as the Platform returns it. */
export interface ProblemResponse {
  code?: string;
  detail?: string;
  correlationId?: string;
  errors?: { code: string; message: string; field?: string }[];
}
