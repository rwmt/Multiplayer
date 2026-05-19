using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class AlertPing : Alert
{
    // A freshly placed marker counts as alert-worthy for the same window a ping stays visible.
    private const float FreshMarkerWindow = PingInfo.PingDuration;

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

        // Union of recent ping-placers and recent marker-placers - both feed Culprits.
        var loc = Multiplayer.session.locationPings;
        var pingNames = loc.pings.Select(p => p.PlayerInfo?.username);
        var freshMarkerNames = loc.Markers
            .Where(IsFreshMarker)
            .Select(m => m.placedByUsername);
        var players = pingNames.Concat(freshMarkerNames).AllNotNull().Distinct().JoinStringsAtMost();
        return $"{"MpAlertPingDesc1".Translate(players)}\n\n{"MpAlertPingDesc2".Translate()}";
    }

    private List<GlobalTargetInfo> culpritList = new();

    private List<GlobalTargetInfo> Culprits
    {
        get
        {
            culpritList.Clear();

            if (Multiplayer.Client != null && !Multiplayer.session.locationPings.alertHidden)
            {
                var loc = Multiplayer.session.locationPings;
                foreach (var ping in loc.pings)
                {
                    if (ping.PlayerInfo == null) continue;
                    if (!ping.IsVisible()) continue;
                    if (ping.Target.HasValue)
                        culpritList.Add(ping.Target.Value);
                }
                foreach (var marker in loc.Markers)
                {
                    if (!IsFreshMarker(marker)) continue;
                    if (!marker.IsVisible()) continue;
                    if (marker.Target.HasValue)
                        culpritList.Add(marker.Target.Value);
                }
            }

            return culpritList;
        }
    }

    // placedAt == 0 = restored from save/session-data, not placed live.
    private static bool IsFreshMarker(PingInfo m)
        => m.isMarker && m.placedAt > 0f && Time.realtimeSinceStartup - m.placedAt < FreshMarkerWindow;

    public override AlertReport GetReport()
    {
        return AlertReport.CulpritsAre(Culprits);
    }

    public override void OnClick()
    {
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            Multiplayer.session.locationPings.alertHidden = true;
        else
            base.OnClick();
    }
}
