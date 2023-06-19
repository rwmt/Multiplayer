using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{

    public class AlertPing : Alert
    {
        public AlertPing()
        {
            defaultPriority = AlertPriority.Critical;
        }

        public override Color BGColor
        {
            get
            {
                float num = Pulser.PulseBrightness(0.5f, Pulser.PulseBrightness(0.5f, 0.6f));
                return new Color(num, num, num) * Color.red;
            }
        }

        public override string GetLabel()
        {
            return "MpAlertPing".Translate();
        }

        public override TaggedString GetExplanation()
        {
            if (Multiplayer.Client == null)
                return "";

            var players = Multiplayer.session.cursorAndPing.pings.Select(p => p.PlayerInfo?.username).AllNotNull().JoinStringsAtMost();
            return $"{"MpAlertPingDesc1".Translate(players)}\n\n{"MpAlertPingDesc2".Translate()}";
        }

        private List<GlobalTargetInfo> culpritList = new();

        private List<GlobalTargetInfo> Culprits
        {
            get
            {
                culpritList.Clear();

                if (Multiplayer.Client != null && !Multiplayer.session.cursorAndPing.alertHidden)
                    foreach (var ping in Multiplayer.session.cursorAndPing.pings)
                    {
                        if (ping.PlayerInfo == null) continue;
                        if (ping.Target.HasValue)
                            culpritList.Add(ping.Target.Value);
                    }

                return culpritList;
            }
        }

        public override AlertReport GetReport()
        {
            return AlertReport.CulpritsAre(Culprits);
        }

        public override void OnClick()
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                Multiplayer.session.cursorAndPing.alertHidden = true;
            else
                base.OnClick();
        }
    }

    [HarmonyPatch(typeof(Alert), nameof(Alert.Notify_Started))]
    static class PreventAlertPingBounce
    {
        static bool Prefix(Alert __instance)
        {
            return __instance is not AlertPing;
        }
    }
}
