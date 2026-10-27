# Version-specific ritual checks

The 1.6 and 1.7 runtime paths differ, so these checks are deliberately separate.
They are standalone probes, not full gameplay or multiplayer-session tests.

## Run all local runtime probes

From the repository root, run:

```sh
./run-local-tests.sh \
  "/path/to/RimWorld-1.6/Managed" \
  "/path/to/Prepatcher/Assemblies" \
  "/path/to/Harmony/1.6/Assemblies" \
  "/path/to/Multiplayer"
```

The optional final argument defaults to this repository. It must contain
already-built assemblies for both `1.6/` and `1.7/`; the runner does not build
or deploy them. It validates all required files before invoking Docker and
then runs the two 1.6 probes below and the 1.7 metadata audit. Paths may contain
spaces, and the runner can be invoked from another working directory. Docker
and Python 3 must be installed and available on `PATH`.

This does not run RimWorld, exercise full host/join gameplay, or run the manual
Windows Mono `TestsOnMono.exe` described in [its README](../TestsOnMono/README.md).

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

The audit uses `System.Reflection.Metadata` in Docker with the SDK version selected by
the repository's root `global.json` to read the assemblies' target-framework
attributes and AssemblyRef/TypeRef tables without loading or executing the mod or
game assemblies. It checks the .NET 10 target, the RimWorld 1.7
`Assembly-CSharp` reference, Harmony's external AssemblyRef, and the absence of
Prepatcher/Cecil references and bundled Harmony/game DLLs. This is a metadata/reference
check only; it does not load the assemblies into a live RimWorld 1.7 CoreCLR process
or test in-game loading, host/join behavior, or gameplay.

## RimWorld 1.6: late reflection-only Unity dependencies

```sh
bash Source/RuntimeTests/run-reflection-only-probe.sh "/path/to/RimWorld-1.6/Managed"
```

An optional second argument selects a mod root with the updated 1.6 loader. This
Mono probe reproduces the missing `UnityEngine.InputLegacyModule` metadata lookup
with a null reflection-only resolver, then verifies Multiplayer's scoped resolver
both reuses an already loaded Unity assembly and resolves a late dependency from
that game's Managed directory. It uses the real Unity input DLL without running
Unity methods. No Prepatcher files are changed; full live scene cleanup is not
simulated. Logs are written to `bin/reflection-only-*.log`.
