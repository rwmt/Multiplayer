using HarmonyLib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using System.Linq;
using Multiplayer.Common.Util;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class ClientJoiningState : ClientBaseState
    {
        public ClientJoiningState(ConnectionBase connection) : base(connection)
        {
        }

        public override void StartState()
        {
            connection.Send(Packets.Client_Protocol, MpVersion.Protocol);
            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        [PacketHandler(Packets.Server_ProtocolOk)]
        public void HandleProtocolOk(ByteReader data)
        {
            bool hasPassword = data.ReadBool();

            if (hasPassword)
            {
                // Delay showing the window for better UX
                OnMainThread.Schedule(() => Find.WindowStack.Add(new GamePasswordWindow
                {
                    returnToServerBrowser = Find.WindowStack.WindowOfType<BaseConnectingWindow>().returnToServerBrowser
                }), 0.3f);
            }
            else
            {
                connection.Send(Packets.Client_Username, Multiplayer.username);
            }
        }

        [PacketHandler(Packets.Server_InitDataRequest)]
        public void HandleInitDataRequest(ByteReader data)
        {
            var includeConfigs = data.ReadBool();
            connection.SendFragmented(Packets.Client_InitData, PackInitData(includeConfigs));
        }

        public static byte[] PackInitData(bool includeConfigs)
        {
            return ByteWriter.GetBytes(
                JoinData.WriteServerData(includeConfigs),
                VersionControl.CurrentVersionString,
                Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId).ToList(),
                Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId).ToList(),
                MultiplayerData.localDefInfos.Select(p => (p.Key, p.Value.count, p.Value.hash)).ToList()
            );
        }

        [PacketHandler(Packets.Server_UsernameOk)]
        public void HandleUsernameOk(ByteReader data)
        {
            var writer = new ByteWriter();

            writer.WriteInt32(MultiplayerData.localDefInfos.Count);

            foreach (var kv in MultiplayerData.localDefInfos)
            {
                writer.WriteString(kv.Key);
                writer.WriteInt32(kv.Value.count);
                writer.WriteInt32(kv.Value.hash);
            }

            connection.SendFragmented(Packets.Client_JoinData, writer.ToArray());
        }

        [PacketHandler(Packets.Server_JoinData)]
        [IsFragmented]
        public void HandleJoinData(ByteReader data)
        {
            Multiplayer.session.gameName = data.ReadString();
            Multiplayer.session.playerId = data.ReadInt32();

            var remoteInfo = new RemoteData
            {
                remoteRwVersion = data.ReadString(),
                remoteMpVersion = data.ReadString(),
                remoteAddress = Multiplayer.session.address,
                remotePort = Multiplayer.session.port,
                remoteSteamHost = Multiplayer.session.steamHost
            };

            var defDiff = false;
            var defsData = new ByteReader(data.ReadPrefixedBytes());

            foreach (var local in MultiplayerData.localDefInfos)
            {
                var status = (DefCheckStatus)defsData.ReadByte();
                local.Value.status = status;

                if (status != DefCheckStatus.Ok)
                    defDiff = true;
            }

            JoinData.ReadServerData(data.ReadPrefixedBytes(), remoteInfo);

            // Delay showing the window for better UX
            OnMainThread.Schedule(Complete, 0.3f);

            void Complete()
            {
                if (JoinData.CompareToLocal(remoteInfo) && !defDiff)
                {
                    StartDownloading();
                    return;
                }

                if (defDiff)
                    Multiplayer.StopMultiplayerAndClearAllWindows();

                var defDiffStr = "\n\n" + MultiplayerData.localDefInfos
                    .Where(kv => kv.Value.status != DefCheckStatus.Ok)
                    .Take(10)
                    .Join(kv => $"{kv.Key}: {kv.Value.status}", "\n");

                Find.WindowStack.Add(new JoinDataWindow(remoteInfo){
                    connectAnywayDisabled = defDiff ? "MpMismatchDefsDiff".Translate() + defDiffStr : null,
                    connectAnywayCallback = StartDownloading
                });

                void StartDownloading()
                {
                    connection.Send(Packets.Client_WorldRequest);
                    connection.ChangeState(ConnectionStateEnum.ClientLoading);
                }
            }
        }
    }

}
