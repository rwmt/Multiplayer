using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MultiplayerLoader;

namespace Tests;

[NonParallelizable]
public class ReflectionOnlyUnityResolverTest
{
    [Test]
    public void ReusesAnExistingUnityAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("UnityEngine.Existing" + Guid.NewGuid().ToString("N")) { Version = new Version(1, 0) },
            AssemblyBuilderAccess.Run);
        var resolver = new ReflectionOnlyUnityResolver(Path.GetTempPath());
        // CoreCLR exposes a runtime wrapper rather than the AssemblyBuilder itself.
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Single(candidate => candidate.FullName == assembly.FullName);

        Assert.That(resolver.Resolve(null, new ResolveEventArgs(assembly.FullName!)), Is.SameAs(loaded));
    }

    [Test]
    public void LeavesNonUnityAssembliesToTheirOwnResolver()
    {
        var resolver = new ReflectionOnlyUnityResolver(Path.GetTempPath());

        Assert.That(resolver.Resolve(null, new ResolveEventArgs(typeof(object).Assembly.FullName!)), Is.Null);
        Assert.That(resolver.Resolve(null, new ResolveEventArgs("Unknown.Mod, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")), Is.Null);
    }

    [Test]
    public void MissingUnityAssemblyIsNotSubstituted()
    {
        var resolver = new ReflectionOnlyUnityResolver(Path.GetTempPath());

        Assert.That(resolver.Resolve(null, new ResolveEventArgs(
            "UnityEngine.Missing" + Guid.NewGuid().ToString("N") + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")), Is.Null);
    }

    [Test]
    public void LoadsALateUnityDependencyFromTheManagedDirectory()
    {
        using var fixture = new UnityAssemblyFixture();
        var resolver = new ReflectionOnlyUnityResolver(fixture.Directory);

        var result = resolver.Resolve(null, new ResolveEventArgs(fixture.Identity.FullName));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GetName().FullName, Is.EqualTo(fixture.Identity.FullName));
        Assert.That(result.ReflectionOnly, Is.False);
        Assert.That(resolver.Resolve(null, new ResolveEventArgs(fixture.Identity.FullName)), Is.SameAs(result));
    }

    [Test]
    public void RejectsAnAssemblyWithADifferentVersion()
    {
        using var fixture = new UnityAssemblyFixture();
        var resolver = new ReflectionOnlyUnityResolver(fixture.Directory);
        var requested = new AssemblyName(fixture.Identity.FullName) { Version = new Version(9, 0) };

        Assert.That(resolver.Resolve(null, new ResolveEventArgs(requested.FullName)), Is.Null);
    }

    private sealed class UnityAssemblyFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "mp-refonly-" + Guid.NewGuid().ToString("N"));
        public AssemblyName Identity { get; }

        public UnityAssemblyFixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            var name = "UnityEngine.Late" + Guid.NewGuid().ToString("N");
            var tree = CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")] public class FixtureType { }");
            var compilation = CSharpCompilation.Create(name, [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var file = Path.Combine(Directory, name + ".dll");
            using (var output = File.Create(file))
            {
                var result = compilation.Emit(output);
                Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            }
            Identity = AssemblyName.GetAssemblyName(file);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
