using System.Text;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

public class PacketTest
{
    private static IEnumerable<IPacket> RoundtripPackets()
    {
        yield return new ClientCursorPacket(1);

        yield return new ClientCursorPacket(2)
        {
            map = 0,
            icon = 42,
            x = 12.3f,
            z = -7.7f
        };

        yield return new ClientCursorPacket(3)
        {
            map = 2,
            icon = 3,
            x = 1.5f,
            z = 2.5f,
            dragX = 4.4f,
            dragZ = 5.5f
        };

        yield return new ServerCursorPacket(4, new ClientCursorPacket(123));

        yield return new ClientCommandPacket(CommandType.GlobalTimeSpeed, 1, [1, 2, 3]);
        yield return new ClientCommandPacket(CommandType.PlayerCount, 123, []);
        yield return new ClientCommandPacket(CommandType.DebugTools, 0, [255, 0, 255]);

        yield return new ServerCommandPacket
        {
            type = CommandType.Sync,
            ticks = 100,
            factionId = 1,
            mapId = 2,
            playerId = 3,
            data = [42]
        };

        yield return new ServerCommandPacket
        {
            type = CommandType.CreateJoinPoint,
            ticks = 200,
            factionId = 5,
            mapId = 10,
            playerId = 7,
            data = []
        };

        yield return new ClientPingLocPacket(0, 0, 0, 0f, 0f, 0f);

        yield return new ClientPingLocPacket(1, 42, 3, 10.5f, -2.25f, 99.9f);

        yield return new ServerPingLocPacket(7,
            new ClientPingLocPacket(5, 123, 1, 1.23f, 4.56f, 7.89f));

        yield return ServerPlayerListPacket.List([
            new ServerPlayerListPacket.PlayerInfo
            {
                id = 1,
                username = "Alice",
                latency = 42,
                type = PlayerType.Normal,
                status = PlayerStatus.Playing,
                steamId = 123456789,
                steamPersonaName = "AliceSteam",
                ticksBehind = 0,
                simulating = false,
                r = 255, g = 0, b = 0,
                factionId = 10
            },
            new ServerPlayerListPacket.PlayerInfo
            {
                id = 2,
                username = "Bob",
                latency = 99,
                type = PlayerType.Arbiter,
                status = PlayerStatus.Desynced,
                steamId = 987654321,
                steamPersonaName = "BobBot",
                ticksBehind = 5,
                simulating = true,
                r = 0, g = 255, b = 0,
                factionId = 20
            }
        ]);

        yield return ServerPlayerListPacket.Add(new ServerPlayerListPacket.PlayerInfo
        {
            id = 3,
            username = "Charlie",
            latency = 10,
            type = PlayerType.Steam,
            status = PlayerStatus.Simulating,
            steamId = 111222333,
            steamPersonaName = "CharlieC",
            ticksBehind = 1,
            simulating = false,
            r = 0, g = 0, b = 255,
            factionId = 30
        });

        yield return ServerPlayerListPacket.Remove(99);

        yield return ServerPlayerListPacket.Latencies([
            new ServerPlayerListPacket.PlayerLatency
            {
                playerId = 1,
                latency = 42,
                ticksBehind = 0,
                simulating = false,
                frameTime = 16.67f
            },
            new ServerPlayerListPacket.PlayerLatency
            {
                playerId = 2,
                latency = 77,
                ticksBehind = 3,
                simulating = true,
                frameTime = 33.33f
            }
        ]);

        yield return ServerPlayerListPacket.Status(1, PlayerStatus.Playing);

        var sampleOpinion = new SyncOpinion
        {
            startTick = 12345,
            commandRandomStates = [1, 2, 3],
            worldRandomStates = [10, 20, 30],
            mapRandomStates =
            [
                new MapRandomState { mapId = 1, randomStates = [111, 222] },
                new MapRandomState { mapId = 2, randomStates = [333, 444, 555] }
            ],
            traceHashes = [999, 888, 777],
            simulating = true,
            roundMode = RoundModeEnum.ToNearest
        };

        yield return new ClientSyncInfoPacket { SyncOpinion = sampleOpinion };
        yield return new ServerSyncInfoPacket { SyncOpinion = sampleOpinion };

        yield return new ClientSetFactionPacket(123, 123);
        yield return new ServerSetFactionPacket(123, 123);

        yield return new ClientKeepAlivePacket(999, 90, false, 1111);
        yield return new ServerKeepAlivePacket(256);

        yield return new ServerTimeControlPacket(2_123_456, 1000, 1.2f);

        yield return new ServerFreezePacket(true, 1234);
        yield return new ServerFreezePacket(false, 9876);
        yield return ClientFreezePacket.Freeze();
        yield return ClientFreezePacket.Unfreeze();

        yield return ServerChatPacket.Create("");
        yield return ServerChatPacket.Create("ABC123!@#");

        yield return ClientChatPacket.Create("");
        yield return ClientChatPacket.Create("ABC123!@#");

        yield return new ClientProtocolPacket(50);

        yield return new ServerProtocolOkPacket(true);
        yield return new ServerProtocolOkPacket(false);

        yield return new ClientUsernamePacket("username");
        yield return new ClientUsernamePacket("username", "password");

        yield return new ClientJoinDataPacket
        {
            modCtorRoundMode = RoundModeEnum.Downward,
            staticCtorRoundMode = RoundModeEnum.TowardZero,
            defInfos =
            [
                new KeyedDefInfo { name = "key", count = 1, hash = 123 },
                new KeyedDefInfo { name = "key2", count = 0, hash = 0 }
            ]
        };

        yield return new ServerJoinDataPacket
        {
            gameName = "GameName",
            playerId = 1,
            rwVersion = "1.6.4566",
            mpVersion = "0.11.0+123456",
            defStatus =
            [
                DefCheckStatus.Ok, DefCheckStatus.Ok, DefCheckStatus.Count_Diff, DefCheckStatus.Hash_Diff,
                DefCheckStatus.Not_Found
            ],
            rawServerInitData = [1, 2, 3, 4, 5]
        };

        yield return new ClientFrameTimePacket(0f);
        yield return new ClientFrameTimePacket(0.5f);
        yield return new ClientFrameTimePacket(123f);

        yield return new ServerInitDataRequestPacket(true);
        yield return new ServerInitDataRequestPacket(false);
    }

