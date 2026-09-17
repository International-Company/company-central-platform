'use client';

import { useEffect } from 'react';

/**
 * Registers the service worker, and only where it can do any good.
 *
 * **Not in development.** A worker that serves build output from a cache is
 * exactly wrong while the build output is changing every few seconds, and the
 * hours lost to "I changed it and nothing happened" are the reason most people
 * turn service workers off and never turn them back on.
 *
 * Registration is deliberately late: after the page has loaded, so fetching
 * and parsing the worker never competes with the screen the person is waiting
 * for. Nothing on any screen depends on it having registered.
 *
 * A failure is swallowed on purpose. The worker adds an installed application
 * and an honest page when the network is gone; it is not load-bearing, and an
 * error banner because a browser declined to register one would be reporting
 * our problem as theirs.
 */
export function ServiceWorker() {
  useEffect(() => {
    if (process.env.NODE_ENV !== 'production') {
      return;
    }

    if (!('serviceWorker' in navigator)) {
      return;
    }

    const register = () => {
      void navigator.serviceWorker
        .register('/sw.js', { scope: '/' })
        .catch(() => undefined);
    };

    if (document.readyState === 'complete') {
      register();

      return;
    }

    window.addEventListener('load', register);

    return () => window.removeEventListener('load', register);
  }, []);

  return null;
}
