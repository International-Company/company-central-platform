// Load profile for the Company Central Platform.
//
// Run with k6:  k6 run tests/load/platform-load.js
//
//   BASE_URL      where the Platform is        (default http://localhost:5080)
//   CCP_USERNAME  an account with the usual administrative permissions
//   CCP_PASSWORD  its password
//
// ---------------------------------------------------------------------------
// The thresholds below are ARCHITECTURE.md section 24 turned into assertions.
// They were written there as "indicative targets, to be validated in Phase 20",
// with the note that budgets exist so a regression is a failed test rather than
// an opinion -- and a budget that lives only in prose is an opinion. k6 exits
// non-zero when a threshold is breached, so this is the mechanism that makes
// that sentence true.
//
// IT HAS NOT BEEN RUN AGAINST A REALISTIC DATA VOLUME. Recorded as debt rather
// than implied: the numbers a load test produces against an empty database are
// a measure of the framework, not of the Platform. It needs a seeded environment
// of a plausible size -- a few thousand employees, a populated audit trail --
// and that is a Phase 19 environment, which waits on the provider decision.
// ---------------------------------------------------------------------------

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5080';
const USERNAME = __ENV.CCP_USERNAME;
const PASSWORD = __ENV.CCP_PASSWORD;

// Separate trends per class of work, because one aggregate p95 across
// authentication and audit search says nothing about either. The budgets differ
// by an order of magnitude and so should the measurements.
const authentication = new Trend('ccp_authentication', true);
const permissionCheck = new Trend('ccp_permission_check', true);
const listEndpoint = new Trend('ccp_list_endpoint', true);
const auditSearch = new Trend('ccp_audit_search', true);

export const options = {
  scenarios: {
    // A working day rather than a spike. Most of what this Platform serves is
    // people opening screens, so the shape that matters is sustained concurrent
    // reads with a trickle of writes -- not a thundering herd, which measures
    // the rate limiter.
    steady: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 20 },
        { duration: '2m', target: 20 },
        { duration: '30s', target: 0 },
      ],
      gracefulRampDown: '30s',
    },

    // Authentication on its own clock, and slowly.
    //
    // Signing in inside the main loop would spend the rate-limit budget --
    // authentication is partitioned by the account being targeted and allows ten
    // a minute -- so the measurement would become a measurement of the limiter.
    // Six a minute for three minutes stays well inside it and still gives the
    // p95 something to work with.
    //
    // It has a scenario at all because the alternative is an exported function
    // nothing calls, feeding a threshold on a metric that never receives a
    // sample. This project has shipped that exact shape twice -- two declared
    // instruments with no caller, both with alerts written against them, both
    // reading permanently healthy -- and is not shipping it a third time.
    authentication: {
      executor: 'constant-arrival-rate',
      exec: 'authenticationProbe',
      rate: 6,
      timeUnit: '1m',
      duration: '3m',
      preAllocatedVUs: 2,
    },
  },

  thresholds: {
    // ARCHITECTURE.md section 24, verbatim.
    ccp_authentication: ['p(95)<300'],
    ccp_permission_check: ['p(95)<20'],
    ccp_list_endpoint: ['p(95)<400'],
    ccp_audit_search: ['p(95)<2000'],

    // Errors are a budget too. A run that met every latency target while
    // failing one request in twenty has not met anything -- and a rate limiter
    // rejecting under load would show up here rather than as slowness.
    http_req_failed: ['rate<0.01'],
  },
};

export function setup() {
  if (!USERNAME || !PASSWORD) {
    throw new Error(
      'Set CCP_USERNAME and CCP_PASSWORD. This test signs in as a real account; ' +
        'it does not carry credentials of its own, and no credential belongs in ' +
        'this file.',
    );
  }

  const response = http.post(
    `${BASE}/api/v1/auth/login`,
    JSON.stringify({ username: USERNAME, password: PASSWORD }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  if (response.status !== 200) {
    throw new Error(`Sign-in failed with ${response.status}. Nothing else can run.`);
  }

  return { token: response.json('accessToken') };
}

export default function (data) {
  const authorized = {
    headers: {
      Authorization: `Bearer ${data.token}`,
      'Content-Type': 'application/json',
    },
  };

  group('permission check', () => {
    // The hottest path in the Platform: every request that reaches an endpoint
    // resolves the caller's permissions first. The budget is 20ms because it is
    // paid on top of everything else, by everybody, all day.
    const response = http.get(`${BASE}/api/v1/me/permissions`, authorized);

    permissionCheck.add(response.timings.duration);
    check(response, { 'permissions resolved': (r) => r.status === 200 });
  });

  group('list endpoints', () => {
    for (const path of ['/api/v1/users?page=1&pageSize=25', '/api/v1/roles']) {
      const response = http.get(`${BASE}${path}`, authorized);

      listEndpoint.add(response.timings.duration);
      check(response, { 'list returned': (r) => r.status === 200 });
    }
  });

  group('audit search', () => {
    // The slowest read the Platform offers, and deliberately budgeted an order
    // of magnitude looser: it is a monthly-partitioned table that grows for ever
    // and it is searched by a person who is investigating something, not by a
    // screen somebody is waiting on.
    const response = http.get(
      `${BASE}/api/v1/audit/events?page=1&pageSize=25`,
      authorized,
    );

    auditSearch.add(response.timings.duration);
    check(response, { 'audit searched': (r) => r.status === 200 });
  });

  // A person reading a screen, not a machine in a loop. Without this the test
  // measures how fast k6 can saturate a socket.
  sleep(1);
}

// The `authentication` scenario above runs this. It is a real sign-in, hashed
// with the production Argon2id parameters, which is most of the 300ms budget and
// the reason that budget is what it is.
export function authenticationProbe() {
  const response = http.post(
    `${BASE}/api/v1/auth/login`,
    JSON.stringify({ username: USERNAME, password: PASSWORD }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  authentication.add(response.timings.duration);
  check(response, { 'signed in': (r) => r.status === 200 });
}
