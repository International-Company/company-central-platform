'use client';

import { createContext, useContext, useMemo } from 'react';
import type { ReactNode } from 'react';

/**
 * What the signed-in person may do, for deciding what to *show*.
 *
 * **This is user experience, not security** (ARCHITECTURE.md §14). Hiding a
 * button the user cannot use spares them a refusal they could not have
 * predicted; it does not stop anyone doing anything. Every one of these
 * decisions is made again, authoritatively, by the Platform on the request
 * itself — and the Platform's answer is the only one that counts.
 *
 * Worth being blunt about, because the failure mode is subtle: a team that
 * starts treating the hidden button as the control will eventually ship an
 * endpoint whose only protection was that no link pointed at it.
 */

export interface PermissionSet {
  /** Permission names in `<application>.<resource>.<action>` form. */
  granted: readonly string[];
}

const PermissionContext = createContext<PermissionSet>({ granted: [] });

export function PermissionProvider({
  granted,
  children,
}: {
  granted: readonly string[];
  children: ReactNode;
}) {
  const value = useMemo<PermissionSet>(() => ({ granted }), [granted]);

  return (
    <PermissionContext.Provider value={value}>
      {children}
    </PermissionContext.Provider>
  );
}

/**
 * Whether the caller holds a permission.
 *
 * Exact match only. No wildcard expansion here, deliberately: the Platform
 * decides what a grant implies, and a second interpretation in the browser would
 * be a second set of rules that drifts from the first — showing controls the
 * server then refuses, which is worse than showing none.
 */
export function usePermission(permission: string): boolean {
  const { granted } = useContext(PermissionContext);

  return granted.includes(permission);
}

/**
 * Renders its children only if the permission is held.
 *
 * A convenience for the common case, and no more authoritative than
 * `usePermission`.
 */
export function IfPermitted({
  permission,
  children,
}: {
  permission: string;
  children: ReactNode;
}) {
  return usePermission(permission) ? <>{children}</> : null;
}
