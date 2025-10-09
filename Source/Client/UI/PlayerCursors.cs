using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class PlayerCursors
{
    private HashSet<int> lastSelected = new();
    private float lastSelectedSend;
    private int lastMap;

    private byte cursorSeq;
    private float lastCursorSend;

    public void SendVisuals()
    {
        if (Time.realtimeSinceStartup - lastCursorSend > 0.05f)
        {
            lastCursorSend = Time.realtimeSinceStartup;
            SendCursor();
        }

        if (Time.realtimeSinceStartup - lastSelectedSend > 0.2f)
        {
            lastSelectedSend = Time.realtimeSinceStartup;
            SendSelected();
        }
    }

    private void SendCursor()
    {
        var packet = new ClientCursorPacket(cursorSeq++);

        if (Find.CurrentMap != null && !WorldRendererUtility.WorldSelected)
        {
            packet.map = (byte)Find.CurrentMap.Index;

            var icon = Find.MapUI?.designatorManager?.SelectedDesignator?.icon;
            int iconId = icon == null ? 0 :
                !MultiplayerData.icons.Contains(icon) ? 0 : MultiplayerData.icons.IndexOf(icon);
            packet.icon = (byte)iconId;

            var mapPosition = UI.MouseMapPosition();
            packet.x = mapPosition.x;
            packet.z = mapPosition.z;

            if (Find.Selector.dragBox.IsValidAndActive)
            {
                var dragVec = Find.Selector.dragBox.start;
                packet.dragX = dragVec.x;
                packet.dragZ = dragVec.z;
            }
        }

        Multiplayer.Client.Send(packet, reliable: false);
    }

    private void SendSelected()
    {
        if (Current.ProgramState != ProgramState.Playing) return;

        var writer = new ByteWriter();

        int mapId = Find.CurrentMap?.Index ?? -1;
        if (WorldRendererUtility.WorldSelected) mapId = -1;

        bool reset = false;

        if (mapId != lastMap)
        {
            reset = true;
            lastMap = mapId;
            lastSelected.Clear();
        }

        var selected = new HashSet<int>(Find.Selector.selected.OfType<Thing>().Select(t => t.thingIDNumber));

        var add = new List<int>(selected.Except(lastSelected));
        var remove = new List<int>(lastSelected.Except(selected));

        if (!reset && add.Count == 0 && remove.Count == 0) return;

        writer.WriteBool(reset);
        writer.WritePrefixedInts(add);
        writer.WritePrefixedInts(remove);

        lastSelected = selected;

        Multiplayer.Client.Send(Packets.Client_Selected, writer.ToArray());
    }
}
