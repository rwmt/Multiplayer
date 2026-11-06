using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tests;

// Compile the production loader source with small widget/network doubles. The normal test
// suite cannot load Unity's native UI initialization, and no game installation is needed here.
[TestFixture]
public class RitualRoleCacheTest
{
    private AssemblyLoadContext context = null!;
    private Type scenarios = null!;

    [OneTimeSetUp]
    public void CompileLoader()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var options = new CSharpParseOptions(LanguageVersion.CSharp12);
        var compilation = CSharpCompilation.Create(
            "RitualRoleCacheRegression",
            [CSharpSyntaxTree.ParseText(ReadResource("Tests.SyncRituals.cs"), options),
             CSharpSyntaxTree.ParseText(ReadResource("Tests.RitualRoleCacheFixtures.txt"), options)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyBytes = new MemoryStream();
        var emitted = compilation.Emit(assemblyBytes);
        Assert.That(emitted.Success, Is.True,
            string.Join(Environment.NewLine, emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        assemblyBytes.Position = 0;
        context = new AssemblyLoadContext("Ritual role cache tests", isCollectible: true);
        scenarios = context.LoadFromStream(assemblyBytes).GetType("RitualCacheScenarios", throwOnError: true)!;
        Invoke("Initialize");
    }

    [OneTimeTearDown]
    public void UnloadLoader() => context?.Unload();

    [TestCase(false)]
    [TestCase(true)]
    public void RemoteAssignment_WithoutDrawingDialog_UsesCurrentRoleGroups(bool psychic)
    {
        var result = Replay(psychic, initialGroup: "");
        Assert.That(result, Is.EqualTo(new[] { "True", "current ritual", "1" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RemoteAssignment_AfterDrawingAnotherDialog_DiscardsStaleGroups(bool psychic)
    {
        var result = Replay(psychic, initialGroup: "another ritual");
        Assert.That(result, Is.EqualTo(new[] { "True", "current ritual", "1" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LocalUiCommand_WhenEnqueued_DoesNotPrepareOrExecuteOriginal(bool psychic)
    {
        var result = Replay(psychic, initialGroup: "another ritual", enqueue: true);
        Assert.That(result, Is.EqualTo(new[] { "False", "another ritual", "0" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AssignmentInsideSyncedDelegate_PreparesGroupsAtOriginalMethodEntry(bool psychic)
    {
        var result = Replay(psychic, initialGroup: "another ritual", nested: true);
        Assert.That(result, Is.EqualTo(new[] { "True", "current ritual", "1" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedReplacement_DoesNotInvalidateAssignmentGroupEnumeration(bool psychic)
    {
        Assert.That(Invoke("NestedReplacement", psychic), Is.EqualTo(true));
    }

    [Test]
    public void DifferentGenericWidget_DoesNotRunTheRegisteredPreparation()
    {
        Assert.That(Invoke("DifferentWidget"), Is.EqualTo(true));
    }

    [Test]
    public void PsychicAssignment_DoesNotModifyIdeologyCache()
    {
        Assert.That(Invoke("SeparateCaches"), Is.EqualTo(true));
    }

    private string[] Replay(bool psychic, string initialGroup, bool enqueue = false, bool nested = false)
        => (string[])Invoke("Replay", psychic, initialGroup, enqueue, nested)!;

    private object? Invoke(string method, params object[] args)
        => scenarios.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);

    private static string ReadResource(string name)
    {
        using var resource = typeof(RitualRoleCacheTest).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd();
    }
}
