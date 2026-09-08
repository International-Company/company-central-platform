# =============================================================================
# Company Central Platform — API image
#
# Multi-stage: the SDK builds, a minimal runtime image runs. The runtime image
# carries no compiler, no source and no build tooling, so it presents a much
# smaller attack surface than a single-stage build.
#
# Build from the repository root:
#   docker build -f build/api.Dockerfile -t ccp-api .
# =============================================================================

# --- Build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# The whole source tree, then restore.
#
# An earlier version copied each .csproj individually so that restore could be
# cached independently of source edits. That list was written in Phase 1 and
# named five projects; by Phase 5 there were twenty. Restore skipped the ones it
# could not find - "Skipping project ... because it was not found", a warning,
# not an error - and publish then failed on the missing assets files. A list
# that must be edited every time a module is added is a list that will be wrong,
# and this one was wrong for four phases without anyone noticing, because the
# image was never actually built.
#
# Copying everything costs the restore cache on any source change. That is the
# right trade for a codebase that gains a module every phase or two: a slower
# build is a cost, a build that silently omits projects is a defect.
#
# .editorconfig is not optional here. Directory.Build.props turns warnings into
# errors, and the analyzer suppressions that make that survivable - each with a
# written justification - live in .editorconfig. Without it the build fails on
# CA1716 in the kernel.
COPY global.json Directory.Build.props .editorconfig ./
COPY src/ src/

RUN dotnet restore src/Host/CCP.Api.Host/CCP.Api.Host.csproj

RUN dotnet publish src/Host/CCP.Api.Host/CCP.Api.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# --- Runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as an unprivileged user. The base image provides `app` (uid 1654).
USER $APP_UID

COPY --from=build --chown=$APP_UID /app .

# The container listens on a non-privileged port; TLS is terminated at the edge
# (ARCHITECTURE.md §20.2).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

EXPOSE 8080

# No secret is baked into the image. Configuration and secrets arrive as
# environment variables from the cloud secret manager (ARCHITECTURE.md §12.7).

# No HEALTHCHECK instruction: the aspnet runtime image ships without curl or
# wget, and adding one purely to probe the container is needless attack surface.
# The container platform probes /health/live over HTTP instead
# (ARCHITECTURE.md §22.4), which is what actually gates traffic.

ENTRYPOINT ["dotnet", "CCP.Api.Host.dll"]
