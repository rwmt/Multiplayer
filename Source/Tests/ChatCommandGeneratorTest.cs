using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Multiplayer.Common;
using Multiplayer.Common.ChatCommands;
using Multiplayer.SourceGen;

namespace Tests;

public class ChatCommandGeneratorTest
{
    [Test]
    public void ChatCommand_RegistersCommand()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            [ChatCommand("ping", Description = "Ping the server.", Usage = "ping")]
            public sealed class PingCommand : ChatCommand
            {
                public override void Execute(ChatCommandContext context)
                {
                    context.Source.SendMsg("pong");
                }
            }
            """
        );

        var registry = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("ChatCommandRegistry.g.cs"));
        var source = registry.GetText().ToString();

        Assert.That(source, Does.Contain("internal static partial class ChatCommandRegistry"));
        Assert.That(source, Does.Contain("""new global::Multiplayer.Common.PingCommand()"""));
        Assert.That(source, Does.Contain("""manager.AddCommand(@"ping","""));
    }

    [Test]
    public void ChatCommand_GeneratesTypedArgumentParser()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct EchoArgs([ChatArgument("text")] string Text);

            [ChatCommand("echo", Usage = "echo <text>")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                    context.Source.SendMsg(args.Text);
                }
            }
            """
        );

        var source = GeneratedRegistrySource(result);

