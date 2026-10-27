using System;
using System.IO;
using System.Reflection;

public static class ReflectionOnlyUnityProbe
{
    public static int Main(string[] args)
    {
        string loaderPath = args[0], game = args[1], mode = args[2];
        try
        {
            Assembly existing = null;
            if (mode == "existing")
                existing = Assembly.LoadFrom(Path.Combine(game, "UnityEngine.InputLegacyModule.dll"));

            // Match the stock preloader's handler: it observes the request but returns null.
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (sender, eventArgs) => null;
            if (mode != "baseline")
            {
                var loader = Assembly.LoadFrom(loaderPath);
                var resolver = loader.GetType("MultiplayerLoader.ReflectionOnlyUnityResolver", true);
                resolver.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { game });
            }

            var fixture = Assembly.ReflectionOnlyLoadFrom("bin/ReflectionOnlyUnityFixture.dll");
            var type = fixture.GetType("ReflectionOnlyInputSignature").GetMethod("ReadTouch").ReturnType;
            if (mode == "baseline")
                throw new Exception("Baseline unexpectedly resolved the dependency");
            if (type.FullName != "UnityEngine.Touch" || type.Assembly.ReflectionOnly)
                throw new Exception("Expected Unity's normal execution type; got " + type);
            if (existing != null && !ReferenceEquals(existing, type.Assembly))
                throw new Exception("Existing Unity assembly identity was not reused");
            if (!Path.GetFullPath(type.Assembly.Location).Equals(Path.GetFullPath(Path.Combine(game, "UnityEngine.InputLegacyModule.dll"))))
                throw new Exception("Unity assembly was loaded from an unexpected location");
            Console.WriteLine("PASS " + mode + ": resolved UnityEngine.Touch through the production Multiplayer resolver; reflectionOnly=False");
            return 0;
        }
        catch (FileNotFoundException exception)
        {
            if (mode == "baseline" && exception.FileName.StartsWith("UnityEngine.InputLegacyModule,"))
            {
                Console.WriteLine("PASS baseline: reproduced the original reflection-only UnityEngine.InputLegacyModule failure");
                return 0;
            }
            Console.WriteLine("FAIL " + exception);
            return 1;
        }
        catch (Exception exception)
        {
            Console.WriteLine("FAIL " + exception);
            return 1;
        }
    }
}
