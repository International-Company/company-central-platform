# =============================================================================
# Company Central Platform — frontend image
#
# Multi-stage. The runtime carries the traced standalone server and nothing
# else: no source, no node_modules, no build tooling — a much smaller surface
# than a single-stage build.
#
# Built from the repository root, like the API image, so both services share
# one context and one set of ignore rules:
#   docker build -f frontend/Dockerfile -t ccp-frontend .
# =============================================================================

# --- Dependencies ------------------------------------------------------------
FROM node:24-alpine AS deps
WORKDIR /app

# The lockfile alone first, so this layer is reused whenever only source
# changed. `npm ci` installs exactly what the lockfile pins - never a resolved
# range, which is how a build stops being reproducible.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

# --- Build -------------------------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY frontend/ ./

# Typecheck and lint run in CI, not here. A container build that also lints
# fails for two unrelated reasons and takes twice as long to tell you which.
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

# --- Runtime -----------------------------------------------------------------
FROM node:24-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=8080 \
    HOSTNAME=0.0.0.0

# An unprivileged user. The node image provides `node` (uid 1000).
USER node

# `standalone` holds the server and its traced dependencies; `static` and
# `public` are served from disk and are not traced into it.
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static

EXPOSE 8080

# No secret is baked in. PLATFORM_API_URL and anything else arrive as
# environment variables at deploy time (ARCHITECTURE.md §12.7).
CMD ["node", "server.js"]
