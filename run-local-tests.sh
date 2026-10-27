#!/usr/bin/env bash
# Run the local game-assembly-dependent probes for RimWorld 1.6 and 1.7.
set -euo pipefail

usage() {
  cat >&2 <<EOF
Usage: $0 <RimWorld-1.6-Managed> <Prepatcher-Assemblies> <Harmony-1.6-Assemblies> [mod-root]

Runs the 1.6 ritual-prefix and reflection-only Mono probes plus the 1.7
assembly-metadata audit. All inputs must already exist; the mod root must
contain built 1.6/ and 1.7/ assemblies. Docker and Python 3 are required.

Paths may be absolute or relative to the current directory. The default mod
root is this repository. Invoke from any working directory.
EOF
  exit "${1:-2}"
}

if [[ "$#" -eq 1 && ( "$1" == "--help" || "$1" == "-h" ) ]]; then
  usage 0
fi

if [[ "$#" -lt 3 || "$#" -gt 4 ]]; then
  usage
fi

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR"

resolve_dir() {
  local label="$1" path="$2"
  if [[ ! -d "$path" ]]; then
    echo "Required $label directory does not exist: $path" >&2
    exit 1
  fi
  (cd -- "$path" && pwd)
}

GAME_MANAGED_DIR="$(resolve_dir 'RimWorld 1.6 Managed' "$1")"
PREPATCHER_DIR="$(resolve_dir 'Prepatcher Assemblies' "$2")"
HARMONY_DIR="$(resolve_dir 'Harmony 1.6 Assemblies' "$3")"
MOD_ROOT="$(resolve_dir 'mod root' "${4:-$REPO_ROOT}")"

required_files=(
  "$GAME_MANAGED_DIR/Assembly-CSharp.dll"
  "$GAME_MANAGED_DIR/UnityEngine.CoreModule.dll"
  "$GAME_MANAGED_DIR/UnityEngine.InputLegacyModule.dll"
  "$GAME_MANAGED_DIR/netstandard.dll"
  "$PREPATCHER_DIR/0PrepatcherAPI.dll"
  "$HARMONY_DIR/0Harmony.dll"
  "$MOD_ROOT/1.6/Assemblies/MultiplayerLoader.dll"
  "$MOD_ROOT/1.6/AssembliesCustom/Multiplayer.dll"
  "$MOD_ROOT/1.6/AssembliesCustom/MultiplayerCommon.dll"
  "$MOD_ROOT/1.7/Assemblies/MultiplayerLoader.dll"
  "$MOD_ROOT/1.7/AssembliesCustom/Multiplayer.dll"
  "$MOD_ROOT/1.7/AssembliesCustom/MultiplayerCommon.dll"
)
for file in "${required_files[@]}"; do
  if [[ ! -f "$file" ]]; then
    echo "Required local test input is missing: $file" >&2
    exit 1
  fi
done

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required by the RimWorld 1.6 ritual probe, but was not found in PATH." >&2
  exit 1
fi

python3 - "$REPO_ROOT/global.json" <<'PY'
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
PY

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is required to run these local probes, but was not found in PATH." >&2
  exit 1
fi

echo "== RimWorld 1.6 ritual prefix probe =="
bash "$SCRIPT_DIR/Source/RuntimeTests/run-ritual-probe.sh" \
  "$GAME_MANAGED_DIR" "$PREPATCHER_DIR" "$HARMONY_DIR" "$MOD_ROOT"

echo "== RimWorld 1.6 reflection-only probe =="
bash "$SCRIPT_DIR/Source/RuntimeTests/run-reflection-only-probe.sh" \
  "$GAME_MANAGED_DIR" "$MOD_ROOT"

echo "== RimWorld 1.7 metadata audit =="
bash "$SCRIPT_DIR/Source/RuntimeTests/audit-rimworld-1.7-reference.sh" \
  "$MOD_ROOT"

echo "All local runtime probes passed."