    [TestCaseSource(nameof(RoundtripPackets))]
    public void TestRoundtrip(IPacket original)
    {
        var binder = RuntimeBinderOf(original);
        var serialize = binder.Serialize(original);
        var deserialized = binder.Deserialize(serialize);

        var valueFields = original.GetType().GetFields().Where(f => f.FieldType.IsValueType).ToList();
        var rawFields = original.GetType().GetFields().Where(f => !f.FieldType.IsValueType).ToList();
        if (rawFields.Count > 0)
        {
            // Default record's equality compares reference types by reference meaning e.g., a byte[] of [1,2,3] isn't
            // equal to another byte[] of [1, 2, 3] unless it's the same instance (the same reference). In case
            // there are such fields, fallback to comparing the raw serialized bytes, and each field individually.
            var serializedAgain = binder.Serialize(deserialized);
            Assert.That(serialize, Is.EqualTo(serializedAgain));

            foreach (var valueField in valueFields)
                Assert.That(valueField.GetValue(deserialized), Is.EqualTo(valueField.GetValue(original)));
            // Unlike byte[].Equals, Is.EqualTo compares by value, not by reference.
            foreach (var rawField in rawFields)
                Assert.That(rawField.GetValue(deserialized), Is.EqualTo(rawField.GetValue(original)));
        }
        else
        {
            Assert.That(deserialized, Is.EqualTo(original));
        }
    }

    [Test]
    public async Task SnapshotBinaryRepresentation()
    {
        var packetsByType = RoundtripPackets().GroupBy(p => p.GetType());
        foreach (var packetsOfType in packetsByType)
        {
            var binder = RuntimeBinderOf(packetsOfType.Key);
            var text = new StringBuilder();
            foreach (var packet in packetsOfType)
            {
                var serialized = binder.Serialize(packet);
                text.Append(BitConverter.ToString(serialized));
                if (serialized.Length > 32)
                    text.Append($" ({serialized.Length} bytes)");
                text.AppendLine();
            }

            await Verify(text).UseDirectory("packet-serializations").UseFileName(packetsOfType.Key.Name).DisableDiff()
                .AutoVerify(includeBuildServer: false);
        }
    }

    private static Binder<IPacket> RuntimeBinderOf(IPacket p) => RuntimeBinderOf(p.GetType());
    private static Binder<IPacket> RuntimeBinderOf(Type type) => (PacketBuffer buf, ref IPacket? packet) =>
    {
        // Normally packets are structs, so they can't be null, but here we are using them dynamically, so we need to
        // initialize them.
        packet ??= (IPacket)Activator.CreateInstance(type)!;
        packet.Bind(buf);
    };
}
