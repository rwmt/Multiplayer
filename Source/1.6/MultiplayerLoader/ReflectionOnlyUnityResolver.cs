#nullable enable
using System;
using System.IO;
using System.Reflection;

namespace MultiplayerLoader;

internal sealed class ReflectionOnlyUnityResolver
{
    private static bool installed;
    private readonly string managedDirectory;

    internal ReflectionOnlyUnityResolver(string managedDirectory)
    {
        this.managedDirectory = managedDirectory;
    }

    internal static void Install(string managedDirectory)
    {
        if (installed)
            return;

        var resolver = new ReflectionOnlyUnityResolver(managedDirectory);
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolver.Resolve;
        installed = true;
    }

    internal Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);
        var name = requested.Name;
        if (name == null || (name != "UnityEngine" && !name.StartsWith("UnityEngine.", StringComparison.Ordinal)))
            return null;

        // Prepatcher leaves retired assemblies in Mono's reflection-only context.
        // Unity's later scene cleanup may inspect types whose input-module dependency
        // was never bound there. Reuse the normal Unity type identity, not a second copy.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.ReflectionOnly && Matches(requested, assembly.GetName()))
                return assembly;

        if (Path.GetFileName(name) != name)
            return null;

        var path = Path.Combine(managedDirectory, name + ".dll");
        if (!File.Exists(path) || !Matches(requested, AssemblyName.GetAssemblyName(path)))
            return null;

        return Assembly.LoadFrom(path);
    }

    private static bool Matches(AssemblyName requested, AssemblyName candidate) =>
        string.Equals(requested.FullName, candidate.FullName, StringComparison.OrdinalIgnoreCase);
}