        Assert.That(source, Does.Contain("command0.SetParser(TryParseCommand0Args);"));
        Assert.That(
            source,
            Does.Contain("args = new global::Multiplayer.Common.EchoArgs(global::Multiplayer.Common.ChatCommands.ChatCommandArgumentReader.JoinRest(context.RawArgs, 0));")
        );
    }

    [Test]
    public void ChatCommand_GeneratesPlayerArgumentParser()
    {
        var result = RunGeneratorWithCompilation(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct InspectArgs([ChatArgument("username")] ServerPlayer Player);

            [ChatCommand("inspect", Usage = "inspect <username>")]
            public sealed class InspectCommand : ChatCommand<InspectArgs>
            {
                protected override void Execute(ChatCommandContext context, InspectArgs args)
                {
                }
            }
            """
        );

        var source = GeneratedRegistrySource(result.Result);

        Assert.That(source, Does.Contain("ChatCommandArgumentReader.TryParsePlayer(context, global::Multiplayer.Common.ChatCommands.ChatCommandArgumentReader.JoinRest(context.RawArgs, 0), @\"username\""));
        Assert.That(
            result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty
        );
    }

    [Test]
    public void ChatCommand_SameClassNameInDifferentNamespacesGeneratesCompilableRegistry()
    {
        var result = RunGeneratorWithCompilation(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace First
            {

            public readonly record struct EchoArgs(string Text);

            [ChatCommand("first")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            }

            namespace Second
            {

            public readonly record struct EchoArgs(string Text);

            [ChatCommand("second")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            }
            """
        );

        Assert.That(
            result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty
        );
    }

    [Test]
    public void ChatCommand_MultipleArgumentsKeepStringArgumentPositional()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct EchoArgs(string Text, int Count);

            [ChatCommand("echo", Usage = "echo <text> <count>")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        var source = GeneratedRegistrySource(result);

        Assert.That(source, Does.Contain("ChatCommandArgumentReader.TryParseInt(context.RawArgs[1], @\"Count\""));
        Assert.That(source, Does.Contain("args = new global::Multiplayer.Common.EchoArgs(context.RawArgs[0], parsedarg1);"));
    }

    [Test]
    public void ChatCommand_UsesCustomParserWhenPresent()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct CustomArgs(string Value);

            [ChatCommand("custom")]
            public sealed class CustomCommand : ChatCommand<CustomArgs>
            {
                public static bool TryParse(ChatCommandContext context, out CustomArgs args)
                {
                    args = new CustomArgs("custom");
                    return true;
                }

                protected override void Execute(ChatCommandContext context, CustomArgs args)
                {
                    context.Source.SendMsg(args.Value);
                }
            }
            """
        );

        var source = GeneratedRegistrySource(result);

        Assert.That(source, Does.Contain("global::Multiplayer.Common.CustomCommand.TryParse(context, out args)"));
    }

    [Test]
    public void ChatCommand_GeneratesParsersForOptionalPrimitiveEnumAndRestArguments()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public enum EchoMode
            {
                Normal,
                Loud
            }

            public readonly record struct EchoArgs(
                [ChatArgument("count")] int Count = 1,
                [ChatArgument("enabled")] bool Enabled = true,
                [ChatArgument("mode")] EchoMode Mode = EchoMode.Normal,
                [ChatRest] string? Text = null
            );

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        var source = GeneratedRegistrySource(result);

        Assert.That(source, Does.Contain("ChatCommandArgumentReader.TryParseInt(context.RawArgs[0], @\"count\""));
        Assert.That(source, Does.Contain("ChatCommandArgumentReader.TryParseBool(context.RawArgs[1], @\"enabled\""));
        Assert.That(source, Does.Contain("ChatCommandArgumentReader.TryParseEnum<global::Multiplayer.Common.EchoMode>(context.RawArgs[2], @\"mode\""));
        Assert.That(source, Does.Contain("ChatCommandArgumentReader.JoinRest(context.RawArgs, 3)"));
        Assert.That(source, Does.Contain("if (context.RawArgs.Count > 0)"));
        Assert.That(source, Does.Contain("arg0 = 1;"));
    }

    [Test]
    public void ChatCommand_OptionalEnumDefaultGeneratesCompilableRegistry()
    {
        var result = RunGeneratorWithCompilation(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public enum EchoMode
            {
                Normal,
                Loud
            }

            public readonly record struct EchoArgs([ChatArgument("mode")] EchoMode Mode = EchoMode.Loud);

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        Assert.That(
            result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty
        );
    }

    [Test]
    public void ChatCommand_RestArgumentMustBeString()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct EchoArgs([ChatRest] int Text);

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT004"));
    }

    [Test]
    public void ChatCommand_RestArgumentMustBeFinalArgument()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct EchoArgs([ChatRest] string Text, int Count);

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT004"));
    }

    [Test]
    public void ChatCommand_RestArgumentMustBeUnique()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct EchoArgs([ChatRest] string First, [ChatRest] string Second);

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT004"));
    }

    [Test]
    public void ChatCommand_AmbiguousArgumentConstructorsReportDiagnostic()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public sealed class EchoArgs
            {
                public EchoArgs(int count)
                {
                }

                public EchoArgs(string text)
                {
                }
            }

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT005"));
    }

    [Test]
    public void ChatCommand_PrivateCustomParserDoesNotGenerateInaccessibleCall()
    {
        var result = RunGeneratorWithCompilation(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public readonly record struct EchoArgs(string Text);

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                private static bool TryParse(ChatCommandContext context, out EchoArgs args)
                {
                    args = new EchoArgs("private");
                    return true;
                }

                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        Assert.That(
            result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty
        );
    }

    [Test]
    public void ChatCommand_PrivateArgumentConstructorReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            public sealed class EchoArgs
            {
                private EchoArgs(string text)
                {
                }
            }

            [ChatCommand("echo")]
            public sealed class EchoCommand : ChatCommand<EchoArgs>
            {
                protected override void Execute(ChatCommandContext context, EchoArgs args)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT005"));
    }

    [Test]
    public void ChatCommand_PrivateCommandConstructorReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            [ChatCommand("secret")]
            public sealed class SecretCommand : ChatCommand
            {
                private SecretCommand()
                {
                }

                public override void Execute(ChatCommandContext context)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT006"));
    }

    [Test]
    public void ChatCommand_AbstractCommandReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            [ChatCommand("abstract")]
            public abstract class AbstractCommand : ChatCommand
            {
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT006"));
    }

    [Test]
    public void ChatCommand_BlankPrimaryNameReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            [ChatCommand("")]
            public sealed class BlankCommand : ChatCommand
            {
                public override void Execute(ChatCommandContext context)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT007"));
    }

    [Test]
    public void ChatCommand_BlankAliasReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            [ChatCommand("named", "")]
            public sealed class BlankAliasCommand : ChatCommand
            {
                public override void Execute(ChatCommandContext context)
                {
                }
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT007"));
    }

    [Test]
    public void ChatCommand_DuplicateNameReportsDiagnostic()
    {
        var result = RunGenerator(
            """
            using Multiplayer.Common;
            using Multiplayer.Common.ChatCommands;

            namespace Multiplayer.Common;

            [ChatCommand("same")]
            public sealed class FirstCommand : ChatCommand
            {
                public override void Execute(ChatCommandContext context) {}
            }

            [ChatCommand("same")]
            public sealed class SecondCommand : ChatCommand
            {
                public override void Execute(ChatCommandContext context) {}
            }
            """
        );

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Does.Contain("MPCHAT001"));
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        return RunGeneratorWithCompilation(source).Result;
    }

    private static GeneratorRun RunGeneratorWithCompilation(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ChatCommandAttribute).Assembly.Location))
            .Cast<MetadataReference>();

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        var registryDeclaration = CSharpSyntaxTree.ParseText(
            """
            namespace Multiplayer.Common.ChatCommands;

            internal static partial class ChatCommandRegistry
            {
                public static partial void Register(ChatCommandManager manager, MultiplayerServer server);
            }
            """,
            parseOptions
        );

        var compilation = CSharpCompilation.Create(
            "ChatCommandGeneratorTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions), registryDeclaration],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ChatCommandRegistryGenerator().AsSourceGenerator()],
            parseOptions: parseOptions
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return new GeneratorRun(driver.GetRunResult(), outputCompilation);
    }

    private static string GeneratedRegistrySource(GeneratorDriverRunResult result)
    {
        return result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("ChatCommandRegistry.g.cs")).GetText().ToString();
    }

    private sealed record GeneratorRun(GeneratorDriverRunResult Result, Compilation Compilation);
}
