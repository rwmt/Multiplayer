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
            gameName = file?.gameName ?? string.Empty;

            var localAddr = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Loopback;
            address = localAddr.ToString();
        }

        private string address;
        private string gameName;
        private int maxPlayers = 8;
        private int autosaveInterval = 8;
        private bool lan = true;
        private bool steam;
        private string password;

        private string maxPlayersBuffer;
        private string autosaveBuffer;

        public override void DoWindowContents(Rect inRect)
        {
            var entry = new Rect(0, 10f, inRect.width, 30f);

            var labelWidth = 100f;

            gameName = TextEntryLabeled(entry, "Name:  ", gameName, labelWidth);
            entry = entry.Down(40);

            address = TextEntryLabeled(entry, "Address:  ", address, labelWidth);
            entry = entry.Down(40);

            TextFieldNumericLabeled(entry.Width(labelWidth + 30f), "Max players:  ", ref maxPlayers, ref maxPlayersBuffer, labelWidth);

            TextFieldNumericLabeled(entry.Right(200f).Width(labelWidth + 35f), "Autosave every ", ref autosaveInterval, ref autosaveBuffer, labelWidth + 5f);
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

            Widgets.CheckboxLabeled(entry.Right(labelWidth - Text.CalcSize("LAN:  ").x).Width(120), "LAN:  ", ref lan, placeCheckboxNearText: true);
            entry = entry.Down(30);

            if (SteamManager.Initialized)
            {
                Widgets.CheckboxLabeled(entry.Right(labelWidth - Text.CalcSize("Steam:  ").x).Width(120), "Steam:  ", ref steam, placeCheckboxNearText: true);
                entry = entry.Down(30);
            }

            var buttonRect = new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f);

            if (Widgets.ButtonText(buttonRect, "Host") && TryParseIp(address, out IPAddress addr, out int port))
            {
                if (file != null)
                {
                    if (file.replay)
                        HostFromReplay(addr, port);
                    else
                        HostFromSave(addr, port);
                }
                else
                {
                    ClientUtil.HostServer(addr, port, false);
                }

                Close(true);
            }
        }

        private bool TryParseIp(string ip, out IPAddress addr, out int port)
        {
            port = MultiplayerServer.DefaultPort;
            string[] parts = ip.Split(':');

            if (!IPAddress.TryParse(parts[0], out addr))
            {
                Messages.Message("MpInvalidAddress", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (parts.Length >= 2 && !int.TryParse(parts[1], out port))
            {
                Messages.Message("MpInvalidPort", MessageTypeDefOf.RejectInput, false);
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

        public static void TextFieldNumericLabeled(Rect rect, string label, ref int val, ref string buffer, float labelWidth)
        {
            Rect labelRect = rect;
            labelRect.width = labelWidth;
            Rect fieldRect = rect;
            fieldRect.xMin += labelWidth;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(labelRect, label);
            Text.Anchor = anchor;
            Widgets.TextFieldNumeric(fieldRect, ref val, ref buffer);
        }

        public override void PostClose()
        {
            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }

        private void HostFromSave(IPAddress addr, int port)
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.InitData.gameToLoad = file.name;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    LongEventHandler.QueueLongEvent(() => ClientUtil.HostServer(addr, port, false), "MpLoading", false, null);
                });
            }, "Play", "LoadingLongEvent", true, null);
        }

        private void HostFromReplay(IPAddress addr, int port)
        {
            Replay.LoadReplay(file.name, true, () =>
            {
                OnMainThread.StopMultiplayer();
                ClientUtil.HostServer(addr, port, true);
            });
        }
    }
}
