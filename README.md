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

Use the .NET 10 SDK and select the matching game references with `RimWorldVersion`.
The selector defaults to `1.6`; only `1.6` and `1.7` are accepted. It separates
restore assets and outputs under version-specific `bin/` and `obj/` directories.

```sh
dotnet build Source/Multiplayer.sln -c Release -p:RimWorldVersion=1.6
dotnet build Source/Multiplayer.sln -c Release -p:RimWorldVersion=1.7
DOTNET_ROLL_FORWARD=Major dotnet test Source/Tests/Tests.csproj -c Release --no-build -p:RimWorldVersion=1.6
DOTNET_ROLL_FORWARD=Major dotnet test Source/Tests/Tests.csproj -c Release --no-build -p:RimWorldVersion=1.7
```

RimWorld 1.6 builds target .NET Framework 4.8 and use Harmony 2.4.2 with
Prepatcher. RimWorld 1.7 builds target .NET 10, use Harmony 3.0.0-rc.3, and patch
ritual handlers at runtime without Prepatcher. Install the matching Harmony and
Prepatcher mods. `LoadFolders.xml` selects the corresponding `1.6/` or `1.7/` DLLs;
shared assets remain at the mod root. When migrating from a flat install, remove old
root `Assemblies/` and `AssembliesCustom/` folders after backing up any local changes.

Version-specific C# implementations live under `Source/1.6/Client/`,
`Source/1.6/MultiplayerLoader/`, `Source/1.7/Client/`, and
`Source/1.7/MultiplayerLoader/`. MSBuild selects exactly one tree for each build;
put shared code in the existing project folders and version-only code in the matching
tree.

The workshop bundler builds both variants and retains the historical 1.5 package.

The [version-specific ritual checks](Source/RuntimeTests/README.md) cover the 1.6
registrar and direct `PrepatcherPrefix` dispatch under Mono/Harmony 2, including
normal and psychic handlers and serializer registration. The probe stubs `DoSync`
and calls the prefix directly; it does not exercise Prepatcher's Cecil injection or a
full game session. The 1.7 check reads .NET 10, RimWorld, and Harmony assembly metadata
without running the game assemblies. Live 1.7 CoreCLR and host/join testing remain
unavailable.

Pull requests build both game versions using the .NET 10 SDK. Download the
`Multiplayer-mod-pr<number>-<commit>` and `Multiplayer-server-pr<number>-<commit>`
artifacts from the **Validate Pull Request** Actions run; downloads are retained
for seven days. The mod artifact extracts to `Multiplayer/` with both version
folders. The server artifact includes Windows/Linux builds for both game versions,
with the .NET 10 / 1.7 servers under `Server/1.7/`. Test results are uploaded separately.

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

