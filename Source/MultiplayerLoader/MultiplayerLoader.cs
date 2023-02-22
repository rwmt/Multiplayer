using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace MultiplayerLoader
{
    // This class has to be named Multiplayer for backwards compatibility of the settings file location
    public class Multiplayer : Mod
    {
        public static Multiplayer? instance;
        public static Action<Rect>? settingsWindowDrawer;

        public Multiplayer(ModContentPack content) : base(content)
        {
            instance = this;
            LoadAssembliesCustom();

            GenTypes.GetTypeInAnyAssembly("Multiplayer.Client.Multiplayer")!.GetMethod("InitMultiplayer")!.Invoke(null, null);
        }

        private void LoadAssembliesCustom()
        {
            var assemblies = new List<Assembly>();

            foreach (FileInfo item in
                     from f
                         in ModContentPack.GetAllFilesForModPreserveOrder(Content, "AssembliesCustom/",
                             e => e.ToLower() == ".dll")
                     select f.Item2)
            {
                Assembly assembly;

                try
                {
                    byte[] rawAssembly = File.ReadAllBytes(item.FullName);
                    FileInfo fileInfo =
                        new FileInfo(Path.Combine(item.DirectoryName!, Path.GetFileNameWithoutExtension(item.FullName)) +
                                     ".pdb");

                    if (fileInfo.Exists)
                    {
                        byte[] rawSymbolStore = File.ReadAllBytes(fileInfo.FullName);
                        assembly = AppDomain.CurrentDomain.Load(rawAssembly, rawSymbolStore);
                        Log.Message(""+AssemblyName.GetAssemblyName(item.FullName));
                    }
                    else
                    {
                        assembly = AppDomain.CurrentDomain.Load(rawAssembly);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Exception loading {item.Name}: {ex}");
                    break;
                }

                assemblies.Add(assembly);
            }

            var asmResolve = (Delegate)typeof(AppDomain).GetField("AssemblyResolve", BindingFlags.Instance | BindingFlags.NonPublic)
                !.GetValue(AppDomain.CurrentDomain)!;

            Assembly Resolver(object _, ResolveEventArgs args)
            {
                return assemblies.FirstOrDefault(a => a.FullName == args.Name);
            }

            typeof(AppDomain).GetField("AssemblyResolve", BindingFlags.Instance | BindingFlags.NonPublic)
                !.SetValue(AppDomain.CurrentDomain, Delegate.Combine(
                    (ResolveEventHandler)Resolver,
                    asmResolve));

            foreach (var asm in assemblies)
                if (Content.assemblies.AssemblyIsUsable(asm))
                {
                    GenTypes.ClearCache();
                    Content.assemblies.loadedAssemblies.Add(asm);
                }
        }

        public override string SettingsCategory() => "Multiplayer";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settingsWindowDrawer?.Invoke(inRect);
        }
    }
}
