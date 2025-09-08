using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Desyncs;

public static class MetadataGenerator
{
    public static string Generate() => $"{MakeLogTimestamp()}\n{ListActiveMods()}\n{ListHarmonyPatches()}";

    private static string MakeLogTimestamp() =>
        $"Log generated on {DateTime.Now.ToLongDateString()}, {DateTime.Now.ToLongTimeString()}";

    private static string ListHarmonyPatches()
    {
        var patchMethods = Harmony
            .GetAllPatchedMethods()
            .Select(method => (method, patches: Harmony.GetPatchInfo(method)))
            .Where(x => x.patches != null)
            .ToList();
        var patchListing = DescribeAllPatchedMethods(patchMethods);
        var newline = patchListing.EndsWith('\n') ? "" : "\n";
        return $"Active Harmony patches:\n{patchListing}{newline}{HarmonyUtil.DescribeHarmonyVersions(patchMethods)}\n";
    }

    /// <summary>
    /// Produces a human-readable list of all methods patched by all Harmony instances and their respective patches.
    /// </summary>
    private static string DescribeAllPatchedMethods(List<(MethodBase, HarmonyLib.Patches)> patchMethods)
    {
        try
        {
            return HarmonyUtil.DescribePatchedMethodsList(patchMethods);
        }
        catch (Exception e)
        {
            return "Could not retrieve patched methods from the Harmony library:\n" + e;
        }
    }

    private static string ListActiveMods()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Loaded mods:");
        foreach (var modContentPack in LoadedModManager.RunningMods)
        {
            builder.AppendFormat("{0}({1})", modContentPack.Name, modContentPack.PackageIdPlayerFacing);
            TryAppendOverrideVersion(builder, modContentPack);
            TryAppendManifestVersion(builder, modContentPack);
            builder.Append(": ");
            var firstAssembly = true;
            var anyAssemblies = false;
            foreach (var loadedAssembly in modContentPack.assemblies.loadedAssemblies)
            {
                if (!firstAssembly)
                {
                    builder.Append(", ");
                }

                firstAssembly = false;
                builder.Append(loadedAssembly.GetName().Name);

                var (version, fileVersion) = ReadModAssembly(loadedAssembly, modContentPack);

                if (version != null)
                {
                    if (fileVersion == null) builder.AppendFormat("({0} [no file version])", version);
                    else if (version == fileVersion) builder.AppendFormat("({0})", version);
                    else builder.AppendFormat("(av: {0} fv:{1})", version, fileVersion);
                }

                anyAssemblies = true;
            }

            if (!anyAssemblies)
            {
                builder.Append("(no assemblies)");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void TryAppendOverrideVersion(StringBuilder builder, ModContentPack pack)
    {
        var filePath = Path.Combine(pack.RootDir, "About", "Version.xml");
        if (!File.Exists(filePath)) return;

        var doc = XDocument.Load(filePath);

        var overrideVersionElement = doc.Root?.Element("overrideVersion");
        if (overrideVersionElement != null) builder.AppendFormat("[ov:{0}]", overrideVersionElement.Value);
    }

    private static void TryAppendManifestVersion(StringBuilder builder, ModContentPack pack)
    {
        var filePath = Path.Combine(pack.RootDir, "About", "Manifest.xml");
        if (!File.Exists(filePath)) return;

        var doc = XDocument.Load(filePath);
        var versionElement = doc.Root?.Element("version") ?? doc.Root?.Element("Version");
        if (versionElement != null) builder.AppendFormat("[mv:{0}]", versionElement.Value);
    }

    /// <summary>
    /// Reads assembly version information for a mod assembly.
    /// Mod assemblies require special treatment, since they are loaded from byte arrays and their <see cref="Assembly.Location"/> is null.
    /// </summary>
    /// <param name="assembly">The assembly to read</param>
    /// <param name="contentPack">The content pack the assembly was loaded from</param>
    private static (string assemblyVersion, string assemblyFileVersion) ReadModAssembly(Assembly assembly,
        ModContentPack contentPack)
    {
        static string ToSemanticString(Version v)
        {
            // System.Version parts: Major.Minor.Build.Revision
            return v.Build < 0
                ? $"{v.ToString(2)}.0"
                : v.ToString(v.Revision <= 0 ? 3 : 4);
        }

        if (assembly == null) return (null, null);
        const string assembliesFolderName = "Assemblies";

        var expectedAssemblyFileName = $"{assembly.GetName().Name}.dll";
        var modAssemblyFolderFiles = ModContentPack.GetAllFilesForMod(contentPack, assembliesFolderName);
        var fileHandle = modAssemblyFolderFiles.Values.FirstOrDefault(f => f.Name == expectedAssemblyFileName);

        try
        {
            var assemblyFilePath = fileHandle?.FullName ?? assembly.Location;
            var fileInfo = FileVersionInfo.GetVersionInfo(assemblyFilePath);

            return (ToSemanticString(assembly.GetName().Version), ToSemanticString(new Version(fileInfo.FileVersion)));
        }
        catch (Exception)
        {
            return (ToSemanticString(assembly.GetName().Version), null);
        }
    }
}
