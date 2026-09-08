import createNextIntlPlugin from 'next-intl/plugin';
import type { NextConfig } from 'next';

const withNextIntl = createNextIntlPlugin('./src/i18n/request.ts');

const config: NextConfig = {
  reactStrictMode: true,

  // No `X-Powered-By`. It tells an attacker the stack and tells a user nothing.
  poweredByHeader: false,

  // Type and lint errors fail the build. A warning nobody has to fix is a
  // warning nobody fixes.
  typescript: { ignoreBuildErrors: false },
  eslint: { ignoreDuringBuilds: false },
};

export default withNextIntl(config);
