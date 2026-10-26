This is a manual test executable for Windows Mono, separate from the main solution.
It exercises Mono runtime internals and does not run on RimWorld 1.7's CoreCLR runtime.

Build it explicitly with the .NET 10 SDK:

```sh
dotnet build Source/TestsOnMono/TestsOnMono.csproj -c Release -p:RimWorldVersion=1.6
```

This uses Harmony 2.4.2, matching Prepatcher on RimWorld 1.6.

Run the resulting `TestsOnMono.exe` under Windows Mono.
