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

## Building for RimWorld 1.6 and 1.7

Use the .NET 10 SDK. Both mod variants target .NET Framework 4.8 with C# 12
and compile against the RimWorld 1.6 reference assemblies. The 1.7 runtime
redirects legacy Unity assembly references; runtime checks handle game API differences.

Select the game version with `RimWorldVersion` (defaults to `1.6`). This selects
Harmony 2.4.2 for 1.6, matching Prepatcher's bundled Harmony, or Harmony 3.0.0-rc.3
for 1.7. The source and target framework are shared, but the resulting DLLs are
version-specific because they reference different Harmony assemblies.

```sh
dotnet build Source/Multiplayer.sln -c Release -p:RimWorldVersion=1.6
dotnet test Source/Tests/Tests.csproj -c Release --no-build -p:RimWorldVersion=1.6
dotnet build Source/Multiplayer.sln -c Release -p:RimWorldVersion=1.7
dotnet test Source/Tests/Tests.csproj -c Release --no-build -p:RimWorldVersion=1.7
```

Restore intermediates and build outputs are isolated by game version. Mod DLLs
are copied to `1.6/Assemblies/` and `1.6/AssembliesCustom/`, or their `1.7/`
equivalents. `LoadFolders.xml` selects the matching variant. Install Prepatcher
on 1.6; install Harmony 3 on 1.7, which does not require Prepatcher. The package
contains no bundled Harmony DLL. When updating an older checkout or installation,
remove obsolete root `Assemblies/` and `AssembliesCustom/` folders before running
the game, so only the versioned DLLs are loaded.

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

