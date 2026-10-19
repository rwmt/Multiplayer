using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[TestFixture]
public class SelectedPacketTest
{
    // What the receiving client does with the packets
    private static HashSet<int> Apply(HashSet<int> selection, IEnumerable<ClientSelectedPacket> packets)
    {
        foreach (var packet in packets)
        {
            var read = default(ClientSelectedPacket);
            read.Bind(new PacketReader(new ByteReader(packet.Serialize().data)));

            if (read.reset) selection.Clear();
            selection.UnionWith(read.newlySelectedIds!);
            selection.ExceptWith(read.unselectedIds!);
        }

        return selection;
    }

    [Test]
    public void SmallChangeIsOnePacket()
    {
        var packets = ClientSelectedPacket.Split(true, [1, 2], [3]).ToList();

        Assert.That(packets, Has.Count.EqualTo(1));
        Assert.That(packets[0].reset, Is.True);
        Assert.That(packets[0].newlySelectedIds, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(packets[0].unselectedIds, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void ResetWithoutChangesIsStillSent()
    {
        var packets = ClientSelectedPacket.Split(true, [], []).ToList();

        Assert.That(packets, Has.Count.EqualTo(1));
        Assert.That(Apply([5], packets), Is.Empty);
    }

    [Test]
    public void BigSelectionIsSplitWithinTheLimit()
    {
        var added = Enumerable.Range(1000, 450).ToArray();
        var removed = Enumerable.Range(0, 10).ToArray();

        var packets = ClientSelectedPacket.Split(false, added, removed).ToList();

        Assert.That(packets, Has.Count.EqualTo(3));
        Assert.That(packets.All(p => p.newlySelectedIds.Length <= ClientSelectedPacket.MaxIds));
        Assert.That(packets.Any(p => p.reset), Is.False);
        Assert.That(Apply([..removed, 9999], packets), Is.EquivalentTo(added.Append(9999)));
    }

    [Test]
    public void OnlyTheFirstPartResets()
    {
        var added = Enumerable.Range(0, 2 * ClientSelectedPacket.MaxIds + 1).ToArray();

        var packets = ClientSelectedPacket.Split(true, added, []).ToList();

        Assert.That(packets.Select(p => p.reset), Is.EqualTo(new[] { true, false, false }));
        Assert.That(Apply([-1], packets), Is.EquivalentTo(added));
    }
}
