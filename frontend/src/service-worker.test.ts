import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { createContext, runInContext } from 'node:vm';
import { describe, expect, it } from 'vitest';

/**
 * What the service worker is allowed to keep.
 *
 * **This is the one file in the portal where a mistake leaks data between
 * people.** Everything this application serves is rendered for one signed-in
 * person; a service-worker cache survives sign-out, survives a different
 * person signing in on the same machine, and answers from disk before the
 * network is consulted. One over-broad rule and the second person to use a
 * shared laptop is reading the first person's screens, with nothing anywhere
 * saying so.
 *
 * The decision is a pure function for exactly this reason. A test that has to
 * boot a service worker is a test nobody runs.
 */

const origin = 'https://platform.example';

type Worker = {
  cachePolicy: (request: { method: string; mode?: string }, url: URL) => string;
  offlineFallback: (request: unknown) => Promise<unknown>;
};

/** The real file, loaded and run the way a browser would. */
function load(stubs: { fetch?: unknown; caches?: unknown } = {}): Worker {
  const source = readFileSync(
    join(import.meta.dirname, '..', 'public', 'sw.js'),
    'utf8',
  );

  const self: Record<string, unknown> = {
    location: { origin },
    addEventListener: () => undefined,
    skipWaiting: () => undefined,
    clients: { claim: () => undefined },
  };

  // Run in a context holding what a worker scope holds, and nothing else.
  // Anything the file reaches for at load time that a worker would not have
  // throws here, which is itself worth knowing: this file has no window and no
  // document, and code written as though it did fails only once installed.
  const scope: Record<string, unknown> = {
    self,
    URL,
    caches: stubs.caches,
    fetch: stubs.fetch,
  };

  createContext(scope);
  runInContext(source, scope);

  const cachePolicy = self['__cachePolicy'];
  const offlineFallback = self['__networkThenOfflinePage'];

  expect(typeof cachePolicy, 'sw.js no longer exposes its policy').toBe('function');
  expect(typeof offlineFallback, 'sw.js no longer exposes its fallback').toBe('function');

  return { cachePolicy, offlineFallback } as Worker;
}

const decide = load().cachePolicy;

/** A page the person navigated to. */
const navigation = { method: 'GET', mode: 'navigate' };

/** A file the page asked for. */
const asset = { method: 'GET', mode: 'no-cors' };

function forPath(request: typeof navigation, path: string) {
  return decide(request, new URL(path, origin));
}

describe('anything the signed-in person is looking at', () => {
  it('is never stored, whatever the path', () => {
    // Every route handler in this application either exchanges a token or
    // returns one person's data, and both are the reason the application
    // exists at all.
    const routes = [
      '/api/me/permissions',
      '/api/me/tasks?page=1',
      '/api/users',
      '/api/users/1234/roles',
      '/api/auth/sign-out',
      '/api/documents/1/content',
      '/api/security/events',
      '/api',
    ];

    for (const route of routes) {
      expect(forPath(asset, route), route).toBe('passthrough');
      expect(forPath(navigation, route), route).toBe('passthrough');
    }
  });

  it('is fetched fresh every time a page is opened', () => {
    // Network first and never written. A cached dashboard would be somebody's
    // approvals queue, served to whoever opens the laptop next.
    for (const page of ['/ar/dashboard', '/en/users', '/ar/tasks', '/ar/audit', '/']) {
      expect(forPath(navigation, page), page).toBe('network-then-offline-page');
    }
  });

  it('is not cached because a path merely looks harmless', () => {
    // A path that starts with something cacheable is not cacheable. This is
    // the shape the mistake takes: a rule written for /_next that matches
    // /_next-door, or one for /icons that matches /icons-of-people.
    expect(forPath(asset, '/_next-door/secret')).toBe('passthrough');
    expect(forPath(asset, '/iconsomething')).toBe('passthrough');
    expect(forPath(asset, '/apifoo')).toBe('passthrough');
  });
});

describe('what it does keep', () => {
  it('is build output, whose names carry a content hash', () => {
    expect(forPath(asset, '/_next/static/chunks/main-a1b2c3.js')).toBe('cache-first');
    expect(forPath(asset, '/_next/static/media/plex-sans.woff2')).toBe('cache-first');
  });

  it('is the icons and the page shown when the network is gone', () => {
    expect(forPath(asset, '/icons/icon-192.png')).toBe('cache-first');
    expect(forPath(asset, '/offline.html')).toBe('cache-first');
  });

  it('is never the manifest, which names the start page', () => {
    // Small, revalidated, and wrong for a long time if it goes stale: a
    // home-screen icon opening a page that moved two deployments ago.
    expect(forPath(asset, '/ar/manifest.webmanifest')).toBe('passthrough');
  });
});

describe('a request that changes something', () => {
  it('never reaches the cache at all', () => {
    for (const method of ['POST', 'PUT', 'DELETE', 'PATCH']) {
      expect(decide({ method }, new URL('/_next/static/chunks/main.js', origin)), method)
        .toBe('passthrough');
    }
  });
});

describe('another origin', () => {
  it('is left alone', () => {
    expect(decide(asset, new URL('https://elsewhere.example/_next/static/x.js')))
      .toBe('passthrough');
  });
});

describe('when the network is not there', () => {
  it('answers a page with the offline page', async () => {
    // Proved against a server that was actually stopped, and kept honest here:
    // taking the server away in the middle of a suite is not something CI can
    // do, and without this the fallback would be exercised by nothing.
    const offlinePage = { body: 'the offline page' };

    const worker = load({
      fetch: () => Promise.reject(new TypeError('Failed to fetch')),
      caches: {
        match: (key: string) => Promise.resolve(key === '/offline.html' ? offlinePage : undefined),
      },
    });

    expect(await worker.offlineFallback({ method: 'GET', mode: 'navigate' })).toBe(offlinePage);
  });

  it('does not put the page it did reach into the cache', async () => {
    // The whole point. A page fetched successfully is one person's screen, and
    // writing it anywhere is how the next person to use the machine reads it.
    const page = { body: 'somebody signed-in dashboard' };

    const worker = load({
      fetch: () => Promise.resolve(page),
      caches: {
        match: () => Promise.resolve(undefined),
        open: () => {
          throw new Error('the fallback opened a cache while handling a page');
        },
      },
    });

    expect(await worker.offlineFallback({ method: 'GET', mode: 'navigate' })).toBe(page);
  });
});
