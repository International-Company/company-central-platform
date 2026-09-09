/**
 * A stand-in for the `server-only` package, for tests.
 *
 * The real one has no main entry point Vite can resolve — its exports are
 * conditional on React's `react-server` condition, which only Next's bundler
 * supplies. Aliasing it here lets a server-side module be unit tested without
 * weakening the guard: the import stays in the source, so a mistaken client
 * import still fails the real build.
 */
export {};
