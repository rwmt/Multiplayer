using System.IO.Compression;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

public class ReaderBoundsTest
{
    private static ByteReader LengthPrefixed(int len, int payload = 8) =>
        new(ByteWriter.GetBytes(len).Concat(new byte[payload]).ToArray());

    [TestCase(int.MaxValue)]
    [TestCase(9)]
    [TestCase(-1)]
    public void ReadRawRejectsLengthOutsideData(int len)
    {
        var reader = new ByteReader(new byte[8]);
        Assert.Throws<ReaderException>(() => reader.ReadRaw(len));
        Assert.That(reader.Position, Is.Zero);
    }

    [Test]
    public void ReadRawReadsExactLength()
    {
        var reader = new ByteReader([1, 2, 3, 4]);
        Assert.That(reader.ReadRaw(4), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(reader.Left, Is.Zero);
    }

    [Test]
    public void PrefixedReadsRejectHugeLengths()
    {
        Assert.Throws<ReaderException>(() => LengthPrefixed(int.MaxValue).ReadPrefixedBytes());
        Assert.Throws<ReaderException>(() => LengthPrefixed(int.MaxValue).ReadPrefixedInts());
        Assert.Throws<ReaderException>(() => LengthPrefixed(int.MaxValue).ReadPrefixedUInts());
        Assert.Throws<ReaderException>(() => LengthPrefixed(int.MaxValue).ReadPrefixedULongs());
        Assert.Throws<ReaderException>(() => LengthPrefixed(int.MaxValue).ReadPrefixedStrings());
        Assert.Throws<ReaderException>(() => LengthPrefixed(-5).ReadPrefixedUInts());
        // 8 bytes hold 2 uints, not 3
        Assert.Throws<ReaderException>(() => LengthPrefixed(3).ReadPrefixedUInts());
        Assert.That(LengthPrefixed(2).ReadPrefixedUInts(), Is.EqualTo(new uint[] { 0, 0 }));
    }

    [Test]
    public void BindBytesEnforcesMaxLength()
    {
        var bytes = new byte[100];
        var writer = new ByteWriter();
        new PacketWriter(writer).BindBytes(ref bytes, maxLength: -1);

        byte[] read = [];
        Assert.Throws<ReaderException>(() => new PacketReader(new ByteReader(writer.ToArray())).BindBytes(ref read, 99));

        new PacketReader(new ByteReader(writer.ToArray())).BindBytes(ref read, 100);
        Assert.That(read, Has.Length.EqualTo(100));

        Assert.Throws<WriterException>(() => new PacketWriter(new ByteWriter()).BindBytes(ref bytes, 99));
    }

    [Test]
    public void BindBytesRejectsLengthBeyondData()
    {
        byte[] read = [];
        Assert.Throws<ReaderException>(() =>
            new PacketReader(LengthPrefixed(int.MaxValue)).BindBytes(ref read, maxLength: -1));
    }

    [Test]
    public void ListBindDoesNotPreallocateFromLength()
    {
        List<int> list = [];
        // A huge claimed count fails on the missing data instead of preallocating
        Assert.Catch(() =>
            new PacketReader(LengthPrefixed(int.MaxValue - 1)).Bind(ref list, BinderOf.Int(), maxLength: int.MaxValue));
    }

    [Test]
    public void GzippedRejectsDecompressionBomb()
    {
        var binder = BinderOf.Identity<Blob>().Gzipped(maxLength: 1 << 20, maxDecompressedLength: 1 << 16);

        var small = new Blob { data = new byte[1000] };
        Assert.That(binder.Deserialize(binder.Serialize(small)).data, Has.Length.EqualTo(1000));

        // Zeros compress to almost nothing
        var bomb = new Blob { data = new byte[1 << 20] };
        var serialized = binder.Serialize(bomb);
        Assert.That(serialized, Has.Length.LessThan(1 << 16));
        Assert.Throws<ReaderException>(() => binder.Deserialize(serialized));
    }

    [Test]
    public void ModListLimitsMatchBetweenInitAndJoinData()
    {
        // Random config contents, so that they don't compress too well
        var random = new Random(0);
        string RandomText(int bytes)
        {
            var data = new byte[bytes];
            random.NextBytes(data);
            return Convert.ToBase64String(data);
        }

        var mods = Enumerable.Range(0, 1000).Select(i => new ClientInitDataPacket.ModData
        {
            packageIdNonUnique = $"mod.{i}",
            name = $"Mod {i}",
            files = [new ClientInitDataPacket.ModFile { path = "Assemblies/Mod.dll", hash = i }],
            config = new ClientInitDataPacket.ModConfig { fileName = "Mod_Mod.xml", contents = RandomText(1000) }
        }).ToList();

        var initData = new ClientInitDataPacket
        {
            rwVersion = "1.6", debugOnlySyncCmds = [], hostOnlySyncCmds = [], defInfos = [], Mods = mods
        };
        // Bigger than the old 32 KiB and 512 KiB limits
        Assert.That(initData.rawMods, Has.Length.GreaterThan(1 << 19));

        var roundtrip = Roundtrip(initData);
        var joinData = Roundtrip(new ServerJoinDataPacket
        {
            gameName = "", rwVersion = "", mpVersion = "", defStatus = [], missingDefs = [],
            rawServerInitData = roundtrip.rawMods
        });
        Assert.That(joinData.ServerInitData, Has.Count.EqualTo(mods.Count));
    }

    private static T Roundtrip<T>(T packet) where T : struct, IPacket
    {
        var writer = new ByteWriter();
        packet.Bind(new PacketWriter(writer));
        var result = default(T);
        result.Bind(new PacketReader(new ByteReader(writer.ToArray())));
        return result;
    }

    private struct Blob : IPacketBufferable
    {
        public byte[] data;
        public void Bind(PacketBuffer buf) => buf.BindBytes(ref data, maxLength: -1);
    }
}
