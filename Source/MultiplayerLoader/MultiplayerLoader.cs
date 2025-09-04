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
        public static Multiplayer instance;
        public static Action<Rect> settingsWindowDrawer;

        public Multiplayer(ModContentPack content) : base(content)
        {
            instance = this;
            LoadAssembliesCustom();

            FindTypeInAppDomain("Multiplayer.Client.Multiplayer")!.GetMethod("InitMultiplayer")!.Invoke(null, null);
        }

        public static Type? FindTypeInAppDomain(string typeFullName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeFullName, throwOnError: false, ignoreCase: false))
                .FirstOrDefault(type => type != null);

        private void LoadAssembliesCustom()
        {
            var assemblies = new List<Assembly>();

            foreach (FileInfo item in ModContentPack
                         .GetAllFilesForModPreserveOrder(Content, "AssembliesCustom/", e => e.ToLower() == ".dll")
                         .Select(f => f.Item2))
            {
                Assembly assembly;
                try
                {
                    // This would work with Assembly.Load the same as ModAssemblyHandler does it, but then the .dll
                    // files are locked and cannot be replaced when a game instance is already running. This way it's
                    // possible to keep a game instance open and just rebuild and reopen another instance without any
                    // issues with replacing the dll files.
                    byte[] rawAssembly = File.ReadAllBytes(item.FullName);
                    FileInfo fileInfo = new FileInfo(Path.ChangeExtension(item.FullName, "pdb"));

                    if (fileInfo.Exists)
                    {
                        byte[] rawSymbolStore = File.ReadAllBytes(fileInfo.FullName);
                        assembly = AppDomain.CurrentDomain.Load(rawAssembly, rawSymbolStore);
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

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                Content.assemblies.loadedAssemblies.FirstOrDefault(a => a.FullName == args.Name);

            foreach (var asm in assemblies.Where(asm => Content.assemblies.AssemblyIsUsable(asm)))
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
