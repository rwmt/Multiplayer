using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Common;

public class ChatCmdManager
{
    private readonly IDictionary<string, ChatCmdHandler> handlers = new Dictionary<string, ChatCmdHandler>();

    public void Handle(IChatSource source, string cmd)
    {
        var parts = cmd.Split(' ');
        if (handlers.TryGetValue(parts[0], out var handler))
        {
            if (handler.requiresHost && source is ServerPlayer { IsHost: false })
                source.SendMsg("No permission");
            else
                handler.Handle(source, parts.SubArray(1));
        }
        else
        {
            source.SendMsg("Invalid command. Use help or ? to list available commands.");
        }
    }

    public void AddCommandHandler(string name, ChatCmdHandler handler) => handlers[name] = handler;

    public IEnumerable<string> GetCommandNames() => handlers.Keys.OrderBy(name => name);

    public IEnumerable<ChatCmdInfo> GetCommandInfos()
    {
        return handlers
            .GroupBy(entry => entry.Value)
            .Select(group => new ChatCmdInfo(group.Key, group.Select(entry => entry.Key).ToArray()))
            .OrderBy(info => info.PrimaryName);
    }

    public bool TryGetCommandInfo(string name, out ChatCmdInfo info)
    {
        if (handlers.TryGetValue(name, out var handler))
        {
            info = new ChatCmdInfo(handler, handlers.Where(entry => entry.Value == handler).Select(entry => entry.Key).ToArray());
            return true;
        }

        info = null!;
        return false;
    }
}

public sealed class ChatCmdInfo(ChatCmdHandler handler, string[] names)
{
    public ChatCmdHandler Handler { get; } = handler;
    public string[] Names { get; } = names;
    public string PrimaryName => Names.First();
    public string DisplayNames => string.Join(", ", Names);
}

public abstract class ChatCmdHandler
{
    public bool requiresHost;

    public MultiplayerServer Server => MultiplayerServer.instance!;

    public virtual string Description => string.Empty;
    public virtual string Usage => string.Empty;

    public abstract void Handle(IChatSource source, string[] args);

    public void SendNoPermission(ServerPlayer player)
    {
        player.SendMsg("You don't have permission.");
    }

    public ServerPlayer? FindPlayer(string username)
    {
        return Server.GetPlayer(username);
    }
}

public class ChatCmdJoinPoint : ChatCmdHandler
{
    public override string Description => "Create a fresh join point immediately.";
    public override string Usage => "joinpoint";

    public ChatCmdJoinPoint()
    {
        requiresHost = true;
    }

    public override void Handle(IChatSource source, string[] args)
    {
        if (!Server.worldData.TryStartJoinPointCreation(true, sourcePlayer: source as ServerPlayer))
            source.SendMsg("Join point creation already in progress.");
    }
}

public class ChatCmdHelp : ChatCmdHandler
{
    public override string Description => "Show available commands or detailed help for one command.";
    public override string Usage => "help [command]";

    public override void Handle(IChatSource source, string[] args)
    {
        if (args.Length > 0)
        {
            if (Server.chatCmdManager.TryGetCommandInfo(args[0], out var command))
            {
                source.SendMsg($"Command: {command.DisplayNames}");

                if (!string.IsNullOrEmpty(command.Handler.Description))
                    source.SendMsg($"Description: {command.Handler.Description}");

                if (!string.IsNullOrEmpty(command.Handler.Usage))
                    source.SendMsg($"Usage: {command.Handler.Usage}");

                if (command.Handler.requiresHost)
                    source.SendMsg("Requires host permissions.");

                return;
            }

            source.SendMsg($"Unknown command '{args[0]}'. Use help to list available commands.");
            return;
        }

        source.SendMsg("Available commands:");
        foreach (var command in Server.chatCmdManager.GetCommandInfos())
        {
            var summary = command.Handler.Description;
            if (command.Handler.requiresHost)
                summary = string.IsNullOrEmpty(summary) ? "Requires host permissions." : $"{summary} Requires host permissions.";

            source.SendMsg($"- {command.DisplayNames}: {summary}");
        }

        source.SendMsg("Use help <command> for detailed usage.");
    }
}

public class ChatCmdKick : ChatCmdHandler
{
    public override string Description => "Disconnect a player by username.";
    public override string Usage => "kick <username>";

    public ChatCmdKick()
    {
        requiresHost = true;
    }

    public override void Handle(IChatSource source, string[] args)
    {
        if (args.Length < 1)
        {
            source.SendMsg("No username provided.");
            return;
        }

        var toKick = FindPlayer(args[0]);
        if (toKick == null)
        {
            source.SendMsg("Couldn't find the player.");
            return;
        }

        if (toKick.IsHost)
        {
            source.SendMsg("You can't kick the host.");
            return;
        }

        toKick.Disconnect(MpDisconnectReason.Kick);
    }
}

public class ChatCmdStop : ChatCmdHandler
{
    public override string Description => "Stop the standalone server.";
    public override string Usage => "stop";

    public ChatCmdStop()
    {
        requiresHost = true;
    }

    public override void Handle(IChatSource source, string[] args)
    {
        Server.running = false;
    }
}
