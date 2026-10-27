#!/usr/bin/env bash
set -euo pipefail
if [[ "$#" -lt 3 || "$#" -gt 4 ]]; then
  echo "Usage: bash $0 <RimWorld-1.6-Managed> <Prepatcher-Assemblies> <Harmony-1.6-Assemblies> [mod-root]" >&2
  exit 2
fi

PROBE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$PROBE_DIR/../.." && pwd)"
GAME_MANAGED_DIR="$1"
PREPATCHER_DIR="$2"
HARMONY_DIR="$3"
MOD_ROOT="${4:-$REPO_ROOT}"

for file in \
  "$GAME_MANAGED_DIR/Assembly-CSharp.dll" \
  "$GAME_MANAGED_DIR/UnityEngine.CoreModule.dll" \
  "$PREPATCHER_DIR/0PrepatcherAPI.dll" \
  "$HARMONY_DIR/0Harmony.dll" \
  "$MOD_ROOT/1.6/Assemblies/MultiplayerLoader.dll" \
  "$MOD_ROOT/1.6/AssembliesCustom/Multiplayer.dll" \
  "$MOD_ROOT/1.6/AssembliesCustom/MultiplayerCommon.dll"; do
  if [[ ! -f "$file" ]]; then
    echo "Required 1.6 probe input is missing: $file" >&2
    exit 1
  fi
done

python3 - "$MOD_ROOT/1.6/AssembliesCustom/Multiplayer.dll" <<'PY'
from pathlib import Path
import sys

data = Path(sys.argv[1]).read_bytes()
if b".NETFramework,Version=v4.8" not in data or b".NETCoreApp,Version=v10.0" in data:
    raise SystemExit("The 1.6 probe requires a net48 Multiplayer.dll; build with RimWorldVersion=1.6.")
PY

mkdir -p "$PROBE_DIR/bin"
rm -f "$PROBE_DIR/bin/prepatcher-prefix.log"
docker run --rm \
  -v "$MOD_ROOT":/mod:ro \
  -v "$GAME_MANAGED_DIR":/game:ro \
  -v "$PREPATCHER_DIR":/prepatcher:ro \
  -v "$HARMONY_DIR":/harmony:ro \
  -v "$PROBE_DIR":/probe \
  -w /probe mono:6.12 bash -lc '
    set -euo pipefail
    mcs -r:/harmony/0Harmony.dll -out:bin/RitualPrepatcherMonoProbe.exe RitualPrepatcherMonoProbe.cs
    export MONO_PATH=/harmony:/prepatcher:/mod/1.6/Assemblies:/mod/1.6/AssembliesCustom
    if ! mono --debug bin/RitualPrepatcherMonoProbe.exe /mod /game /prepatcher /harmony > bin/prepatcher-prefix.log 2>&1; then
      cat bin/prepatcher-prefix.log
      exit 1
    fi
    cat bin/prepatcher-prefix.log
  '
