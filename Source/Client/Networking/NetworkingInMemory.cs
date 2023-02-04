using Multiplayer.Common;
using System;
using Verse;

namespace Multiplayer.Client.Networking
{
    public class LocalClientConnection : ConnectionBase
    {
        public LocalServerConnection serverSide;

        public override int Latency { get => 0; set { } }

        public LocalClientConnection(string username)
        {
            this.username = username;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            Multiplayer.LocalServer.Enqueue(() =>
            {
                try
                {
                    serverSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {serverSide}: {e}");
                }
            });
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalClientConn";
        }
    }

    public class LocalServerConnection : ConnectionBase
    {
        public LocalClientConnection clientSide;

        public override int Latency { get => 0; set { } }

        public LocalServerConnection(string username)
        {
            this.username = username;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    clientSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {clientSide}: {e}");
                }
            });
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalServerConn";
        }
    }
}
