#!/usr/bin/env bash
# Audit built RimWorld 1.7 assemblies for target framework and references.
set -euo pipefail
PROBE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$PROBE_DIR/../.." && pwd)"
MOD_ROOT="${1:-$REPO_ROOT}"
GLOBAL_JSON="$REPO_ROOT/global.json"

if ! MOD_ROOT="$(cd -- "$MOD_ROOT" 2>/dev/null && pwd)"; then
  echo "Mod root directory does not exist: ${1:-$REPO_ROOT}" >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required to read the SDK version from $GLOBAL_JSON, but was not found in PATH." >&2
  exit 1
fi

SDK_VERSION="$(python3 - "$GLOBAL_JSON" <<'PY'
import json
import re
import sys

path = sys.argv[1]
try:
    with open(path, encoding="utf-8") as config_file:
        config = json.load(config_file)
except OSError as exc:
    raise SystemExit(f"Cannot read required SDK config {path}: {exc}")
except json.JSONDecodeError as exc:
    raise SystemExit(f"Malformed SDK config {path}: {exc}")

sdk = config.get("sdk") if isinstance(config, dict) else None
version = sdk.get("version") if isinstance(sdk, dict) else None
if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version):
    raise SystemExit(f"SDK config {path} must contain a valid sdk.version string")
print(version)
PY
)"

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
  -v "$GLOBAL_JSON":/tmp/global.json:ro \
  -w /tmp "mcr.microsoft.com/dotnet/sdk:$SDK_VERSION" bash -lc '
    set -euo pipefail
    mkdir -p /tmp/mp-reference-audit
    cp /probe/MetadataAudit.csproj /probe/MetadataAudit.cs /tmp/mp-reference-audit/
    dotnet --version
    dotnet run --project /tmp/mp-reference-audit/MetadataAudit.csproj --configuration Release -- /mod/1.7
  '
