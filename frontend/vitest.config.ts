import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),

      // Vite cannot resolve the real `server-only`: its exports are conditional
      // on React's `react-server` condition, which only Next's bundler
      // supplies. The stub keeps the import in the source — so the real build
      // still refuses a client import — while letting these modules be tested.
      'server-only': fileURLToPath(
        new URL('./src/test/server-only.ts', import.meta.url),
      ),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
