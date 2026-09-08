import createMiddleware from 'next-intl/middleware';
import { routing } from '@/i18n/routing';

export default createMiddleware(routing);

export const config = {
  // Everything except the BFF route handlers, Next's internals and static
  // files. The route handlers must not be locale-prefixed: they are an
  // interface for this application's own code, not pages a person reads.
  matcher: ['/((?!api|_next|_vercel|.*[.].*).*)'],
};
