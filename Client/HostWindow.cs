using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Profile;
using Verse.Sound;
using Verse.Steam;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class HostWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(450f, 320f);

        private SaveFile file;
        public bool returnToServerBrowser;

        public HostWindow(SaveFile file = null)
        {
            closeOnAccept = false;
            doCloseX = true;

            this.file = file;
            settings.gameName = file?.gameName ?? $"{Multiplayer.username}'s game";

            var localAddr = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Loopback;
            settings.address = localAddr;
            addressBuffer = localAddr.ToString();

            settings.lan = true;
            settings.arbiter = true;
        }

        private ServerSettings settings = new ServerSettings();

        private string addressBuffer;
        private string maxPlayersBuffer;
        private string autosaveBuffer;

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(inRect.Down(0), $"Host from {(file == null ? "this game" : (file.replay ? "a replay" : "a savefile"))}");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            var entry = new Rect(0, 45, inRect.width, 30f);

            var labelWidth = 100f;

            settings.gameName = TextEntryLabeled(entry, "Game name:  ", settings.gameName, labelWidth);
            entry = entry.Down(40);

            TextFieldNumericLabeled(entry.Width(labelWidth + 30f), "Max players:  ", ref settings.maxPlayers, ref maxPlayersBuffer, labelWidth, 0, 999);

            TextFieldNumericLabeled(entry.Right(200f).Width(labelWidth + 35f), "Autosave every ", ref settings.autosaveInterval, ref autosaveBuffer, labelWidth + 5f, 0, 999);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(entry.Right(200f).Right(labelWidth + 35f), " minutes");
            Text.Anchor = TextAnchor.UpperLeft;
            entry = entry.Down(40);

            /*const char passChar = '\u2022';
            if (Event.current.type == EventType.Repaint || Event.current.isMouse)
                TextEntryLabeled(entry.Width(200), "Password:  ", new string(passChar, password.Length), labelWidth);
            else
                password = TextEntryLabeled(entry.Width(200), "Password:  ", password, labelWidth);
            entry = entry.Down(40);*/

            var directLabelWidth = Text.CalcSize("Direct:  ").x;
            CheckboxLabeled(entry.Width(130), "Direct:  ", ref settings.direct, placeTextNearCheckbox: true);
            if (settings.direct)
            {
                addressBuffer = Widgets.TextField(entry.Width(130).Right(130 + 10), addressBuffer);
            }

            entry = entry.Down(30);

            CheckboxLabeled(entry.Width(130), "LAN:  ", ref settings.lan, placeTextNearCheckbox: true);
            entry = entry.Down(30);

            if (SteamManager.Initialized)
            {
                CheckboxLabeled(entry.Width(130), "Steam:  ", ref settings.steam, placeTextNearCheckbox: true);
                entry = entry.Down(30);
            }

            TooltipHandler.TipRegion(entry.Width(130), "MpArbiterDesc".Translate());
            CheckboxLabeled(entry.Width(130), "The Arbiter:  ", ref settings.arbiter, placeTextNearCheckbox: true);
            entry = entry.Down(30);

            var buttonRect = new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f);

            if (Widgets.ButtonText(buttonRect, "Host") && 
                (!settings.direct || TryParseIp(addressBuffer, out settings.address, out settings.port)))
            {
                if (file == null)
                    ClientUtil.HostServer(settings, false);
                else if (file.replay)
                    HostFromReplay(settings);
                else
                    HostFromSave(settings);

                Close(true);
            }
        }

        public static void CheckboxLabeled(Rect rect, string label, ref bool checkOn, bool disabled = false, Texture2D texChecked = null, Texture2D texUnchecked = null, bool placeTextNearCheckbox = false)
        {
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            if (placeTextNearCheckbox)
            {
                float textWidth = Text.CalcSize(label).x;
                rect.x = rect.xMax - textWidth - 24f - 5f;
                rect.width = textWidth + 24f + 5f;
            }

            Widgets.Label(rect, label);

            if (!disabled && Widgets.ButtonInvisible(rect, false))
            {
                checkOn = !checkOn;
                if (checkOn)
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera(null);
                else
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera(null);
            }

            Widgets.CheckboxDraw(rect.x + rect.width - 24f, rect.y, checkOn, disabled, 24f, null, null);
            Text.Anchor = anchor;
        }

        private bool TryParseIp(string ip, out IPAddress addr, out int port)
        {
            port = MultiplayerServer.DefaultPort;
            string[] parts = ip.Split(':');

            if (!IPAddress.TryParse(parts[0], out addr))
            {
                Messages.Message("MpInvalidAddress".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (parts.Length >= 2 && !int.TryParse(parts[1], out port))
            {
                Messages.Message("MpInvalidPort".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }

        public static string TextEntryLabeled(Rect rect, string label, string text, float labelWidth)
        {
            Rect labelRect = rect.Rounded();
            labelRect.width = labelWidth;
            Rect fieldRect = rect;
            fieldRect.xMin += labelWidth;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(labelRect, label);
            Text.Anchor = anchor;
            return Widgets.TextField(fieldRect, text);
        }

        public static void TextFieldNumericLabeled(Rect rect, string label, ref int val, ref string buffer, float labelWidth, float min = 0, float max = float.MaxValue)
        {
            Rect labelRect = rect;
            labelRect.width = labelWidth;
            Rect fieldRect = rect;
            fieldRect.xMin += labelWidth;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(labelRect, label);
            Text.Anchor = anchor;
            Widgets.TextFieldNumeric(fieldRect, ref val, ref buffer, min, max);
        }

        public override void PostClose()
        {
            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        private void HostFromSave(ServerSettings settings)
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.InitData.gameToLoad = file.fileName;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    LongEventHandler.QueueLongEvent(() => ClientUtil.HostServer(settings, false), "MpLoading", false, null);
                });
            }, "Play", "LoadingLongEvent", true, null);
        }

        private void HostFromReplay(ServerSettings settings)
        {
            Replay.LoadReplay(file.fileName, true, () =>
            {
                OnMainThread.StopMultiplayer();
                ClientUtil.HostServer(settings, true);
            });
        }
    }
}
