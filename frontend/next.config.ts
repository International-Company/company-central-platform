import createNextIntlPlugin from 'next-intl/plugin';
import type { NextConfig } from 'next';

const withNextIntl = createNextIntlPlugin('./src/i18n/request.ts');

const config: NextConfig = {
  reactStrictMode: true,

  // Traced dependencies only, in a self-contained server. The runtime image
  // then carries neither node_modules nor the source, which keeps it small and
  // gives it much less to be vulnerable in.
  output: 'standalone',

  // No `X-Powered-By`. It tells an attacker the stack and tells a user nothing.
  poweredByHeader: false,

  // Type and lint errors fail the build. A warning nobody has to fix is a
  // warning nobody fixes.
  typescript: { ignoreBuildErrors: false },
  eslint: { ignoreDuringBuilds: false },
};

export default withNextIntl(config);
