using Multiplayer.Client.Factions;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace Multiplayer.Client
{
    public static class PrepatcherWarning
    {
        
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
            Widgets.DrawMenuSection(inRect);
            MouseoverSounds.DoRegion(inRect);

            if (SteamManager.Initialized)
                TooltipHandler.TipRegion(inRect, "Open <color=#f4c542><b>Prepatcher</b></color> mod on <color=#4484b2><b>Steam Workshop</b></color>.");
            else
                TooltipHandler.TipRegion(inRect, "Open <color=#f4c542><b>Prepatcher</b></color> mod on browser.");

            if (Mouse.IsOver(inRect))
                Widgets.DrawHighlight(inRect);

            if (Mouse.IsOver(inRect) && Input.GetMouseButtonDown(0))
            {
                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                if (SteamManager.Initialized)
                {
                    SteamUtility.OpenWorkshopPage(new Steamworks.PublishedFileId_t(2934420800));
                }
                else
                {
                    Application.OpenURL("https://steamcommunity.com/sharedfiles/filedetails/?id=2934420800");
                }
            }

            DrawAnimatedBorder(inRect, 2f, new ColorInt(218, 166, 26).ToColor);
            // Create Faction Headline
            inRect.yMin += 5f;
            using (MpStyle.Set(GameFont.Small))
            using (MpStyle.Set(WordWrap.NoWrap))
                Widgets.Label(inRect.Right(8f), "<b>PREPATCHER NOT FOUND</b>");
            inRect.yMin += 20f;
            // END Create Faction Headline

            // Line
            float lineX = inRect.x + 8f;
            float lineY = inRect.yMin;
            float lineWidth = inRect.width - 16f; //16*2 Header
            Widgets.DrawLineHorizontal(lineX, lineY, lineWidth);
            inRect.yMin += 5f;
            // END Line

            // Description text
            string warningText =
     "Rituals will not work properly in multiplayer without the <color=#f4c542><b>Prepatcher</b></color> mod. " +
     "Please <b>install</b> and <b>enable</b> it to avoid <color=red>desync issues</color>.";
            using (MpStyle.Set(WordWrap.DoWrap))
            {
                Rect textRect = new Rect(inRect.x + 8f, inRect.yMin, inRect.width - 16f, Text.CalcHeight(warningText, inRect.width - 16f));
                Widgets.Label(textRect, warningText);
            }


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

        private static void DrawBorder(Rect inRect, float borderWidth, Color color)
        {
            Color prevColor = GUI.color;

            GUI.color = color;

            GUI.DrawTexture(new Rect(inRect.x, inRect.y, inRect.width, borderWidth), BaseContent.WhiteTex);                   // Up
            GUI.DrawTexture(new Rect(inRect.x, inRect.yMax - borderWidth, inRect.width, borderWidth), BaseContent.WhiteTex);  // Down
            GUI.DrawTexture(new Rect(inRect.x, inRect.y, borderWidth, inRect.height), BaseContent.WhiteTex);                  // Left
            GUI.DrawTexture(new Rect(inRect.xMax - borderWidth, inRect.y, borderWidth, inRect.height), BaseContent.WhiteTex); // Right

            GUI.color = prevColor;
        }
    }
}
