using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    static class ModPatches
    {
        public static void Init()
        {
            var harmony = Multiplayer.harmony;

            // Compat with Fluffy's mod loader
            var fluffysModButtonType = MpReflection.GetTypeByName("ModManager.ModButton_Installed");
            if (fluffysModButtonType != null)
            {
                harmony.Patch(
                    fluffysModButtonType.GetMethod("DoModButton"),
                    new HarmonyMethod(typeof(PageModsPatch), nameof(PageModsPatch.ModManager_ButtonPrefix)),
                    new HarmonyMethod(typeof(PageModsPatch), nameof(PageModsPatch.Postfix))
                );
            }

            var cancelForArbiter = new HarmonyMethod(typeof(CancelForArbiter), "Prefix");

            // Fix PrisonLabor breaking the arbiter
            {
                PatchIfExists("PrisonLabor.Behaviour_MotivationIcon", "Update", cancelForArbiter);
                PatchIfExists("PrisonLabor.Core.GUI_Components.PawnIcons", "MapComponentTick", cancelForArbiter);
                PatchIfExists("PrisonLabor.MapComponent_Icons", "MapComponentTick", cancelForArbiter);
            }

            // PawnsAreCapable compat
            // Replace workSettings.SetPriority(def, p) with (workSettings.priorities[def] = p)
            // Minimizes side effects and goes around syncing
            // Also cache the dictionary to improve performance
            {
                var pawnsAreCapablePatch = new HarmonyMethod(typeof(ModPatches), nameof(PawnsAreCapable_FloatMenu_Patch_Transpiler));
                PatchIfExists("PawnsAreCapable.FloatMenuMakerMap_ChoicesAtFor", "Prefix", transpiler: pawnsAreCapablePatch);
                PatchIfExists("PawnsAreCapable.FloatMenuMakerMap_ChoicesAtFor", "Postfix", transpiler: pawnsAreCapablePatch);
            }

            var randPatchPrefix = new HarmonyMethod(typeof(RandPatches), "Prefix");
            var randPatchPostfix = new HarmonyMethod(typeof(RandPatches), "Postfix");

            // Facial Stuff compat
            {
                PatchIfExists("FacialStuff.Harmony.HarmonyPatchesFS", "TryInteractWith_Postfix", randPatchPrefix, randPatchPostfix);
            }
        }

        static MethodInfo SetPriorityMethod = AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority));
        static MethodBase WorkTypeDefDictCtor = AccessTools.Constructor(typeof(Dictionary<WorkTypeDef, int>), new Type[0]);
        static Dictionary<WorkTypeDef, int> workTypeDefDict = new Dictionary<WorkTypeDef, int>();

        static IEnumerable<CodeInstruction> PawnsAreCapable_FloatMenu_Patch_Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == WorkTypeDefDictCtor)
                {
                    yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(ModPatches), nameof(workTypeDefDict)));
                    continue;
                }

                if (inst.operand == SetPriorityMethod)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModPatches), nameof(SetPriority)));
                    continue;
                }

                yield return inst;
            }
        }

        static void SetPriority(Pawn_WorkSettings settings, WorkTypeDef def, int p) => settings.priorities[def] = p;

        static void PatchIfExists(string typeName, string methodName, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null)
        {
            var type = MpReflection.GetTypeByName(typeName);
            if (type == null) return;

            var method = AccessTools.Method(type, methodName);
            if (method == null) return;

            Multiplayer.harmony.Patch(method, prefix, postfix, transpiler);
        }
    }

    // Hold shift in the mod list to highlight XML mods
    [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoModRow))]
    static class PageModsPatch
    {
        public static string currentModName;
        public static string currentModCompat;
        public static Dictionary<string, string> truncatedStrings;

        static void Prefix(Page_ModsConfig __instance, ModMetaData mod)
        {
            ModManager_ButtonPrefix(null, mod, __instance.truncatedModNamesCache);
        }

        public static void ModManager_ButtonPrefix(object __instance, ModMetaData ____selected, Dictionary<string, string> ____modNameTruncationCache)
        {
            if (!Input.GetKey(KeyCode.LeftShift)) return;

            var mod = ____selected;
            currentModName = __instance == null ? mod.Name : (string)__instance.GetPropertyOrField("TrimmedName");
            truncatedStrings = ____modNameTruncationCache;
            if (Multiplayer.xmlMods.Contains(mod.RootDir.FullName)) {
                currentModCompat = "XML";
            }

            if (Multiplayer.modsCompatibility.ContainsKey((int) mod.publishedFileIdInt.m_PublishedFileId)) {
                var compat = Multiplayer.modsCompatibility[(int) mod.publishedFileIdInt.m_PublishedFileId];
                if (compat == 1) {
                    currentModCompat = $"<color=red>{compat}</color>";
                } else if (compat == 2) {
                    currentModCompat = $"<color=orange>{compat}</color>";
                } else if (compat == 3) {
                    currentModCompat = $"<color=yellow>{compat}</color>";
                } else if (compat == 4) {
                    currentModCompat = $"<color=green>{compat}</color>";
                }
                else {
                    currentModCompat = $"<color=grey>{compat}</color>";
                }
            }
        }

        public static void Postfix()
        {
            currentModName = null;
            truncatedStrings = null;
            currentModCompat = null;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), typeof(Rect), typeof(string))]
    static class WidgetsLabelPatch
    {
        static void Prefix(ref Rect rect, ref string label)
        {
            if (PageModsPatch.currentModName == null || PageModsPatch.currentModCompat == null) return;

            if (label == PageModsPatch.currentModName || PageModsPatch.truncatedStrings.TryGetValue(PageModsPatch.currentModName, out string truncated) && truncated == label)
            {
                rect.width += 50;
                label = $"<b>[{PageModsPatch.currentModCompat}]</b> {label}";
            }
        }
    }

    [HarmonyPatch(typeof(ScribeMetaHeaderUtility), nameof(ScribeMetaHeaderUtility.ModListsMatch))]
    static class IgnoreSomeModsWhenCheckingSaveModList
    {
        static List<string> IgnoredModNames = new List<string>() { "HotSwap", "Mod Manager" };

        static void Prefix(ref List<string> a, ref List<string> b)
        {
            if (a != ScribeMetaHeaderUtility.loadedModIdsList)
                return;

            a = a.ToList();
            b = b.ToList();

            a.RemoveAll((id, index) => IgnoredModNames.Contains(ScribeMetaHeaderUtility.loadedModNamesList[index]));
            b.RemoveAll((id, index) => IgnoredModNames.Contains(LoadedModManager.RunningModsListForReading[index].Name));
        }
    }

}
