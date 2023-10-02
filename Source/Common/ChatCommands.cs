namespace Multiplayer.Common
{
    public abstract class ChatCmdHandler
    {
        public bool requiresHost;

        public MultiplayerServer Server => MultiplayerServer.instance;

        public abstract void Handle(IChatSource source, string[] args);

        public void SendNoPermission(ServerPlayer player)
        {
            player.SendChat("You don't have permission.");
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
}
