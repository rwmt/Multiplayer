using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;

// Standalone managed probe for the RimWorld 1.6 Prepatcher registration path.
// This calls production Init and PrepatcherPrefix directly; it does not run Cecil rewriting.
public static class RitualPrepatcherMonoProbe
{
    static string mod, game, prepatcher, harmonyDir;
    static bool simulatedDoSyncResult;
    static int doSyncCalls;
    static readonly Harmony fixtures = new Harmony("Tests.RitualPrepatcherMonoProbe");

    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: RitualPrepatcherMonoProbe <mod-root> <game-managed> <prepatcher-assemblies> <harmony-assemblies>");
            return 2;
        }

        mod = args[0];
        game = args[1];
        prepatcher = args[2];
        harmonyDir = args[3];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        try
        {
            Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL " + exception);
            return 1;
        }
    }

    static void Run()
    {
        if (Type.GetType("Mono.Runtime") == null)
            throw new Exception("This probe requires Mono.");
        if (typeof(Harmony).Assembly.GetName().Version.Major != 2)
            throw new Exception("RimWorld 1.6 probe requires Harmony 2; got " + typeof(Harmony).Assembly.GetName().Version);

        var gameAssembly = Assembly.LoadFrom(Path.Combine(game, "Assembly-CSharp.dll"));
        var gameVersion = gameAssembly.GetName().Version;
        if (gameVersion.Major != 1 || gameVersion.Minor != 6)
            throw new Exception("Expected RimWorld 1.6 game assemblies; got " + gameVersion + ".");
        var prepatcherApi = Assembly.LoadFrom(Path.Combine(prepatcher, "0PrepatcherAPI.dll"));
        if (prepatcherApi.GetName().Name != "0PrepatcherAPI")
            throw new Exception("The supplied Prepatcher API assembly has an unexpected identity.");
        var loader = Assembly.LoadFrom(Path.Combine(mod, "1.6/Assemblies/MultiplayerLoader.dll"));
        var client = Assembly.LoadFrom(Path.Combine(mod, "1.6/AssembliesCustom/Multiplayer.dll"));
        Assembly.LoadFrom(Path.Combine(mod, "1.6/AssembliesCustom/MultiplayerCommon.dll"));
        var prepatchInterface = loader.GetType("MultiplayerLoader.ISyncMethodForPrepatcher", true);
        var syncMethodType = client.GetType("Multiplayer.Client.SyncMethod", true);
        if (!prepatchInterface.IsAssignableFrom(syncMethodType))
            throw new Exception("1.6 SyncMethod does not implement ISyncMethodForPrepatcher.");

        Console.WriteLine("Mono runtime detected.");
        Console.WriteLine("Harmony=" + typeof(Harmony).Assembly.GetName().Version + "; Prepatcher API=" + prepatcherApi.GetName().Version + "; Game=" + gameAssembly.GetName().Version);

        // Prevent Unity texture access while loading the production serializer registrations.
        var unityCore = Assembly.LoadFrom(Path.Combine(game, "UnityEngine.CoreModule.dll"));
        var loadTexture = unityCore.GetType("UnityEngine.Resources", true)
            .GetMethod("Load", new[] { typeof(string), typeof(Type) });
        if (loadTexture == null)
            throw new Exception("Could not find UnityEngine.Resources.Load(string, Type).");
        fixtures.Patch(loadTexture, prefix: new HarmonyMethod(typeof(RitualPrepatcherMonoProbe).GetMethod("SkipTexture")));

        var rituals = loader.GetType("MultiplayerLoader.SyncRituals", true);
        rituals.GetMethod("Init", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        var handlers = (IList)rituals.GetField("syncMethodsWithPrepatches", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        if (handlers.Count != 10)
            throw new Exception("Expected ten ritual registrations, got " + handlers.Count + ".");

        var normalWidget = gameAssembly.GetType("RimWorld.PawnRitualRoleSelectionWidget", true);
        var psychicWidget = gameAssembly.GetType("RimWorld.PawnPsychicRitualRoleSelectionWidget", true);
        var targetType = syncMethodType.GetProperty("TargetType");
        var normalCount = 0;
        var psychicCount = 0;
        var serializerCount = 0;
        foreach (var handler in handlers)
        {
            if (!prepatchInterface.IsInstanceOfType(handler))
                throw new Exception("A registered handler does not implement ISyncMethodForPrepatcher.");
            var widgetType = (Type)targetType.GetValue(handler, null);
            if (widgetType == normalWidget) normalCount++;
            else if (widgetType == psychicWidget) psychicCount++;
            else throw new Exception("Unexpected ritual handler target " + widgetType + ".");

            var transformers = (Array)syncMethodType.GetField("argTransformers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(handler);
            if (transformers.Length > 1 && transformers.GetValue(1) != null)
                serializerCount++;
        }
        if (normalCount != 5 || psychicCount != 5 || serializerCount != 4)
            throw new Exception("Unexpected registration shape: normal=" + normalCount + ", psychic=" + psychicCount + ", role serializers=" + serializerCount + ".");
        Console.WriteLine("PASS SyncRituals.Init: 10 handlers (5 normal, 5 psychic), four role serializers.");

        // Exercise the actual production prefix against its registered handlers. Stub only
        // DoSync's return value, so no multiplayer state or network transport is required.
        var doSync = syncMethodType.GetMethod("DoSync", BindingFlags.Public | BindingFlags.Instance);
        fixtures.Patch(doSync, prefix: new HarmonyMethod(typeof(RitualPrepatcherMonoProbe).GetMethod("StubDoSync")));
        var prefix = rituals.GetMethod("PrepatcherPrefix", BindingFlags.Public | BindingFlags.Static);
        var normal = FormatterServices.GetUninitializedObject(normalWidget);
        var psychic = FormatterServices.GetUninitializedObject(psychicWidget);

        foreach (bool active in new[] { true, false })
        {
            simulatedDoSyncResult = active;
            AssertPrefix(prefix, 0, normal, !active, active ? "normal active" : "normal inactive");
            AssertPrefix(prefix, 1, psychic, !active, active ? "psychic active" : "psychic inactive");
            AssertPrefix(prefix, 0, psychic, true, active ? "psychic wrong-type active" : "psychic wrong-type inactive");
            AssertPrefix(prefix, 1, normal, true, active ? "normal wrong-type active" : "normal wrong-type inactive");
        }

        Console.WriteLine("PASS direct PrepatcherPrefix dispatch: normal and psychic each call DoSync exactly once when types match; wrong types continue without dispatch.");
    }

    static void AssertPrefix(MethodInfo prefix, int index, object instance, bool expectedContinue, string label)
    {
        doSyncCalls = 0;
        var result = (bool)prefix.Invoke(null, new object[] { index, instance, new object[0] });
        if (result != expectedContinue)
            throw new Exception(label + ": expected continue=" + expectedContinue + ", got " + result + ".");
        var expectedCalls = label.Contains("wrong-type") ? 0 : 1;
        if (doSyncCalls != expectedCalls)
            throw new Exception(label + ": expected DoSync calls=" + expectedCalls + ", got " + doSyncCalls + ".");
        Console.WriteLine("PASS " + label + ": continue=" + result + ", DoSync calls=" + doSyncCalls + ".");
    }

    public static bool SkipTexture() { return false; }

    public static bool StubDoSync(object __instance, ref bool __result)
    {
        doSyncCalls++;
        __result = simulatedDoSyncResult;
        return false;
    }

    static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name + ".dll";
        foreach (var folder in new[] {
            harmonyDir, prepatcher, game,
            Path.Combine(mod, "1.6/Assemblies"), Path.Combine(mod, "1.6/AssembliesCustom")
        })
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);
        }
        return null;
    }
}
