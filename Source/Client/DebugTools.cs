using RimWorld;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

            menu.DebugAction("Save game", SaveGame);
            menu.DebugAction("Print static", PrintStatic);

            menu.DoLabel("Multiplayer");

            menu.DebugToolMap("T: Destroy thing", DestroyThing);
            menu.DebugToolMap("T: Mental state", DoMentalState);

            menu.DebugAction("Save map", SaveMap);
            menu.DebugAction("Advance time", AdvanceTime);
            DoMapIncidentDebugAction(menu);
        }

        [SyncMethod(SyncContext.MapMouseCell)]
        [SyncDebugOnly]
        static void DoMentalState()
        {
            foreach (var pawn in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()).OfType<Pawn>())
                pawn.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("TargetedTantrum"), forceWake: true);
        }

        private static void DoMapIncidentDebugAction(Dialog_DebugActionsMenu menu)
        {
            var target = Find.CurrentMap;

            menu.DebugAction("Do incident on map", () =>
            {
                var list = new List<DebugMenuOption>();
                foreach (var localDef2 in DefDatabase<IncidentDef>.AllDefs.Where(d => d.TargetAllowed(target)).OrderBy(d => d.defName))
                {
                    IncidentDef localDef = localDef2;
                    string text = localDef.defName;
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(localDef.category, target);
                    if (!localDef.Worker.CanFireNow(parms, false))
                        text += " [NO]";

                    list.Add(new DebugMenuOption(text, DebugMenuOptionMode.Action, () =>
                    {
                        if (localDef.pointsScaleable)
                        {
                            StorytellerComp storytellerComp = Find.Storyteller.storytellerComps.First((StorytellerComp x) => x is StorytellerComp_OnOffCycle || x is StorytellerComp_RandomMain);
                            parms = storytellerComp.GenerateParms(localDef.category, parms.target);
                        }

                        ExecuteMapIncident(localDef, parms);
                    }));
                }

                Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
            });
        }

        [SyncMethod(SyncContext.CurrentMap, new[] { typeof(IncidentDef), typeof(Expose<IncidentParms>) })]
        [SyncDebugOnly]
        private static void ExecuteMapIncident(IncidentDef def, IncidentParms parms)
        {
            def.Worker.TryExecute(parms);
        }

        [SyncMethod(SyncContext.MapMouseCell)]
        [SyncDebugOnly]
        static void DestroyThing()
        {
            foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
                thing.Destroy(DestroyMode.Vanish);
        }

        [SyncMethod]
        [SyncDebugOnly]
        static void SaveMap()
        {
            Map map = Find.Maps[0];
            byte[] mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_0_{Multiplayer.username}.xml", mapData);
        }

        [SyncMethod]
        [SyncDebugOnly]
        static void AdvanceTime()
        {
            int to = 148 * 1000;
            if (Find.TickManager.TicksGame < to)
            {
                Find.TickManager.ticksGameInt = to;
                Find.Maps[0].AsyncTime().mapTicks = to;
            }
        }

        static void SaveGame()
        {
            byte[] data = ScribeUtil.WriteExposable(Current.Game, "game", true);
            File.WriteAllBytes($"game_0_{Multiplayer.username}.xml", data);
        }

        static void PrintStatic()
        {
            var builder = new StringBuilder();

            foreach (var type in typeof(Game).Assembly.GetTypes())
                if (!type.IsGenericTypeDefinition && type.Namespace != null && (type.Namespace.StartsWith("RimWorld") || type.Namespace.StartsWith("Verse")))
                    foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                        if (!field.IsLiteral)
                            builder.AppendLine($"{field.FieldType} {type}::{field.Name}: {field.GetValue(null)}");

            Log.Message(builder.ToString());
        }
    }

}
