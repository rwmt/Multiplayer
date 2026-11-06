using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.Client.Util;

namespace Tests;

[NonParallelizable]
public class HashCodeTranspilerTest
{
    [Test]
    public void PatchingGameCallerPreservesExistingDictionaryKeys()
    {
        var key = (new object(), "ingredients");
        var dictionary = new Dictionary<(object, string), string> { [key] = "cached" };
        var tupleHash = key.GetHashCode();
        var systemHash = HashCode.Combine(17, 42);
        var target = AccessTools.Method(typeof(HashCodeTranspilerTest), nameof(GameSeed));
        var harmony = new Harmony("Tests.GameHashCalls");

        try
        {
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(HashCodeTranspiler), nameof(HashCodeTranspiler.ReplaceCalls)));

            Assert.That(target.Invoke(null, new object[] { 17, 42 }), Is.EqualTo(DeterministicHash.HashCombineInt(17, 42)));
            Assert.That(HashCode.Combine(17, 42), Is.EqualTo(systemHash));
            Assert.That(key.GetHashCode(), Is.EqualTo(tupleHash));
            Assert.That(dictionary[key], Is.EqualTo("cached"));
            Assert.That(dictionary.Remove(key), Is.True);
        }
        finally
        {
            harmony.Unpatch(target, AccessTools.Method(typeof(HashCodeTranspiler), nameof(HashCodeTranspiler.ReplaceCalls)));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int GameSeed(int a, int b) => HashCode.Combine(a, b);

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public void ReplacesIntegerCombinesWithoutDroppingLabels(int arity)
    {
        var combine = typeof(HashCode).GetMethods().Single(m => m.Name == "Combine" && m.GetGenericArguments().Length == arity)
            .MakeGenericMethod(Enumerable.Repeat(typeof(int), arity).ToArray());
        var generator = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator();
        var label = generator.DefineLabel();
        var call = new CodeInstruction(OpCodes.Call, combine);
        call.labels.Add(label);

        var replaced = HashCodeTranspiler.ReplaceCalls(new[] { call }).Single();

        Assert.That(replaced.labels, Does.Contain(label));
        var replacement = (MethodInfo)replaced.operand;
        Assert.That(replacement.DeclaringType, Is.EqualTo(typeof(DeterministicHash)));
        Assert.That(replacement.GetParameters().Select(p => p.ParameterType), Is.EqualTo(Enumerable.Repeat(typeof(int), arity)));
    }

    [Test]
    public void LeavesMixedTypeCombinesAlone()
    {
        var combine = typeof(HashCode).GetMethods().Single(m => m.Name == "Combine" && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(typeof(int), typeof(string));
        var call = new CodeInstruction(OpCodes.Call, combine);

        Assert.That(HashCodeTranspiler.ReplaceCalls(new[] { call }).Single().operand, Is.EqualTo(combine));
    }
}
