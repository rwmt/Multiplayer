using Multiplayer.Client;

namespace Tests;

public class CoreClrRuntimeTest
{
    [Test]
    public void StaticConstructorInvokerRestoresDefaultsOnEveryReload()
    {
        var reset = MpReflection.CreateStaticConstructorInvoker(typeof(StaticDefaults));
        var originalValues = StaticDefaults.Values;

        for (var reload = 0; reload < 2; reload++)
        {
            StaticDefaults.EffectsEnabled = false;
            StaticDefaults.Values.Clear();

            reset();

            Assert.That(StaticDefaults.EffectsEnabled, Is.True);
            Assert.That(StaticDefaults.Values, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(StaticDefaults.Values, Is.Not.SameAs(originalValues));
            originalValues = StaticDefaults.Values;
        }
    }

    private static class StaticDefaults
    {
        public static bool EffectsEnabled = true;
        public static readonly List<int> Values = new() { 1, 2, 3 };
    }


}
