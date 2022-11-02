using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches
{
    class StylingDialog_DummyPawn : Pawn
    {
        public Pawn origPawn;
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class StylingDialog_ReplaceWindow
    {
        // We normally open the dialog through OpenDialogWithoutCreatingJob,
        // but we leave this here in case some mod opens up the styling dialog by their own
        static bool Prefix(ref Window window)
        {
            if (Multiplayer.Client == null || window is not Dialog_StylingStation dialog || dialog.pawn is StylingDialog_DummyPawn)
                return true;

            window = CreateStylingDialog(dialog.pawn, dialog.stylingStation);

            return Multiplayer.ExecutingCmds && TickPatch.currentExecutingCmdIssuedBySelf
                   || dialog.pawn.CurJob.loadID == SyncMethods.stylingStationJobStartedByMe;
        }

        public static Dialog_StylingStation CreateStylingDialog(Pawn dialogPawn, Thing stylingStation)
        {
            // In vanilla, the styling dialog mutates the pawn directly
            // A dummy pawn taking on the mutations is needed for multiplayer
            var pawn = new StylingDialog_DummyPawn
            {
                origPawn = dialogPawn,
                def = dialogPawn.def,
                gender = dialogPawn.gender,
                mapIndexOrState = dialogPawn.mapIndexOrState,
                nameInt = dialogPawn.Name
            };

            pawn.story = MpUtil.ShallowCopy(dialogPawn.story, new Pawn_StoryTracker(pawn));
            pawn.story.pawn = pawn;

            pawn.style = MpUtil.ShallowCopy(dialogPawn.style, new Pawn_StyleTracker(pawn));
            pawn.style.pawn = pawn;

            pawn.apparel = MpUtil.ShallowCopy(dialogPawn.apparel, new Pawn_ApparelTracker(pawn));
            pawn.apparel.pawn = pawn;
            pawn.apparel.lockedApparel = pawn.apparel.lockedApparel.ToList();
            pawn.apparel.wornApparel = MpUtil.ShallowCopy(dialogPawn.apparel.wornApparel, new ThingOwner<Apparel>());
            pawn.apparel.wornApparel.innerList = pawn.apparel.wornApparel.innerList.ToList();

            pawn.health = new Pawn_HealthTracker(pawn);
            pawn.stances = new Pawn_StanceTracker(pawn);
            pawn.pather = new Pawn_PathFollower(pawn);
            pawn.roping = new Pawn_RopeTracker(pawn);
            pawn.mindState = new Pawn_MindState(pawn);
            pawn.ideo = new Pawn_IdeoTracker(pawn)
            {
                ideo = dialogPawn.ideo.ideo
            };
            pawn.ageTracker = new Pawn_AgeTracker(pawn)
            {
                cachedLifeStageIndex = dialogPawn.ageTracker.cachedLifeStageIndex
            };

            return new Dialog_StylingStation(pawn, stylingStation);
        }
    }

    [HarmonyPatch(typeof(Dialog_StylingStation), nameof(Dialog_StylingStation.DoWindowContents))]
    static class StylingDialog_CloseOnDestroyed
    {
        static bool Prefix(Dialog_StylingStation __instance)
        {
            // The styling station got destroyed (in multiplayer, the styling dialog is not pausing)
            if (__instance.stylingStation != null && !__instance.stylingStation.Spawned)
            {
                __instance.Close();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_StylingStation), nameof(Dialog_StylingStation.DoWindowContents))]
    static class StylingDialog_Marker
    {
        public static Dialog_StylingStation drawing;

        static void Prefix(Dialog_StylingStation __instance)
        {
            drawing = __instance;
        }

        static void Finalizer()
        {
            drawing = null;
        }
    }

    struct StylingData : ISyncSimple
    {
        public Pawn pawn;
        public Thing stylingStation;
        public bool hadStylingStation;
        public HairDef hair;
        public BeardDef beard;
        public TattooDef faceTattoo;
        public TattooDef bodyTattoo;
        public Color hairColor;
        public Dictionary<Apparel, Color> apparelColors;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
    internal static class StylingDialog_HandleAccept
    {
        static void Postfix(string label, ref Widgets.DraggableResult __result)
        {
            if (StylingDialog_Marker.drawing == null) return;
            if (Multiplayer.Client == null) return;

            if (label == "Accept".Translate() && __result.AnyPressed())
            {
                OnAccept();
                __result = Widgets.DraggableResult.Idle;
            }
        }

        static void OnAccept()
        {
            var dia = StylingDialog_Marker.drawing;
            var pawn = dia.pawn;

            StylingDialog_Accept(
                new StylingData()
                {
                    pawn = ((StylingDialog_DummyPawn)pawn).origPawn,
                    stylingStation = dia.stylingStation,
                    hadStylingStation = dia.stylingStation != null,
                    hair = pawn.story.hairDef,
                    beard = pawn.style.beardDef,
                    faceTattoo = pawn.style.FaceTattoo,
                    bodyTattoo = pawn.style.BodyTattoo,
                    hairColor = dia.desiredHairColor,
                    apparelColors = dia.apparelColors
                }
            );

            dia.Reset(false);
            dia.Close();
        }

        [SyncMethod]
        internal static void StylingDialog_Accept(StylingData data)
        {
            // If the styling station gets destroyed during syncing do nothing
            if ((data.stylingStation != null) != data.hadStylingStation)
                return;

            var dialog = new Dialog_StylingStation(data.pawn, data.stylingStation)
            {
                apparelColors = data.apparelColors,
                desiredHairColor = data.hairColor
            };

            var pawn = data.pawn;
            pawn.story.hairDef = data.hair;
            pawn.style.beardDef = data.beard;
            pawn.style.FaceTattoo = data.faceTattoo;
            pawn.style.BodyTattoo = data.bodyTattoo;

            // Copied from RimWorld.Dialog_StylingStation.DrawBottomButtons

            if (pawn.story.hairDef != dialog.initialHairDef
                || pawn.style.beardDef != dialog.initialBeardDef
                || pawn.style.FaceTattoo != dialog.initialFaceTattoo
                || pawn.style.BodyTattoo != dialog.initialBodyTattoo
                || data.hairColor != pawn.story.hairColor)
            {
                if (data.stylingStation != null)
                {
                    pawn.style.SetupNextLookChangeData(
                        pawn.story.hairDef,
                        pawn.style.beardDef,
                        pawn.style.FaceTattoo,
                        pawn.style.BodyTattoo,
                        dialog.desiredHairColor
                    );

                    dialog.Reset();
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.UseStylingStation, data.stylingStation));
                }
                else
                {
                    pawn.style.Notify_StyleItemChanged();
                }
            }
            dialog.ApplyApparelColors();
        }
    }

    [HarmonyPatch]
    internal static class OpenDialogWithoutCreatingJob
    {
        private static AccessTools.FieldRef<object, Building_StylingStation> stylingStationField;

        internal static MethodBase TargetMethod()
        {
            var method = MpMethodUtil.GetLambda(typeof(Building_StylingStation),
                nameof(Building_StylingStation.GetFloatMenuOptions), lambdaOrdinal: 0);

            stylingStationField = AccessTools.FieldRefAccess<Building_StylingStation>(method.DeclaringType, "<>4__this");

            return method;
        }

        internal static bool Prefix(object __instance, Pawn ___selPawn)
        {
            if (Multiplayer.Client == null)
                return true;

            Find.WindowStack.Add(StylingDialog_ReplaceWindow.CreateStylingDialog(___selPawn, stylingStationField(__instance)));

            return false;
        }
    }

}
