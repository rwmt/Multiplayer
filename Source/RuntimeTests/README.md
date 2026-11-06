# RimWorld 1.7 metadata audit

Build the current mod, then run this command from the repository root:

```sh
dotnet run --project Source/RuntimeTests/MetadataAudit.csproj -c Release -- ./1.7
```

The argument is the built mod's version directory, containing `Assemblies` and
`AssembliesCustom`. The audit uses the .NET SDK selected by `global.json`; no game
installation is needed.

The audit reads assembly metadata without loading game code. It checks .NET 10,
the RimWorld 1.7 reference, external Harmony 3, and absence of Prepatcher/Cecil or
bundled game DLLs. It does not verify live game loading or multiplayer gameplay.
Mono/Prepatcher probes belong with the frozen legacy source, not this branch.
