#!/usr/bin/env bash
set -euo pipefail
PROBE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$PROBE_DIR/../.." && pwd)"
MOD_ROOT="${1:-$REPO_ROOT}"

for file in \
  "$MOD_ROOT/1.7/AssembliesCustom/Multiplayer.dll" \
  "$MOD_ROOT/1.7/AssembliesCustom/MultiplayerCommon.dll" \
  "$MOD_ROOT/1.7/Assemblies/MultiplayerLoader.dll"; do
  if [[ ! -f "$file" ]]; then
    echo "Required 1.7 audit input is missing: $file" >&2
    exit 1
  fi
done

docker run --rm \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -v "$MOD_ROOT":/mod:ro \
  -v "$PROBE_DIR":/probe:ro \
  -w /tmp mcr.microsoft.com/dotnet/sdk:10.0 bash -lc '
    set -euo pipefail
    mkdir -p /tmp/mp-reference-audit
    cp /probe/MetadataAudit.csproj /probe/MetadataAudit.cs /tmp/mp-reference-audit/
    dotnet run --project /tmp/mp-reference-audit/MetadataAudit.csproj --configuration Release -- /mod/1.7
  '
