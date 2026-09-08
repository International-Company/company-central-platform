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

# Copy the project graph first so restore is cached independently of source
# changes. Editing a .cs file then costs a compile, not a full restore.
COPY global.json Directory.Build.props ./
COPY src/Kernel/CCP.Kernel/*.csproj                 src/Kernel/CCP.Kernel/
COPY src/Kernel/CCP.Kernel.Application/*.csproj     src/Kernel/CCP.Kernel.Application/
COPY src/Kernel/CCP.Kernel.Infrastructure/*.csproj  src/Kernel/CCP.Kernel.Infrastructure/
COPY src/Kernel/CCP.Kernel.Api/*.csproj             src/Kernel/CCP.Kernel.Api/
COPY src/Host/CCP.Api.Host/*.csproj                 src/Host/CCP.Api.Host/

RUN dotnet restore src/Host/CCP.Api.Host/CCP.Api.Host.csproj

COPY src/ src/

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
