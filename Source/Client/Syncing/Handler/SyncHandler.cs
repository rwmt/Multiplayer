using Multiplayer.API;
using Multiplayer.Common;
using System;

namespace Multiplayer.Client
{
    public abstract class SyncHandler
    {
        public int syncId = -1;

        public SyncContext context;
        public bool debugOnly;
        public bool hostOnly;
        public int version;

        protected SyncHandler()
        {
        }

        protected void SendSyncCommand(int mapId, ByteWriter data)
        {
            if (!Multiplayer.GhostMode)
                Multiplayer.Client.SendCommand(CommandType.Sync, mapId, data.ToArray());
        }

        public abstract void Handle(ByteReader data);

        public abstract void Validate();

        protected void ValidateType(string desc, SyncType type)
        {
            if (type.type != null && !SyncSerialization.CanHandle(type))
                throw new Exception($"Sync handler uses a non-serializable type: {type.type}. Details: {desc}");
        }
    }

}
