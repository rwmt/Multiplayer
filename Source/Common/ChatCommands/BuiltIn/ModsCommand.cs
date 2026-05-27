using System;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common.ChatCommands;

[ChatCommand("mods", Description = "Show the server mod list summary.", Usage = "mods")]
public class ModsCommand : ChatCommand
{
    private const int MaxModsToPrint = 20;

    public override void Execute(ChatCommandContext context)
    {
        var initData = Server.InitData;
        if (initData == null)
        {
            context.Source.SendMsg("Mod data is not available yet.");
            return;
        }

        try
        {
            var mods = ClientInitDataPacket.ModData.ListBinder.Deserialize(initData.RawData);

            context.Source.SendMsg($"RimWorld: {initData.RwVersion}");
            context.Source.SendMsg($"Mods ({mods.Count}):");

            foreach (var mod in mods.Take(MaxModsToPrint))
                context.Source.SendMsg($"- {mod.name} ({mod.packageIdNonUnique})");

            if (mods.Count > MaxModsToPrint)
                context.Source.SendMsg($"... and {mods.Count - MaxModsToPrint} more.");
        }
        catch (Exception e)
        {
            ServerLog.Error($"Failed to read server mod data: {e}");
            context.Source.SendMsg("Could not read mod data.");
        }
    }
}
