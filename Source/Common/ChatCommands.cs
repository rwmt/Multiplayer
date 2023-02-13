namespace Multiplayer.Common
{
    public abstract class ChatCmdHandler
    {
        public bool requiresHost;

        public MultiplayerServer Server => MultiplayerServer.instance;

        public abstract void Handle(ServerPlayer player, string[] args);

        public void SendNoPermission(ServerPlayer player)
        {
            player.SendChat("You don't have permission.");
        }

        public ServerPlayer FindPlayer(string username)
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

        public override void Handle(ServerPlayer player, string[] args)
        {
            if (!Server.TryStartJoinPointCreation(true))
                player.SendChat("Join point creation already in progress.");
        }
    }

    public class ChatCmdKick : ChatCmdHandler
    {
        public ChatCmdKick()
        {
            requiresHost = true;
        }

        public override void Handle(ServerPlayer player, string[] args)
        {
            if (args.Length < 1)
            {
                player.SendChat("No username provided.");
                return;
            }

            var toKick = FindPlayer(args[0]);
            if (toKick == null)
            {
                player.SendChat("Couldn't find the player.");
                return;
            }

            if (toKick.IsHost)
            {
                player.SendChat("You can't kick the host.");
                return;
            }

            toKick.Disconnect(MpDisconnectReason.Kick);
        }
    }
}
