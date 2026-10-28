# RimWorld 1.7 metadata audit

Build the current mod, then run `./run-local-tests.sh [built-mod-root]`.
The root defaults to this checkout and must contain `1.7/Assemblies` and
`1.7/AssembliesCustom`. Docker and Python 3 are required; no game installation is
needed. The Docker SDK is selected from the repository's `global.json`.

The audit reads assembly metadata without loading game code. It checks .NET 10,
the RimWorld 1.7 reference, external Harmony 3, and absence of Prepatcher/Cecil or
bundled game DLLs. It does not verify live game loading or multiplayer gameplay.
Mono/Prepatcher probes belong with the frozen legacy source, not this branch.
