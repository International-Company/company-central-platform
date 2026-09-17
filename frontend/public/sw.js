/*
 * The service worker.
 *
 * **It caches almost nothing, deliberately.** This application exists to keep
 * the Platform's tokens out of the browser: every screen is rendered per
 * request for one signed-in person, and `platform-client.ts` sets
 * `cache: 'no-store'` on every call for the same reason -- "a cached response
 * here would be one user's data served to another". A service worker is a
 * cache that survives sign-out, survives a different person signing in on the
 * same machine, and answers from disk before the network is consulted. Putting
 * an authenticated response in it would undo the whole design, quietly, and on
 * a shared machine the second person would never know.
 *
 * So the policy is narrow and stated once, in `cachePolicy` below:
 *
 *   - anything under /api        the worker does not touch at all
 *   - /_next/static/...          cache first; the filenames carry a content
 *                                hash, so a cached one can never be stale
 *   - a page the person asks for network first, never stored, and the offline
 *                                page if the network is not there
 *   - everything else            straight to the network
 *
 * What this buys is a real installed application -- its own window, its own
 * icon, its own launch -- that starts instantly because the code is already on
 * disk, and that says something honest when there is no connection instead of
 * showing the browser's dinosaur. It does not buy working offline. Nothing in
 * this Platform can be read without the Platform, and pretending otherwise
 * would mean showing somebody yesterday's approvals.
 *
 * `cachePolicy` is attached to `self` so it can be tested on its own. It is
 * the only part of this file where a mistake would leak data, and a test that
 * has to boot a service worker is a test nobody runs.
 */

// Bumped when the shape of what is cached changes. Activation deletes every
// cache that is not this one, so a worker released with a bug takes its cache
// with it when it goes.
const CACHE = 'ccp-static-v1';

const OFFLINE_PAGE = '/offline.html';

/**
 * What to do with a request. Pure, and exported for the tests.
 *
 * `passthrough` means the worker declines to handle it at all: the browser
 * makes the request exactly as it would with no worker installed.
 */
function cachePolicy(request, url) {
  // Only ever GET. A POST is a change, and a change is never answered from a
  // cache.
  if (request.method !== 'GET') {
    return 'passthrough';
  }

  // Another origin. Not ours to cache, and the Platform API is reached through
  // this application's own routes anyway.
  if (url.origin !== self.location.origin) {
    return 'passthrough';
  }

  // The BFF. Every one of these is either a token exchange or one person's
  // data, and both are the reason this application exists.
  if (url.pathname === '/api' || url.pathname.startsWith('/api/')) {
    return 'passthrough';
  }

  // The manifest is generated per locale and names the signed-in start page.
  // Small, and not worth being wrong about.
  if (url.pathname.endsWith('/manifest.webmanifest')) {
    return 'passthrough';
  }

  // Build output. Every filename carries a content hash, so a cached copy is
  // the file it claims to be for ever, and a new build asks for new names.
  if (url.pathname.startsWith('/_next/static/')) {
    return 'cache-first';
  }

  // The offline page and the icons: fixed files that have to be there at the
  // moment the network is not.
  if (url.pathname === OFFLINE_PAGE || url.pathname.startsWith('/icons/')) {
    return 'cache-first';
  }

  // A page. Always fetched; never stored. If the network is not there, the
  // person gets a page that says so rather than the browser's error.
  if (request.mode === 'navigate') {
    return 'network-then-offline-page';
  }

  return 'passthrough';
}

// Exposed for the tests, which run this file in a sandbox and call these
// directly. Booting a real service worker to find out what it caches is a test
// nobody runs, and this is the file where not knowing is expensive.
self.__cachePolicy = cachePolicy;
self.__networkThenOfflinePage = (request) => networkThenOfflinePage(request);

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE).then((cache) => cache.addAll([OFFLINE_PAGE])),
  );

  // The new worker takes over without waiting for every tab to close. What it
  // replaces never held anything but build output, so there is no half-updated
  // state to protect.
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) =>
        Promise.all(names.filter((name) => name !== CACHE).map((name) => caches.delete(name))),
      )
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  const policy = cachePolicy(event.request, url);

  if (policy === 'passthrough') {
    return;
  }

  if (policy === 'cache-first') {
    event.respondWith(cacheFirst(event.request));

    return;
  }

  if (policy === 'network-then-offline-page') {
    event.respondWith(networkThenOfflinePage(event.request));
  }
});

async function cacheFirst(request) {
  const cached = await caches.match(request);

  if (cached) {
    return cached;
  }

  const response = await fetch(request);

  // Only a plain success is worth keeping. An opaque or partial response
  // stored here would be served back as though it were the file.
  if (response.ok && response.type === 'basic') {
    const cache = await caches.open(CACHE);

    await cache.put(request, response.clone());
  }

  return response;
}

async function networkThenOfflinePage(request) {
  try {
    return await fetch(request);
  } catch {
    const offline = await caches.match(OFFLINE_PAGE);

    return (
      offline
      ?? new Response('', { status: 503, statusText: 'Offline' })
    );
  }
}

/**
 * Sign-out empties the cache.
 *
 * It holds nothing private, and it is still the right thing to do: somebody
 * handing a laptop to a colleague should leave nothing of their session
 * behind, and "nothing private is in there" is a claim that has to stay true
 * through every future change to this file.
 */
self.addEventListener('message', (event) => {
  if (event.data === 'sign-out') {
    event.waitUntil(caches.keys().then((names) => Promise.all(names.map((name) => caches.delete(name)))));
  }
});
