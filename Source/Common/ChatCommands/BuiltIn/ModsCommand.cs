using System;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common.ChatCommands;

public readonly record struct ModsCommandArgs(
    [ChatArgument("page")] int Page = 1,
    [ChatArgument("amount")] int Amount = 20
);

[ChatCommand("mods", Description = "Show the server mod list summary.", Usage = "mods [page] [amount]")]
public class ModsCommand : ChatCommand<ModsCommandArgs>
{
    protected override void Execute(ChatCommandContext context, ModsCommandArgs args)
    {
        if (args.Page < 1 || args.Amount < 1)
        {
            context.Source.SendMsg("Usage: mods [page] [amount]");
            return;
        }

        var initData = Server.InitData;
        if (initData == null)
        {
            context.Source.SendMsg("Mod data is not available yet.");
            return;
        }

        try
        {
            var mods = ClientInitDataPacket.ModData.ListBinder.Deserialize(initData.RawData);
            var totalPages = Math.Max(1, (int)Math.Ceiling(mods.Count / (double)args.Amount));
            if (args.Page > totalPages)
            {
                context.Source.SendMsg($"Page {args.Page} is out of range. Last page is {totalPages}.");
                return;
            }

            var pageMods = mods
                .Skip((args.Page - 1) * args.Amount)
                .Take(args.Amount)
                .ToList();

            context.Source.SendMsg($"RimWorld: {initData.RwVersion}");
            context.Source.SendMsg(totalPages == 1
                ? $"Mods ({mods.Count}):"
                : $"Mods ({mods.Count}), page {args.Page}/{totalPages}:"
            );

            foreach (var mod in pageMods)
                context.Source.SendMsg($"- {mod.name} ({mod.packageIdNonUnique})");
        }
        catch (Exception e)
        {
            ServerLog.Error($"Failed to read server mod data: {e}");
            context.Source.SendMsg("Could not read mod data.");
        }
    }
}
