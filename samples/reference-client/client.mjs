/**
 * A working Company Central Platform client, in about a hundred lines.
 *
 * Node 20+, no dependencies. Copy it, translate it into whatever your system is
 * written in, or read it and write your own — it exists to show the four things
 * every integration gets wrong on the first attempt:
 *
 *   1. Caching the token instead of fetching one per request.
 *   2. Refreshing *before* expiry rather than after a 401.
 *   3. Honouring 429 instead of retrying immediately.
 *   4. Matching on the error `code` rather than on the message.
 *
 * Run it:
 *
 *   CCP_URL=https://… \
 *   CCP_CLIENT_ID=ccp_… \
 *   CCP_CLIENT_SECRET=ccps_… \
 *   node client.mjs
 *
 * The credentials come from the environment. Not from a file in your repository,
 * and not from an argument that lands in your shell history.
 */

const baseUrl = process.env.CCP_URL ?? 'http://localhost:5080';
const clientId = process.env.CCP_CLIENT_ID;
const clientSecret = process.env.CCP_CLIENT_SECRET;

if (!clientId || !clientSecret) {
  console.error('Set CCP_CLIENT_ID and CCP_CLIENT_SECRET.');
  process.exit(1);
}

/**
 * The token, and when to stop trusting it.
 *
 * Refreshed sixty seconds before it actually expires. Waiting for a 401 works
 * until a request is in flight across the boundary, and then it fails once in a
 * way nobody can reproduce.
 */
let token = null;
let expiresAt = 0;

async function getToken({ onBehalfOf } = {}) {
  // A delegated token is a different token, so it is not cached alongside the
  // application's own. Caching them together is how a nightly job ends up
  // acting as whoever last triggered something.
  if (!onBehalfOf && token && Date.now() < expiresAt) {
    return token;
  }

  const body = new URLSearchParams({
    grant_type: 'client_credentials',
    client_id: clientId,
    client_secret: clientSecret,
  });

  if (onBehalfOf) {
    body.set('on_behalf_of', onBehalfOf);
  }

  const response = await fetch(`${baseUrl}/api/v1/oauth/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));

    // AUTHZ.INVALID_CLIENT covers a wrong secret, an unknown client id, a
    // revoked credential and a disabled application — deliberately, so an
    // attacker learns nothing. It means: check your credentials, then ask
    // whether somebody revoked them.
    throw new Error(`Token request failed (${response.status}): ${problem.code ?? 'unknown'}`);
  }

  const issued = await response.json();

  if (!onBehalfOf) {
    token = issued.access_token;
    expiresAt = Date.now() + (issued.expires_in - 60) * 1000;
  }

  return issued.access_token;
}

/**
 * One call, with the two failures worth handling built in.
 */
async function call(path, { method = 'GET', body, onBehalfOf, attempt = 1 } = {}) {
  const accessToken = await getToken({ onBehalfOf });

  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });

  // Rate limited. Retry-After is not advice — retrying immediately is refused
  // again and makes the queue behind you longer.
  if (response.status === 429 && attempt <= 3) {
    const wait = Number(response.headers.get('retry-after') ?? 5) * 1000;

    await new Promise((resolve) => setTimeout(resolve, wait));

    return call(path, { method, body, onBehalfOf, attempt: attempt + 1 });
  }

  // The endpoint is going away. One log line is enough, and it is the difference
  // between a planned migration and an outage.
  if (response.headers.has('sunset')) {
    console.warn(
      `[deprecated] ${path} stops working on ${response.headers.get('sunset')}. ` +
        `Successor: ${response.headers.get('link') ?? 'see docs/api/versioning.md'}`,
    );
  }

  if (response.status === 204) {
    return null;
  }

  const payload = await response.json().catch(() => null);

  if (!response.ok) {
    const error = new Error(`${method} ${path} failed: ${response.status}`);

    // Match on this, never on a message: messages are translated into Arabic
    // and English and are reworded freely.
    error.code = payload?.code;

    // Quote this when reporting a problem. It retrieves the log line, the trace
    // and the audit record for that exact request.
    error.correlationId = payload?.correlationId;

    throw error;
  }

  return payload;
}

// ---------------------------------------------------------------------------
// What an integration actually does
// ---------------------------------------------------------------------------

/** Declare this system's permissions. Complete list, on every startup. */
async function declarePermissions(permissions) {
  return await call('/api/v1/applications/permissions', {
    method: 'PUT',
    body: { permissions },
  });
}

/** Ask what somebody may do — and, more usefully, over what. */
async function check(userId, permission) {
  return await call('/api/v1/authorization/check', {
    method: 'POST',
    body: { userId, permission },
  });
}

/** Everything filed against one of this system's own records. */
async function documentsFor(resourceType, resourceId) {
  return await call(
    `/api/v1/resources/${encodeURIComponent(resourceType)}/${encodeURIComponent(resourceId)}/documents`,
  );
}

async function main() {
  console.log('Authenticating…');
  await getToken();
  console.log('  got a token, cached until', new Date(expiresAt).toISOString());

  try {
    const result = await declarePermissions([
      { name: 'sample.things.view', description: 'See things' },
      { name: 'sample.things.approve', description: 'Approve a thing' },
    ]);

    console.log('Declared permissions:', result);
  } catch (error) {
    // A permission outside your own namespace is refused, and that refusal is
    // the mechanism keeping one system out of another's resources.
    console.log('Declaration refused:', error.code, error.correlationId ?? '');
  }

  try {
    const documents = await documentsFor('sample-record', 'SR-0001');
    console.log(`Documents filed against SR-0001: ${documents.length}`);
  } catch (error) {
    console.log('Documents unavailable:', error.code);
  }
}

await main();

export { call, check, declarePermissions, documentsFor, getToken };
