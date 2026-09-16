import { describe, expect, it } from 'vitest';
import ar from '@/i18n/messages/ar.json';
import en from '@/i18n/messages/en.json';
import { attentionItems, type Readings } from './attention';

/**
 * What the dashboard says, worked out from what it read.
 *
 * The condition that matters most is the one nobody writes a test for: a
 * reading that was never taken. Half of these fields are null for an ordinary
 * employee, who holds none of the permissions the operational reads need, and
 * treating a null as a zero would tell them the Platform is in perfect health
 * on the strength of four questions they were not allowed to ask.
 */

/** Nothing read, nothing known. Each test turns on only what it is about. */
const nothing: Readings = {
  unreadNotifications: null,
  mfaActive: null,
  outbox: null,
  jobs: null,
  subscriptions: null,
  highSeverityEvents: null,
  users: null,
  employees: null,
  units: null,
};

function keys(readings: Partial<Readings>): string[] {
  return attentionItems({ ...nothing, ...readings }).map((item) => item.key);
}

describe('a reading that was not taken', () => {
  it('says nothing at all', () => {
    expect(attentionItems(nothing)).toEqual([]);
  });

  it('is not the same as a reading of zero', () => {
    // The distinction the whole module turns on. Zero units is a Platform
    // waiting to be set up; unknown units is a person without the permission
    // to ask, and telling them to build a structure they cannot see would be
    // an instruction they cannot follow.
    expect(keys({ units: 0 })).toEqual(['noUnits']);
    expect(keys({ units: null })).toEqual([]);
  });
});

describe('the operational conditions', () => {
  it('reports events that were given up on', () => {
    expect(
      keys({ outbox: { pending: 0, deadLettered: 3, oldestPendingAt: null, oldestPendingAgeSeconds: null } }),
    ).toEqual(['deadLettered']);
  });

  it('says nothing about a queue that is merely moving', () => {
    // Events are written with the change that caused them and delivered
    // afterwards, so a queue with something in it is the design working. Only
    // a queue that has stopped moving is worth a person's time.
    expect(
      keys({ outbox: { pending: 40, deadLettered: 0, oldestPendingAt: null, oldestPendingAgeSeconds: 12 } }),
    ).toEqual([]);

    expect(
      keys({ outbox: { pending: 40, deadLettered: 0, oldestPendingAt: null, oldestPendingAgeSeconds: 900 } }),
    ).toEqual(['outboxBehind']);
  });

  it('counts a job that fails on one instance of several', () => {
    // Its last outcome is a success, so a list of outcomes looks healthy while
    // one machine has been failing every run.
    const healthy = { job: 'a', lastOutcome: 'Succeeded', failingInstances: 0 };
    const partly = { job: 'b', lastOutcome: 'Succeeded', failingInstances: 1 };
    const failed = { job: 'c', lastOutcome: 'Failed', failingInstances: 0 };

    expect(keys({ jobs: [healthy] as never })).toEqual([]);

    const items = attentionItems({ ...nothing, jobs: [healthy, partly, failed] as never });

    expect(items.map((item) => item.key)).toEqual(['failingJobs']);
    expect(items[0]?.values).toEqual({ count: 2 });
  });

  it('counts only the suspended subscriptions', () => {
    const live = { id: '1', suspendedAt: null };
    const suspended = { id: '2', suspendedAt: '2026-09-15T10:00:00Z' };

    expect(keys({ subscriptions: [live] as never })).toEqual([]);
    expect(keys({ subscriptions: [live, suspended] as never })).toEqual(['suspendedSubscriptions']);
  });

  it('reports a second factor that is enrolled but not active as off', () => {
    expect(keys({ mfaActive: false })).toEqual(['mfaOff']);
    expect(keys({ mfaActive: true })).toEqual([]);
  });
});

describe('the setup conditions', () => {
  it('asks for a structure before it asks for employees', () => {
    // No employee can exist before a unit does, so telling somebody with no
    // units to add employees is an instruction the Platform would refuse.
    expect(keys({ units: 0, employees: 0 })).toEqual(['noUnits']);
    expect(keys({ units: 4, employees: 0 })).toEqual(['noEmployees']);
  });

  it('stops asking once the structure is there', () => {
    expect(keys({ units: 4, employees: 20, users: 20 })).toEqual([]);
  });

  it('counts the reader as the only account', () => {
    expect(keys({ users: 1 })).toEqual(['oneUser']);
    expect(keys({ users: 2 })).toEqual([]);
  });
});

describe('the order', () => {
  it('puts what must be acted on above what is merely worth knowing', () => {
    const items = attentionItems({
      ...nothing,
      units: 0,
      unreadNotifications: 5,
      mfaActive: false,
    });

    expect(items.map((item) => item.level)).toEqual(['act', 'watch', 'setup']);
  });
});

describe('every condition has words', () => {
  it('names a message that both catalogues carry', () => {
    // The page looks these up by a key it builds at runtime, which TypeScript
    // cannot check. A key with no message renders an error in place of the
    // sentence, and only on the day the condition first comes true.
    const everyCondition = attentionItems({
      unreadNotifications: 5,
      mfaActive: false,
      outbox: { pending: 9, deadLettered: 2, oldestPendingAt: null, oldestPendingAgeSeconds: 900 },
      jobs: [{ job: 'a', lastOutcome: 'Failed', failingInstances: 0 }] as never,
      subscriptions: [{ id: '1', suspendedAt: '2026-09-15T10:00:00Z' }] as never,
      highSeverityEvents: 1,
      users: 1,
      employees: 0,
      units: 0,
    });

    // Every branch in the module, so a condition added without a message fails
    // here rather than in front of somebody.
    expect(everyCondition).toHaveLength(9);

    for (const catalogue of [ar, en] as const) {
      const words = catalogue.dashboard as Record<string, unknown>;
      const attention = words['attention'] as Record<string, string>;
      const level = words['level'] as Record<string, string>;

      for (const item of everyCondition) {
        expect(attention[item.key], `${item.key} is missing`).toBeTypeOf('string');
        expect(level[item.level], `${item.level} is missing`).toBeTypeOf('string');
      }
    }
  });

  it('interpolates only what the message asks for', () => {
    // A message expecting {count} and given nothing renders the placeholder.
    for (const catalogue of [ar, en] as const) {
      const attention = (catalogue.dashboard as Record<string, unknown>)['attention'] as Record<
        string,
        string
      >;

      const items = attentionItems({
        ...nothing,
        outbox: { pending: 9, deadLettered: 2, oldestPendingAt: null, oldestPendingAgeSeconds: 900 },
        unreadNotifications: 3,
        mfaActive: false,
      });

      for (const item of items) {
        const message = attention[item.key] ?? '';

        // A name only where a name can be: immediately before a comma or the
        // closing brace. The first version of this matched the first word of
        // every plural branch as well, so "One event was given up on" read as
        // a placeholder called One.
        const wanted = [...message.matchAll(/\{(\w+)\s*[,}]/g)].map((match) => match[1]);
        const given = Object.keys(item.values ?? {});

        expect(new Set(wanted), item.key).toEqual(new Set(given));
      }
    }
  });
});
