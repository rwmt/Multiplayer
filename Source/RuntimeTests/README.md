# Version-specific ritual checks

The 1.6 and 1.7 runtime paths differ, so these checks are deliberately separate.
They are standalone probes, not full gameplay or multiplayer-session tests.

## RimWorld 1.6: Mono and Harmony 2

Build the 1.6 mod first. Then pass the actual 1.6 `Managed` directory, Prepatcher
`Assemblies` directory, and installed Harmony mod's 1.6 `Assemblies` directory:

```sh
bash Source/RuntimeTests/run-ritual-probe.sh \
  "/path/to/RimWorld-1.6/Managed" \
  "/path/to/Prepatcher/Assemblies" \
  "/path/to/Harmony/1.6/Assemblies"
```

An optional fourth argument selects a mod root containing `1.6/Assemblies` and
`1.6/AssembliesCustom`; by default the repository root is used. The script uses
Docker's `mono:6.12` and the supplied Harmony 2 runtime. It does not call Multiplayer's
native Mono profiler and does not require `mono-2.0-bdwgc`.

The probe loads the actual 1.6 game/mod/Prepatcher API assemblies and calls the
production `SyncRituals.Init()`. It asserts ten registrations (five normal and five
psychic), four role serializers, and that `SyncMethod` implements the Prepatcher
interface. It then calls the real `PrepatcherPrefix` directly for normal and psychic
widgets. A Harmony test prefix stubs only `SyncMethod.DoSync`'s result so it can verify
exact-type dispatch, one call per matching handler, continuation when synchronization
is inactive, and suppression when the simulated active sync succeeds.

This invokes the prefix directly. It does not run Prepatcher's Cecil rewrite/injection,
load a full RimWorld session, or exercise real network serialization/transport. Output
is saved to `Source/RuntimeTests/bin/prepatcher-prefix.log`.

## RimWorld 1.7: reference and metadata audit

Run this against the versioned mod root after building or packaging the 1.7 variant:

```sh
bash Source/RuntimeTests/audit-rimworld-1.7-reference.sh
# Or pass a packaged mod root:
bash Source/RuntimeTests/audit-rimworld-1.7-reference.sh "/path/to/Multiplayer"
```

The audit uses `System.Reflection.Metadata` through Docker's .NET 10 SDK to read the
assemblies' target-framework attributes and AssemblyRef/TypeRef tables without loading
or executing the mod or game assemblies. It checks .NET 10, the RimWorld 1.7
`Assembly-CSharp` reference, Harmony's external AssemblyRef, and the absence of
Prepatcher/Cecil references and bundled Harmony/game DLLs. This is a metadata/reference
check; there is no live RimWorld 1.7 CoreCLR runtime available here, so it does not
establish successful in-game loading or host/join behavior.
