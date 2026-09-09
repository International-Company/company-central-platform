#!/usr/bin/env bash
#
# Regenerates contracts/platform-api.json from the API's own endpoints.
#
# The generator starts the application to read its route table, and the
# composition root refuses to start without a connection string — it has no way
# to know it is being asked for a document rather than for service. The one
# below is never connected to: migrations are off, no health check fires, and
# the document is built from endpoint metadata rather than from data.
#
# Run this after changing any endpoint or DTO, then commit the result. CI runs
# the same thing and fails if the committed contract has drifted.
set -euo pipefail

cd "$(dirname "$0")/.."

CCP_ConnectionStrings__Platform="Host=localhost;Port=5432;Database=ccp_contract_generation;Username=contract;Password=contract" \
  dotnet build src/Host/CCP.Api.Host/CCP.Api.Host.csproj \
    --no-incremental \
    -p:GenerateApiContract=true \
    -v quiet --nologo

# The generator names the file after the assembly. Renamed to say what it is:
# this is the Platform's API contract, not one project's build output.
mv -f contracts/CCP.Api.Host.json contracts/platform-api.json

echo "contracts/platform-api.json regenerated."
