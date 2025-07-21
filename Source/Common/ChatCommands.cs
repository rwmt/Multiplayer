using System.Collections.Generic;

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
                source.SendChat("No permission");
            else
                handler.Handle(source, parts.SubArray(1));
        }
        else
        {
            source.SendChat("Invalid command");
        }
    }

    public void AddCommandHandler(string name, ChatCmdHandler handler) => handlers[name] = handler;
}

public abstract class ChatCmdHandler
{
    public bool requiresHost;

    public MultiplayerServer Server => MultiplayerServer.instance!;

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
    public ChatCmdJoinPoint()
    {
        requiresHost = true;
    }

    public override void Handle(IChatSource source, string[] args)
    {
        if (!Server.worldData.TryStartJoinPointCreation(true))
            source.SendMsg("Join point creation already in progress.");
    }
}

public class ChatCmdKick : ChatCmdHandler
{
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
    public ChatCmdStop()
    {
        requiresHost = true;
    }

    public override void Handle(IChatSource source, string[] args)
    {
        Server.running = false;
    }
}
