#!/usr/bin/env bash
# Probe 1.6 Unity reflection-only dependency resolution under Mono.
set -euo pipefail
if [[ "$#" -lt 1 || "$#" -gt 2 ]]; then
  echo "Usage: bash $0 <RimWorld-1.6-Managed> [mod-root]" >&2
  exit 2
fi
PROBE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
MOD_ROOT="${2:-$(cd -- "$PROBE_DIR/../.." && pwd)}"
GAME_MANAGED_DIR="$1"
for file in "$GAME_MANAGED_DIR/UnityEngine.InputLegacyModule.dll" "$GAME_MANAGED_DIR/netstandard.dll" "$MOD_ROOT/1.6/Assemblies/MultiplayerLoader.dll"; do
  [[ -f "$file" ]] || { echo "Missing probe input: $file" >&2; exit 1; }
done
mkdir -p "$PROBE_DIR/bin"
docker run --rm \
  -v "$MOD_ROOT":/mod:ro \
  -v "$GAME_MANAGED_DIR":/game:ro \
  -v "$PROBE_DIR":/probe -w /probe mono:6.12 bash -lc '
    set -euo pipefail
    mcs -target:library -r:/game/netstandard.dll -r:/game/UnityEngine.CoreModule.dll \
      -r:/game/UnityEngine.InputLegacyModule.dll -out:bin/ReflectionOnlyUnityFixture.dll ReflectionOnlyUnityFixture.cs
    mcs -out:bin/ReflectionOnlyUnityProbe.exe ReflectionOnlyUnityProbe.cs
    for mode in baseline existing late; do
      if ! mono bin/ReflectionOnlyUnityProbe.exe /mod/1.6/Assemblies/MultiplayerLoader.dll /game "$mode" > "bin/reflection-only-$mode.log" 2>&1; then
        cat "bin/reflection-only-$mode.log"
        exit 1
      fi
      cat "bin/reflection-only-$mode.log"
    done
  '
