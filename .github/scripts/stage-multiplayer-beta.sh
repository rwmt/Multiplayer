#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
destination="${1:-output/Multiplayer}"
if [[ "$destination" != /* ]]; then
  destination="$repo_root/$destination"
fi

# Package only shared assets and the resolved outputs from each selector build.
rm -rf "$destination"
mkdir -p "$destination"
cp -R "$repo_root/About" "$repo_root/Defs" "$repo_root/Languages" "$repo_root/Textures" "$destination"
cp "$repo_root/LoadFolders.xml" "$destination/LoadFolders.xml"
rm -f "$destination/Languages/.git"
sed -i 's#<name>Multiplayer</name>#<name>Multiplayer [Continuous]</name>#' "$destination/About/About.xml"

for version in 1.6 1.7; do
  build="$repo_root/Source/Client/bin/$version/Release"
  [[ -f "$build/Multiplayer.dll" ]] || { echo "Missing $version Client output: $build/Multiplayer.dll" >&2; exit 1; }
  [[ -f "$build/MultiplayerCommon.dll" ]] || { echo "Missing $version Common output: $build/MultiplayerCommon.dll" >&2; exit 1; }

  mkdir -p "$destination/$version/Assemblies" "$destination/$version/AssembliesCustom"
  cp "$build/Multiplayer.dll" "$build/MultiplayerCommon.dll" "$destination/$version/AssembliesCustom/"

  found_loader=false
  for dll in "$build"/*.dll; do
    [[ -f "$dll" ]] || continue
    name="${dll##*/}"
    case "$name" in
      Multiplayer.dll|MultiplayerCommon.dll|0Harmony.dll|0Prepatcher*.dll|Prepatcher*.dll|Mono.Cecil*.dll|UnityStubs.dll|UnityEngine*.dll|Assembly-CSharp*.dll|Verse.dll)
        continue
        ;;
    esac
    cp "$dll" "$destination/$version/Assemblies/"
    [[ "$name" != MultiplayerLoader.dll ]] || found_loader=true
  done

  [[ "$found_loader" == true ]] || { echo "Missing resolved MultiplayerLoader.dll for RimWorld $version" >&2; exit 1; }
done

# Harmony and Prepatcher are supplied by the dependencies declared in About.xml.
if find "$destination/1.7" -type f \( -iname '*prepatcher*.dll' -o -iname '0Harmony.dll' -o -iname 'Mono.Cecil*.dll' \) -print -quit | grep -q .; then
  echo "Unexpected Harmony or Prepatcher implementation in the 1.7 package" >&2
  exit 1
fi
