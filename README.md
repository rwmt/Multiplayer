![banner](https://user-images.githubusercontent.com/49448379/134965756-2a30ffd9-2f6c-43d6-a2a4-584252fc2e4b.png)



The RimWorld Multiplayer mod allows users to play full games of Rimworld cooperatively.

## Links
[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2606448745) |
[Website](https://rimworldmultiplayer.com) |
[Discord](https://discord.gg/S4bxXpv) |
[Documentation](https://hackmd.io/@rimworldmultiplayer/docs/)

## Development
[Git Releases](https://github.com/rwmt/Multiplayer/releases) |
[Installation](https://hackmd.io/@rimworldmultiplayer/docs/https%3A%2F%2Fhackmd.io%2F%40rimworldmultiplayer%2Fplay%23Installation) |
[Hosting](https://hackmd.io/@rimworldmultiplayer/docs/https%3A%2F%2Fhackmd.io%2F%40rimworldmultiplayer%2Fplay%23Installation#Hosting-and-Joining) |
[FAQ](https://hackmd.io/@rimworldmultiplayer/docs/https%3A%2F%2Fhackmd.io%2F%40rimworldmultiplayer%2Ffaq%23Common-Questions#FAQ) |
[Contributing](https://github.com/rwmt/Multiplayer/blob/master/CONTRIBUTORS.md) |
[DEV Wiki](https://hackmd.io/@rimworldmultiplayer/docs/https%3A%2F%2Fhackmd.io%2F%40rimworldmultiplayer%2Fdev-home)

Please do all pull requests to the [dev](https://github.com/rwmt/Multiplayer/tree/dev) branch.

## Building for RimWorld 1.7

This source branch targets RimWorld 1.7, .NET 10, and Harmony 3. SDK selection
comes from `global.json`; later installed .NET 10 SDKs are allowed by `latestMinor`.
Prepatcher is not required. Normal builds have no game-version selector.

```sh
dotnet build Source/Multiplayer.sln -c Release
dotnet test Source/Tests/Tests.csproj -c Release --no-build
./run-local-tests.sh
```

The client build stages resolved mod dependencies into `1.7/Assemblies` and
`1.7/AssembliesCustom`. Game and Harmony DLLs are supplied by the game/mod
installation and must not be packaged. See [the metadata audit](Source/RuntimeTests/README.md)
for local validation and its limitations. Full host/join, reload/rejoin, stockpile
filter/cache, and ritual scenarios still require live game testing.

## Desync tracing modes

RNG, world, map, and command synchronization checks remain enabled in every mode.
The normal **Fast** setting records lightweight event context (tick, RNG state and
thing identity), without walking managed call stacks or formatting stack strings.
**None** disables diagnostic event history. **Slow** additionally captures detailed
managed stacks for each eligible event and can significantly increase CPU and memory
usage. Use Slow only when collecting diagnostics for a reproducible issue.

Diagnostic history also requires the session's desync trace logging to be enabled.
The selector is available in the normal mod settings in Release builds, and the
chosen mode is saved. Managed tracing works on ARM64 as well as x64.

## Versioned CI packages

The Workshop mod currently includes RimWorld 1.5 and 1.6. New packages retain
both and add the current 1.7 source build. `.github/package-versions.json` is the
single list of included versions: `current` identifies this source branch;
`releases` lists the upstream branches to build independently. Adding another
release entry creates another CI matrix job without changing the packaging code.

PR, beta, and workshop workflows call `.github/workflows/build-packages.yml`.
The 1.5 job uses upstream `rw-1.5`. The 1.6 job uses `rw-1.6` when available,
otherwise commit `9df94bd04ea9a94c00bd39a276e58c4c2f965740`. Network failures fail
the job rather than silently selecting a fallback. Every version records the
resolved SHA in `<version>/source-commit.txt` in the package.

Each source is built unchanged with its own references. Backports are explicit;
changes to the 1.7 code do not automatically affect other game versions. Build
artifacts use a common `<version>/About` and `<version>/files` layout. The shared
packaging action consumes one `builds-root` and a list of required versions. It
fails on missing builds, preserves each version's Defs, Languages, Textures and
assemblies, and generates matching dependency metadata and `LoadFolders.xml`.
Only release metadata is shared at the package root. No bundler script is used.

The 1.5 release entry explicitly retains the Workshop package's Prepatcher
requirement: the branch's older About metadata lists Harmony alone, but its ritual
implementation uses Prepatcher. Additional release dependencies are merged without
duplicating source declarations.

PR runs upload mod, server, and test-result artifacts for seven days. Standalone
servers use `Server/<version>/Windows` and `Server/<version>/Linux` for every version.
The workshop workflow stamps the mod version and uploads the combined mod package.

## Donations

If you’re feeling generous these are people who have contributed greatly to the mod’s development and upkeep.

**[Zetrith](https://patreon.com/zetrith)** - Creator, Core, Support\
**[NotFood](https://ko-fi.com/notfood)** - Core, Mod Compatiblity, Compatibility Commissions\
**[Sokyran](https://ko-fi.com/sokyran)** - Core, Mod Compatiblity, Compatibility Commissions\
[Nebual](https://ko-fi.com/Nebual) - Core\
[Thomas107500](https://ko-fi.com/thomas107500) - Mod Compatiblity\
[Luz](https://ko-fi.com/llavorre) - Support, Admin\
[Mistress Mia](https://ko-fi.com/miaamakiir) - Support, Admin\
[Swept](https://ko-fi.com/swept) - Support, Admin, Website

## Notes
Thanks to Pardeike for making [Harmony](https://github.com/pardeike/Harmony) and RevenantX for creating [LiteNetLib](https://github.com/RevenantX/LiteNetLib)

