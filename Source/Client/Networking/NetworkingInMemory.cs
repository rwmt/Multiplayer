using System;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Networking
{
    public class LocalConnection : ConnectionBase
    {
        public override int Latency { get => 0; set { } }
        public LocalConnection remoteConn;
        private bool isClient;
        private Action<Action> enqueue;

        private LocalConnection(string username, bool isClient, Action<Action> enqueue)
        {
            this.username = username;
            this.isClient = isClient;
            this.enqueue = enqueue;
        }

        public static LocalConnection Client(string username) => new(username, true, Multiplayer.LocalServer.Enqueue);
        public static LocalConnection Server(string username) => new(username, false, OnMainThread.Enqueue);

        public static (LocalConnection client, LocalConnection server) Paired(string username)
        {
            var localClient = Client(username);
            var localServer = Server(username);

            localClient.remoteConn = localServer;
            localServer.remoteConn = localClient;
            return (localClient, localServer);
        }

        protected override void SendRaw(byte[] raw, bool reliable = true)
        {
            enqueue(() =>
            {
                try
                {
                    remoteConn.HandleReceiveRaw(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {remoteConn}: {e}");
                }
            });
        }

        protected override void OnClose()
        {
        }

        public override string ToString() => isClient ? "LocalClientConn" : "LocalServerConn";
    }
}
