using UnityEngine;
using RimWorld;
using Steamworks;
using Verse;
using Verse.Sound;
using Verse.Steam;
using Multiplayer.Client.Util;

namespace Multiplayer.Client
{
    public static class PrepatcherWarning
    {
        private static readonly string prepatcherPackageId = "zetrith.prepatcher";
        private static readonly PublishedFileId_t prepatcherFileId = new PublishedFileId_t(2934420800);
        private static ModMetaData prepatcherMetaData;
        public  static PrepatcherStatus prepatcherStatus = PrepatcherStatus.Unknown;

        private const float Spacing = 8f;

        public static void DoPrepatcherWarning(Rect inRect)
        {
            GUI.BeginGroup(new Rect(inRect.x, inRect.y, inRect.width, inRect.height));
            {
                Rect groupRect = new Rect(0, 0, inRect.width, inRect.height);
                DrawWarning(groupRect);
            }
            GUI.EndGroup();
        }

        private static void DrawWarning(Rect inRect)
        {
            if (prepatcherStatus == PrepatcherStatus.Unknown)
                CheckPrepatcherStatus();

            // Background
            Widgets.DrawMenuSection(inRect);
            DrawAnimatedBorder(inRect, 2f, new ColorInt(218, 166, 26).ToColor);
            MouseoverSounds.DoRegion(inRect);

            if (Mouse.IsOver(inRect))
                Widgets.DrawHighlight(inRect);
            // END Background

            //Headline
            inRect.yMin += 5f;
            using (MpStyle.Set(GameFont.Small))
            using (MpStyle.Set(WordWrap.NoWrap))
            {
                switch (prepatcherStatus)
                {
                    case PrepatcherStatus.NotFound:
                        Widgets.Label(inRect.Right(Spacing), "MpPrepatcherWarnNotFoundHeadline".Translate());
                        break;
                    case PrepatcherStatus.NotActive:
                        Widgets.Label(inRect.Right(Spacing), "MpPrepatcherWarnDisabledHeadline".Translate());
                        break;
                }
            }
            inRect.yMin += 20f;
            //END Headline

            // Line
            float lineX = inRect.x + Spacing;
            float lineY = inRect.yMin;
            float lineWidth = inRect.width - (Spacing * 2); //Cut both sides
            Widgets.DrawLineHorizontal(lineX, lineY, lineWidth);
            inRect.yMin += 5f;
            // END Line

            // Description 
            using (MpStyle.Set(WordWrap.DoWrap))
            {
                Rect textRect;
                switch (prepatcherStatus)
                {
                    case PrepatcherStatus.NotFound:
                        textRect = new Rect(inRect.x + Spacing, inRect.yMin, inRect.width - (Spacing * 2), Text.CalcHeight("MpPrepatcherWarnNotFoundDescription".Translate().RemoveRichTextTags(), inRect.width - (Spacing * 2)));
                        Widgets.Label(textRect, "MpPrepatcherWarnNotFoundDescription".Translate());
                        break;
                    case PrepatcherStatus.NotActive:
                        textRect = new Rect(inRect.x + Spacing, inRect.yMin, inRect.width - (Spacing * 2), Text.CalcHeight("MpPrepatcherWarnDisabledDescription".Translate().RemoveRichTextTags(), inRect.width - (Spacing * 2)));
                        Widgets.Label(textRect, "MpPrepatcherWarnDisabledDescription".Translate());
                        break;
                }
            
            }
            // END Description 

            // Tooltip
            if (prepatcherStatus == PrepatcherStatus.NotFound)
            {
                if (SteamManager.Initialized)
                    TooltipHandler.TipRegion(inRect, "MpPrepatcherWarnOpenSteamWorkshop".Translate());
                else
                    TooltipHandler.TipRegion(inRect, "MpPrepatcherWarnOpenBrowser".Translate());

                if (Mouse.IsOver(inRect) && Input.GetMouseButtonDown(0))
                {
                    SoundDefOf.TabOpen.PlayOneShotOnCamera();
                    if (SteamManager.Initialized)
                        SteamUtility.OpenWorkshopPage(prepatcherFileId);
                    else
                        Application.OpenURL("https://steamcommunity.com/sharedfiles/filedetails/?id=2934420800");
                }
            }
            else
            {
                //We don't want the game to restart without saving by accident.
                if (Current.Game == null)
                {
                    TooltipHandler.TipRegion(inRect, "MpPrepatcherWarnEnableRestart".Translate());
                    if (Mouse.IsOver(inRect) && Input.GetMouseButtonDown(0))
                    {
                        SoundDefOf.TabOpen.PlayOneShotOnCamera();
                        DoRestart();
                    }
                }
            }
            // END Tooltip
        }

        private static void DrawAnimatedBorder(Rect inRect, float borderWidth, Color baseColor, float pulseSpeed = 1f)
        {
            Color prevColor = GUI.color;

            float pulse = Mathf.PingPong(Time.realtimeSinceStartup * pulseSpeed, 1f);
            Color animatedColor = Color.Lerp(baseColor, Color.white, pulse);  

            GUI.color = animatedColor;

            GUI.DrawTexture(new Rect(inRect.x, inRect.y, inRect.width, borderWidth), BaseContent.WhiteTex);                   // Up
            GUI.DrawTexture(new Rect(inRect.x, inRect.yMax - borderWidth, inRect.width, borderWidth), BaseContent.WhiteTex);  // Down
            GUI.DrawTexture(new Rect(inRect.x, inRect.y, borderWidth, inRect.height), BaseContent.WhiteTex);                  // Left
            GUI.DrawTexture(new Rect(inRect.xMax - borderWidth, inRect.y, borderWidth, inRect.height), BaseContent.WhiteTex); // Right

            GUI.color = prevColor;
        }

        public static void CheckPrepatcherStatus()
        {
            prepatcherMetaData = ModLister.GetModWithIdentifier(prepatcherPackageId);

            if (prepatcherMetaData == null)
            {
                prepatcherStatus = PrepatcherStatus.NotFound;
                return;
            }

            prepatcherStatus = prepatcherMetaData.Active
                ? PrepatcherStatus.Active
                : PrepatcherStatus.NotActive;
        }

        private static void DoRestart()
        {
            if (prepatcherMetaData != null)
            {
                ModsConfig.SetActive(prepatcherMetaData.PackageId, true);
                ModsConfig.RestartFromChangedMods();
            }
            else {
                Messages.Message("MpPrepatcherWarnFail".Translate(), MessageTypeDefOf.NegativeEvent, false);
                CheckPrepatcherStatus();
            }
        }

        public enum PrepatcherStatus
        {
            Unknown,
            NotFound,
            NotActive,
            Active
        }
    }
}
