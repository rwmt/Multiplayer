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

The root `global.json` sets SDK 10.0.302 as the minimum and allows roll-forward
to newer installed .NET 10 minor versions, feature bands, and patches. Select game
references with `RimWorldVersion`; the selector defaults to `1.6` and accepts
`1.6` or `1.7`. It separates restore assets and outputs under version-specific
`bin/` and `obj/` directories. The 1.6 test project targets .NET 8, so the test
commands use `DOTNET_ROLL_FORWARD=Major` separately to run it on the .NET 10 runtime.

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

For example, a client implementation can keep the same namespace, class name,
and internal API in both version trees while the shared caller stays in
`Source/Client/`:

```csharp
// Source/1.6/Client/RuntimeHooks.cs
namespace Multiplayer;
internal static class RuntimeHooks
{
    internal static bool RequiresPrepatcher => true;
}
```

```csharp
// Source/1.7/Client/RuntimeHooks.cs
namespace Multiplayer;
internal static class RuntimeHooks
{
    internal static bool RequiresPrepatcher => false;
}
```

```csharp
// Source/Client/RuntimeSetup.cs
namespace Multiplayer;
internal static class RuntimeSetup
{
    internal static void Configure()
    {
        if (RuntimeHooks.RequiresPrepatcher) InstallPrepatcherHooks();
        else InstallRuntimeHooks();
    }

    private static void InstallPrepatcherHooks() { /* shared setup */ }
    private static void InstallRuntimeHooks() { /* shared setup */ }
}
```

`Source/Directory.Build.targets` maps the `Multiplayer` client project to
`Client/` and includes only `Source/<RimWorldVersion>/Client/**/*.cs`. The same
mapping selects `MultiplayerLoader` from `MultiplayerLoader/` and `Tests` from
`Tests/`. No extra `.csproj` entries or preprocessor directives are needed;
keep the version-specific class out of shared `Source/Client/` so the selected
implementation is defined once. Build the client for either version with:

```sh
dotnet build Source/Client/Multiplayer.csproj -c Release -p:RimWorldVersion=1.6
dotnet build Source/Client/Multiplayer.csproj -c Release -p:RimWorldVersion=1.7
```

The workshop bundler builds both variants and retains the historical 1.5 package.

### Local runtime probes

To run the assembly-dependent local probes for RimWorld 1.6 and 1.7, use
`run-local-tests.sh` with the installed 1.6 game `Managed` directory, Prepatcher
`Assemblies` directory, Harmony 1.6 `Assemblies` directory, and optionally a
mod root containing already-built `1.6/` and `1.7/` assemblies:

```sh
./run-local-tests.sh \
  "/path/to/RimWorld-1.6/Managed" \
  "/path/to/Prepatcher/Assemblies" \
  "/path/to/Harmony/1.6/Assemblies" \
  "/path/to/Multiplayer"
```

The runner requires Docker and Python 3 and does not build or deploy the mod
DLLs. Build both variants before running, or pass a mod root containing both
built variants. It runs the 1.6 ritual-prefix and reflection-only Mono probes
and the 1.7 metadata audit. These do not launch RimWorld or verify a full
host/join session. Windows Mono `TestsOnMono.exe` remains a separate manual test; see
[TestsOnMono](Source/TestsOnMono/README.md).

The [version-specific ritual checks](Source/RuntimeTests/README.md) cover the 1.6
registrar and direct `PrepatcherPrefix` dispatch under Mono/Harmony 2, including
normal and psychic handlers and serializer registration. The probe stubs `DoSync`
and calls the prefix directly; it does not exercise Prepatcher's Cecil injection or a
full game session. The 1.7 check reads .NET 10, RimWorld, and Harmony assembly metadata
without running the game assemblies. Neither probe tests live RimWorld 1.7 CoreCLR
loading, host/join networking, or gameplay.

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

