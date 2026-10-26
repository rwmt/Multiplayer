This is a manual test executable for Windows Mono, separate from the main solution.
It exercises Mono runtime internals and does not run on RimWorld 1.7's CoreCLR runtime.

Build it explicitly using the RimWorld 1.6 configuration:

```sh
dotnet build Source/TestsOnMono/TestsOnMono.csproj -c Release -p:RimWorld17=false
```

Run the resulting `TestsOnMono.exe` under Windows Mono.
