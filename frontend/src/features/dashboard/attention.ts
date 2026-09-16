import type {
  JobSummaryDto,
  OutboxDepthDto,
  WebhookSubscriptionDto,
} from '@/types/platform';

/**
 * What the dashboard should tell somebody, worked out from what was read.
 *
 * **The whole point of the screen.** It used to show four counts and four
 * permanent instructions, which meant it said exactly the same thing on a
 * Platform that was idle, a Platform three days behind on event delivery, and
 * a Platform whose setup had been finished a year earlier. A dashboard that
 * cannot change is a picture, and nobody reads it twice.
 *
 * So each condition here is a thing that is true *now* and stops being listed
 * the moment it stops being true. The setup steps are conditions as well: they
 * appear while the structure is empty and disappear once it is not.
 *
 * **A reading nobody was allowed to take produces nothing.** Every field is
 * nullable, and null means "not read" - either the caller lacks the permission
 * or the call failed. Silence is the only honest answer to that: reporting it
 * as healthy would be a lie, and reporting it as broken would send somebody to
 * investigate a screen they cannot open. This is separate code from the
 * rendering precisely so it can be tested against that case.
 */

/** How much a condition matters, which decides its order and its badge. */
export type AttentionLevel = 'act' | 'watch' | 'setup';

export interface AttentionItem {
  /** The message key under `dashboard.attention`. */
  key: string;

  level: AttentionLevel;

  /** Where the reader goes to deal with it, without the locale prefix. */
  path: string;

  /** Interpolated into the message. */
  values?: Record<string, number>;
}

/**
 * Everything the dashboard managed to read.
 *
 * Null throughout means the reading was not taken. Zero means it was taken and
 * the answer was zero, which is a different thing entirely.
 */
export interface Readings {
  unreadNotifications: number | null;
  mfaActive: boolean | null;
  outbox: OutboxDepthDto | null;
  jobs: readonly JobSummaryDto[] | null;
  subscriptions: readonly WebhookSubscriptionDto[] | null;
  highSeverityEvents: number | null;
  users: number | null;
  employees: number | null;
  units: number | null;
}

/**
 * How long the outbox may be behind before anybody is told.
 *
 * Events are written with the change that caused them and delivered afterwards,
 * so a queue with something in it is the system working, not the system
 * failing. Five minutes is late enough that a person looking would find a cause.
 */
const behindAfterSeconds = 300;

const order: Record<AttentionLevel, number> = { act: 0, watch: 1, setup: 2 };

export function attentionItems(readings: Readings): AttentionItem[] {
  const items: AttentionItem[] = [];

  // Nothing is delivered twice, and a dead letter is never retried again. It is
  // the one condition here that has already lost something.
  if (readings.outbox && count(readings.outbox.deadLettered) > 0) {
    items.push({
      key: 'deadLettered',
      level: 'act',
      path: '/operations',
      values: { count: count(readings.outbox.deadLettered) },
    });
  }

  if (readings.outbox) {
    const age = readings.outbox.oldestPendingAgeSeconds;
    const seconds = age === null || age === undefined ? 0 : Number(age);

    if (count(readings.outbox.pending) > 0 && seconds >= behindAfterSeconds) {
      items.push({
        key: 'outboxBehind',
        level: 'act',
        path: '/operations',
        values: {
          count: count(readings.outbox.pending),
          minutes: Math.max(1, Math.round(seconds / 60)),
        },
      });
    }
  }

  if (readings.jobs) {
    // A job that failed outright, and a job that succeeded on one instance
    // while failing on another - which looks healthy in a list of outcomes and
    // is not.
    const failing = readings.jobs.filter(
      (job) => job.lastOutcome === 'Failed' || count(job.failingInstances) > 0,
    ).length;

    if (failing > 0) {
      items.push({
        key: 'failingJobs',
        level: 'act',
        path: '/operations',
        values: { count: failing },
      });
    }
  }

  if (readings.subscriptions) {
    const suspended = readings.subscriptions.filter(
      (subscription) => subscription.suspendedAt !== null && subscription.suspendedAt !== undefined,
    ).length;

    if (suspended > 0) {
      items.push({
        key: 'suspendedSubscriptions',
        level: 'act',
        path: '/integrations',
        values: { count: suspended },
      });
    }
  }

  if (readings.highSeverityEvents !== null && readings.highSeverityEvents > 0) {
    items.push({
      key: 'securityEvents',
      level: 'act',
      path: '/security',
      values: { count: readings.highSeverityEvents },
    });
  }

  // The reader's own account. Granting a role demands recent proof of identity,
  // so an administrator without a second factor is an administrator who will be
  // refused at the moment they try to do their job.
  if (readings.mfaActive === false) {
    items.push({ key: 'mfaOff', level: 'act', path: '/security' });
  }

  if (readings.unreadNotifications !== null && readings.unreadNotifications > 0) {
    items.push({
      key: 'unreadNotifications',
      level: 'watch',
      path: '/notifications',
      values: { count: readings.unreadNotifications },
    });
  }

  // Setup, in the order the Platform enforces: a unit before an employee, an
  // employee before the account that belongs to them.
  if (readings.units === 0) {
    items.push({ key: 'noUnits', level: 'setup', path: '/organization' });
  }

  if (readings.units !== null && readings.units > 0 && readings.employees === 0) {
    items.push({ key: 'noEmployees', level: 'setup', path: '/employees' });
  }

  if (readings.users !== null && readings.users <= 1) {
    items.push({ key: 'oneUser', level: 'setup', path: '/users' });
  }

  // Stable within a level: the declarations above are already in the order a
  // person should work through them.
  return items.sort((first, second) => order[first.level] - order[second.level]);
}

/** The contract types an int64 as number or string, so every count is widened. */
function count(value: number | string | undefined | null): number {
  return value === null || value === undefined ? 0 : Number(value);
}
