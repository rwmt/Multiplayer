using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;

namespace Tests;

public class SerializationTest
{
    private static T? RoundtripSerialization<T>(T? obj)
    {
        var writer = new ByteWriter();
        SyncSerialization.WriteSync(writer, obj);
        return SyncSerialization.ReadSync<T>(new ByteReader(writer.ToArray()));
    }

    [Test]
    public void TestString()
    {
        Assert.That(RoundtripSerialization("abc"), Is.EqualTo("abc"));
    }

    [Test]
    public void TestStringList()
    {
        List<string> input = ["abc", "def"];
        Assert.That(RoundtripSerialization(input), Is.EqualTo(input));
    }

    [Test]
    public void TestSyncWorkers()
    {
        SyncSerialization.syncTree = new SyncWorkerDictionaryTree
        {
            {
                (ByteWriter writer, IA a) =>
                {
                    SyncSerialization.WriteSync(writer, a is A);
                    SyncSerialization.WriteSyncObject(writer, a, a.GetType());
                },
                (ByteReader reader) =>
                {
                    if (SyncSerialization.ReadSync<bool>(reader))
                        return SyncSerialization.ReadSync<A>(reader)!;
                    return SyncSerialization.ReadSync<Z>(reader)!;
                }
            },
            {
                (SyncWorker worker, ref A a) =>
                {
                    worker.Bind(ref a.a);
                }, true, true
            },
            {
                (SyncWorker worker, ref Z z) =>
                {
                    worker.Bind(ref z.z);
                }, true, true
            },
        };

        {
            var input = new A { a = 1 };
            Assert.That(
                RoundtripSerialization(input),
                Is.EqualTo(input)
            );
            Assert.That(
                RoundtripSerialization<IA>(input),
                Is.EqualTo(input)
            );
        }

        {
            var input = new Z { z = 2 };
            Assert.That(
                RoundtripSerialization<IA>(input),
                Is.EqualTo(input)
            );
        }
    }

    public interface IA;

    // Using structs so that equality is by value which simplifies the test code
    public struct A : IA
    {
        public int a;
    }

    public struct Z : IA
    {
        public int z;
    }
}
