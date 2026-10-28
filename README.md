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

## Legacy versions and CI packages

Legacy compatibility is a packaging concern. PR, beta, and workshop workflows
call `.github/workflows/build-packages.yml`. Independent jobs build this branch
and upstream's `rw-1.6` branch, when present. Until that branch exists, the legacy
job uses commit `9df94bd04ea9a94c00bd39a276e58c4c2f965740`. A branch-check network
failure fails the job rather than silently selecting the fallback. Its resolved
SHA is recorded in the artifact's `1.6/source-commit.txt`.

Legacy code is built unchanged with its own references. Backports must be made
explicitly on the legacy source branch; changes here do not automatically affect
1.6. A shared GitHub action assembles independently built version folders, keeping
each version's Defs, Languages, Textures, assemblies, and dependency declarations.
Only release metadata is shared at the package root. `LoadFolders.xml` selects the
correct assets and DLLs. No standalone bundler script is used.

PR runs upload mod, server, and test-result artifacts for seven days. Server
artifacts retain .NET 8 legacy servers under `Server/Windows` and `Server/Linux`,
with current .NET 10 servers under `Server/1.7/Windows` and `Server/1.7/Linux`.
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

