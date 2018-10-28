using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

    public class ChatCmdAutosave : ChatCmdHandler
    {
        public ChatCmdAutosave()
        {
            requiresHost = true;
        }

        public override void Handle(ServerPlayer player, string[] args)
        {
            Server.DoAutosave();
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

            if (toKick.Username == Server.hostUsername)
            {
                player.SendChat("You can't kick the host.");
                return;
            }

            toKick.Disconnect("MpKicked");
        }
    }
}
