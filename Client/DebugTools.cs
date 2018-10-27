using Harmony;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client
{
    [MpPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems))]
    static class DebugToolsListing_Patch
    {
        static void Postfix(Dialog_DebugActionsMenu __instance)
        {
            if (Multiplayer.Client == null) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            var menu = __instance;

            menu.DoLabel("Multiplayer");

            menu.DebugToolMap("T: Destroy thing", DestroyThing);
            menu.DebugAction("Save map", SaveMap);
            menu.DebugAction("Advance time", AdvanceTime);
        }

        [SyncMethod(SyncContext.MapMouseCell)]
        static void DestroyThing()
        {
            foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
                thing.Destroy(DestroyMode.Vanish);
        }

        [SyncMethod]
        public static void AdvanceTime()
        {
            int to = 148 * 1000;
            if (Find.TickManager.TicksGame < to)
            {
                Find.TickManager.ticksGameInt = to;
                Find.Maps[0].AsyncTime().mapTicks = to;
            }
        }

        [SyncMethod]
        public static void SaveMap()
        {
            Map map = Find.Maps[0];
            byte[] mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_0_{Multiplayer.username}.xml", mapData);
        }
    }

    [MpPatch(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DevToolStarterOnGUI))]
    static class AddDebugButtonPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            bool found = false;

            foreach (CodeInstruction inst in insts)
            {
                if (!found && inst.opcode == OpCodes.Stloc_1)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Add);
                    found = true;
                }

                yield return inst;
            }
        }
    }

    [MpPatch(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons))]
    static class DebugButtonsPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var list = new List<CodeInstruction>(insts);

            var labels = list.Last().labels;
            list.RemoveLast();

            list.Add(
                new CodeInstruction(OpCodes.Ldloc_0) { labels = labels },
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DebugButtonsPatch), nameof(Draw))),
                new CodeInstruction(OpCodes.Ret)
            );

            return list;
        }

        static void Draw(WidgetRow row)
        {
            if (row.ButtonIcon(TexButton.Paste, "Hot swap."))
                HotSwap.DoHotSwap();
        }
    }

}
