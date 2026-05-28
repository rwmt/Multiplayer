using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Multiplayer.Common;

namespace Multiplayer.SourceGen;

[Generator]
public sealed class ChatCommandRegistryGenerator : IIncrementalGenerator
{
    private static readonly string ChatCommandAttributeName = AttributeMetadataName(nameof(ChatCommandAttribute));
    private const string ChatCommandGenericName = "Multiplayer.Common.ChatCommands.ChatCommand<TArgs>";
    private const string ChatCommandContextName = "Multiplayer.Common.ChatCommands.ChatCommandContext";
    private const string ServerPlayerName = "Multiplayer.Common.ServerPlayer";
    private static readonly string ChatArgumentAttributeName = AttributeMetadataName(nameof(ChatArgumentAttribute));
    private static readonly string ChatRestAttributeName = AttributeMetadataName(nameof(ChatRestAttribute));

    private static readonly DiagnosticDescriptor DuplicateNameDescriptor = new(
        "MPCHAT001",
        "Duplicate chat command name",
        "Chat command name or alias '{0}' is already registered by '{1}'",
        "ChatCommands",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor InvalidCommandDescriptor = new(
        "MPCHAT002",
        "Invalid chat command type",
        "Chat command '{0}' must implement Multiplayer.Common.ChatCommands.IChatCommand",
        "ChatCommands",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor UnsupportedArgumentDescriptor = new(
        "MPCHAT003",
        "Unsupported chat command argument",
        "Chat command argument '{0}' has unsupported type '{1}'",
        "ChatCommands",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor InvalidRestArgumentDescriptor = new(
        "MPCHAT004",
        "Invalid chat rest argument",
        "Chat rest argument '{0}' {1}",
        "ChatCommands",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor InvalidArgumentParserDescriptor = new(
        "MPCHAT005",
        "Invalid chat command argument parser",
        "Chat command arguments '{0}' {1}",
        "ChatCommands",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor InvalidCommandConstructorDescriptor = new(
        "MPCHAT006",
        "Invalid chat command constructor",
        "Chat command '{0}' must have an accessible parameterless constructor",
        "ChatCommands",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor InvalidNameDescriptor = new(
        "MPCHAT007",
        "Invalid chat command name",
        "Chat command name or alias cannot be blank",
        "ChatCommands",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ChatCommandAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => CreateCommand(ctx)
            )
            .Where(static command => command is not null)
            .Select(static (command, _) => command!)
            .Collect();

        context.RegisterSourceOutput(commands, Generate);
    }

    private static ChatCommandModel? CreateCommand(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes.First(attribute =>
            attribute.AttributeClass?.ToDisplayString() == ChatCommandAttributeName
        );

        var name = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value?.ToString() ?? type.Name
            : type.Name;

        var aliases = attribute.ConstructorArguments.Length > 1
            ? attribute.ConstructorArguments[1].Values
                .Select(value => value.Value?.ToString() ?? string.Empty)
                .ToArray()
            : [];

        var namedArguments = attribute.NamedArguments.ToDictionary(argument => argument.Key, argument => argument.Value);
        var description = GetString(namedArguments, "Description");
        var usage = GetString(namedArguments, "Usage");
        var requiresHost = namedArguments.TryGetValue("RequiresHost", out var hostValue) && (bool)(hostValue.Value ?? false);

        return new ChatCommandModel(type, name, aliases, description, usage, requiresHost);
    }

    private static string GetString(IReadOnlyDictionary<string, TypedConstant> arguments, string key) =>
        arguments.TryGetValue(key, out var value) ? value.Value?.ToString() ?? string.Empty : string.Empty;

    private static void Generate(SourceProductionContext context, ImmutableArray<ChatCommandModel> commands)
    {
        if (commands.Length == 0)
            return;

        var validCommands = new List<ChatCommandModel>();
        var seenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands.OrderBy(command => command.Type.ToDisplayString(), StringComparer.Ordinal))
        {
            if (!ImplementsInterface(command.Type, "Multiplayer.Common.ChatCommands.IChatCommand"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidCommandDescriptor,
                    command.Type.Locations.FirstOrDefault(),
                    command.Type.ToDisplayString()
                ));
                continue;
            }

            if (!HasAccessibleParameterlessConstructor(command.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidCommandConstructorDescriptor,
                    command.Type.Locations.FirstOrDefault(),
                    command.Type.ToDisplayString()
                ));
                continue;
            }

            var commandType = command.Type.ToDisplayString();
            var hasInvalidName = false;
            var hasDuplicateName = false;
            foreach (var name in command.AllNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidNameDescriptor,
                        command.Type.Locations.FirstOrDefault()
                    ));
                    hasInvalidName = true;
                    continue;
                }

                if (!seenNames.TryAdd(name, commandType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateNameDescriptor,
                        command.Type.Locations.FirstOrDefault(),
                        name,
                        seenNames[name]
                    ));
                    hasDuplicateName = true;
                }
            }

            if (!hasInvalidName && !hasDuplicateName)
                validCommands.Add(command);
        }

        if (validCommands.Count == 0)
            return;

        var source = CreateRegistrySource(context, validCommands);
        context.AddSource("ChatCommandRegistry.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string CreateRegistrySource(SourceProductionContext context, IReadOnlyList<ChatCommandModel> commands)
    {
        var registrations = new StringBuilder();
        var parsers = new StringBuilder();

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            var variable = $"command{i}";
            var metadata = $"metadata{i}";
            var commandType = command.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var names = command.AllNames.ToArray();
            var namesExpression = "new string[] { " + string.Join(", ", names.Select(StringLiteral)) + " }";

            registrations.AppendLine($"var {variable} = new {commandType}();");

            var argsType = GetChatCommandArgumentType(command.Type);
            if (argsType != null)
            {
                var parserName = $"TryParseCommand{i}Args";
                registrations.AppendLine($"{variable}.SetParser({parserName});");
                parsers.AppendLine(CreateParser(context, command, argsType, parserName));
            }

            registrations.AppendLine(
                $"var {metadata} = new global::Multiplayer.Common.ChatCommands.ChatCommandInfo({variable}, {namesExpression}, {StringLiteral(command.Description)}, {StringLiteral(command.Usage)}, {command.RequiresHost.ToString().ToLowerInvariant()});"
            );

            foreach (var name in names)
                registrations.AppendLine($"manager.AddCommand({StringLiteral(name)}, {variable}, {metadata});");

            registrations.AppendLine();
        }

        return $$"""
                 // <auto-generated />
                 #nullable enable

                 namespace Multiplayer.Common.ChatCommands;

                 internal static partial class ChatCommandRegistry
                 {
                     public static partial void Register(global::Multiplayer.Common.ChatCommands.ChatCommandManager manager, global::Multiplayer.Common.MultiplayerServer server)
                     {
                 {{Indent(registrations.ToString().TrimEnd(), 8)}}
                     }

                 {{Indent(parsers.ToString().TrimEnd(), 4)}}
                 }
                 """;
    }

    private static string CreateParser(SourceProductionContext context, ChatCommandModel command, ITypeSymbol argsType, string parserName)
    {
        var customParser = FindCustomParser(command.Type, argsType);
        if (customParser != null)
            return CreateCustomParser(command, argsType, parserName);

        var constructor = FindConstructor(context, argsType, out var hasValidConstructorSelection);

        if (!hasValidConstructorSelection)
            return CreateInvalidParser(argsType, parserName);

        if (constructor == null)
        {
            if (!HasAccessibleParameterlessConstructor(argsType))
            {
                ReportInvalidArgumentParser(context, argsType, "must have an accessible constructor");
                return CreateInvalidParser(argsType, parserName);
            }

            return CreateParameterlessParser(argsType, parserName);
        }

        if (!ValidateConstructorParameters(context, constructor))
            return CreateInvalidParser(argsType, parserName);

        var body = new StringBuilder();
        var values = new List<string>();
        var index = 0;

        foreach (var parameter in constructor.Parameters)
        {
            var variable = $"arg{index}";
            var displayName = GetArgumentName(parameter);
            var isRest = IsRestArgument(constructor, parameter);
            var isOptional = IsOptional(parameter);

            if (!isOptional)
                AppendRequiredArgumentCheck(body, command, displayName, index);

            var rawExpression = isRest
                ? $"global::Multiplayer.Common.ChatCommands.ChatCommandArgumentReader.JoinRest(context.RawArgs, {index})"
                : $"context.RawArgs[{index}]";
            var defaultExpression = DefaultExpression(parameter);
            var parseExpression = ParseExpression(context, parameter, rawExpression, variable, out var parseStatements);
            var valueExpression = variable;
            if (isOptional && parseStatements.Length > 0)
            {
                body.AppendLine($$"""
                        {{parameter.Type.ToDisplayString(FullyQualifiedNullableFormat)}} {{variable}};
                        if (context.RawArgs.Count > {{index}})
                        {
                    {{Indent(parseStatements.TrimEnd(), 8)}}
                            {{variable}} = {{parseExpression}};
                        }
                        else
                        {
                            {{variable}} = {{defaultExpression}};
                        }
                    """);
            }
            else if (isOptional)
            {
                body.AppendLine($"    var {variable} = context.RawArgs.Count > {index} ? {parseExpression} : {defaultExpression};");
            }
            else
            {
                if (parseStatements.Length > 0)
                {
                    body.Append(Indent(parseStatements.TrimEnd(), 4));
                    body.AppendLine();
                }

                valueExpression = parseExpression;
            }

            values.Add(valueExpression);
            index++;
        }

        var argsTypeName = argsType.ToDisplayString(FullyQualifiedNullableFormat);
        return $$"""
                 private static bool {{parserName}}(global::Multiplayer.Common.ChatCommands.ChatCommandContext context, out {{argsTypeName}} args, out string? error)
                 {
                 {{body.ToString().TrimEnd()}}
                     args = new {{argsTypeName}}({{string.Join(", ", values)}});
                     error = null;
                     return true;
                 }
                 """;
    }

    private static string CreateCustomParser(ChatCommandModel command, ITypeSymbol argsType, string parserName)
    {
        return $$"""
                 private static bool {{parserName}}(global::Multiplayer.Common.ChatCommands.ChatCommandContext context, out {{argsType.ToDisplayString(FullyQualifiedNullableFormat)}} args, out string? error)
                 {
                     if ({{command.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}.TryParse(context, out args))
                     {
                         error = null;
                         return true;
                     }

                     error = "Invalid command arguments.";
                     return false;
                 }
                 """;
    }

    private static string CreateParameterlessParser(ITypeSymbol argsType, string parserName)
    {
        return $$"""
                 private static bool {{parserName}}(global::Multiplayer.Common.ChatCommands.ChatCommandContext context, out {{argsType.ToDisplayString(FullyQualifiedNullableFormat)}} args, out string? error)
                 {
                     args = new {{argsType.ToDisplayString(FullyQualifiedNullableFormat)}}();
                     error = null;
                     return true;
                 }
                 """;
    }

    private static string CreateInvalidParser(ITypeSymbol argsType, string parserName)
    {
        return $$"""
                 private static bool {{parserName}}(global::Multiplayer.Common.ChatCommands.ChatCommandContext context, out {{argsType.ToDisplayString(FullyQualifiedNullableFormat)}} args, out string? error)
                 {
                     args = default;
                     error = "Invalid command arguments.";
                     return false;
                 }
                 """;
    }

    private static IMethodSymbol? FindConstructor(SourceProductionContext context, ITypeSymbol argsType, out bool isValid)
    {
        isValid = true;
        var constructors = argsType
            .GetMembers(".ctor")
            .OfType<IMethodSymbol>()
            .Where(ctor => !ctor.IsStatic && ctor.Parameters.Length > 0 && IsAccessible(ctor))
            .ToArray();

        if (constructors.Length == 0)
            return null;

        var largestParameterCount = constructors.Max(ctor => ctor.Parameters.Length);
        var candidates = constructors
            .Where(ctor => ctor.Parameters.Length == largestParameterCount)
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0];

        ReportInvalidArgumentParser(context, argsType, "has ambiguous constructors");
        isValid = false;
        return null;
    }

    private static bool HasAccessibleParameterlessConstructor(ITypeSymbol argsType) =>
        argsType.IsValueType
        || argsType.GetMembers(".ctor")
            .OfType<IMethodSymbol>()
            .Any(ctor => !ctor.IsStatic && ctor.Parameters.Length == 0 && IsAccessible(ctor));

    private static void ReportInvalidArgumentParser(SourceProductionContext context, ITypeSymbol argsType, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidArgumentParserDescriptor,
            argsType.Locations.FirstOrDefault(),
            argsType.ToDisplayString(),
            message
        ));
    }

    private static bool ValidateConstructorParameters(SourceProductionContext context, IMethodSymbol constructor)
    {
        var valid = true;
        var hasRestArgument = false;

        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            var parameter = constructor.Parameters[i];
            if (!IsRestArgument(constructor, parameter))
                continue;

            if (hasRestArgument)
            {
                ReportInvalidRestArgument(context, parameter, "must be the only rest argument");
                valid = false;
            }

            hasRestArgument = true;

            if (!IsValidRestType(parameter.Type))
            {
                ReportInvalidRestArgument(context, parameter, "must be a string or ServerPlayer");
                valid = false;
            }

            if (i != constructor.Parameters.Length - 1)
            {
                ReportInvalidRestArgument(context, parameter, "must be the final argument");
                valid = false;
            }
        }

        return valid;
    }

    private static bool IsRestArgument(IMethodSymbol constructor, IParameterSymbol parameter)
    {
        return HasAttribute(parameter, ChatRestAttributeName)
               || (constructor.Parameters.Length == 1 && IsValidRestType(parameter.Type));
    }

    private static bool IsValidRestType(ITypeSymbol type)
    {
        var nonNullable = NonNullableType(type);
        return nonNullable.SpecialType == SpecialType.System_String
               || nonNullable.ToDisplayString() == ServerPlayerName;
    }

    private static void ReportInvalidRestArgument(SourceProductionContext context, IParameterSymbol parameter, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidRestArgumentDescriptor,
            parameter.Locations.FirstOrDefault(),
            parameter.Name,
            message
        ));
    }

    private static void AppendRequiredArgumentCheck(StringBuilder body, ChatCommandModel command, string displayName, int index)
    {
        body.AppendLine($$"""
                if (!global::Multiplayer.Common.ChatCommands.ChatCommandArgumentReader.HasArgument(context, {{index}}, {{StringLiteral(MissingArgumentMessage(command, displayName))}}, out error))
                {
                    args = default;
                    return false;
                }
            """);
    }

    private static string ParseExpression(SourceProductionContext context, IParameterSymbol parameter, string rawExpression, string variable, out string statements)
    {
        statements = string.Empty;
        var type = parameter.Type;
        var nonNullable = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : type;

        if (nonNullable.SpecialType == SpecialType.System_String)
            return rawExpression;

        if (nonNullable.SpecialType == SpecialType.System_Int32)
        {
            statements = CreateTryParseStatements("TryParseInt", rawExpression, variable, parameter);
            return $"parsed{variable}";
        }

        if (nonNullable.SpecialType == SpecialType.System_Boolean)
        {
            statements = CreateTryParseStatements("TryParseBool", rawExpression, variable, parameter);
            return $"parsed{variable}";
        }

        if (nonNullable.SpecialType == SpecialType.System_Single)
        {
            statements = CreateTryParseStatements("TryParseFloat", rawExpression, variable, parameter);
            return $"parsed{variable}";
        }

        if (nonNullable.TypeKind == TypeKind.Enum)
        {
            var typeName = nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            statements = CreateTryParseStatements($"TryParseEnum<{typeName}>", rawExpression, variable, parameter);
            return $"parsed{variable}";
        }

        if (nonNullable.ToDisplayString() == ServerPlayerName)
        {
            statements = CreateTryParseStatements("TryParsePlayer", $"context, {rawExpression}", variable, parameter);
            return $"parsed{variable}";
        }

        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedArgumentDescriptor,
            parameter.Locations.FirstOrDefault(),
            parameter.Name,
            type.ToDisplayString()
        ));
        return rawExpression;
    }

    private static string CreateTryParseStatements(string method, string input, string valueName, IParameterSymbol symbol)
    {
        return $$"""
                 if (!global::Multiplayer.Common.ChatCommands.ChatCommandArgumentReader.{{method}}({{input}}, {{StringLiteral(GetArgumentName(symbol))}}, out var parsed{{valueName}}, out error))
                 {
                     args = default;
                     return false;
                 }
                 """;
    }

    private static string MissingArgumentMessage(ChatCommandModel command, string name) =>
        string.IsNullOrWhiteSpace(command.Usage)
            ? $"Missing argument: {name}."
            : $"Usage: {command.Usage}";

    private static string DefaultExpression(IParameterSymbol parameter)
    {
        if (parameter.HasExplicitDefaultValue)
        {
            var nonNullable = NonNullableType(parameter.Type);
            if (parameter.ExplicitDefaultValue != null && nonNullable.TypeKind == TypeKind.Enum)
                return $"({nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Literal(parameter.ExplicitDefaultValue)}";

            return Literal(parameter.ExplicitDefaultValue);
        }

        return parameter.Type.NullableAnnotation == NullableAnnotation.Annotated
            || parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            ? "null"
            : "default";
    }

    private static ITypeSymbol NonNullableType(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : type;

    private static string Literal(object? value)
    {
        return value switch
        {
            null => "null",
            string s => StringLiteral(s),
            char c => "'" + c.ToString().Replace("\\", "\\\\").Replace("'", "\\'") + "'",
            bool b => b ? "true" : "false",
            float f => f.ToString(CultureInfo.InvariantCulture) + "f",
            double d => d.ToString(CultureInfo.InvariantCulture) + "d",
            decimal d => d.ToString(CultureInfo.InvariantCulture) + "m",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default"
        };
    }

    private static bool IsOptional(IParameterSymbol parameter) =>
        parameter.HasExplicitDefaultValue
        || parameter.NullableAnnotation == NullableAnnotation.Annotated
        || parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static string GetArgumentName(IParameterSymbol parameter)
    {
        var attribute = parameter.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == ChatArgumentAttributeName
        );

        return attribute?.ConstructorArguments.FirstOrDefault().Value?.ToString()
               ?? parameter.Name;
    }

    private static ITypeSymbol? GetChatCommandArgumentType(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == ChatCommandGenericName)
                return current.TypeArguments[0];
        }

        return null;
    }

    private static IMethodSymbol? FindCustomParser(INamedTypeSymbol commandType, ITypeSymbol argsType)
    {
        return commandType.GetMembers("TryParse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic
                && IsAccessible(method)
                && method.ReturnType.SpecialType == SpecialType.System_Boolean
                && method.Parameters.Length == 2
                && method.Parameters[0].Type.ToDisplayString() == ChatCommandContextName
                && method.Parameters[1].RefKind == RefKind.Out
                && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, argsType)
            );
    }

    private static bool IsAccessible(ISymbol symbol) =>
        symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceName) =>
        type.AllInterfaces.Any(@interface => @interface.ToDisplayString() == interfaceName);

    private static bool HasAttribute(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName);

    private static string AttributeMetadataName(string attributeTypeName) =>
        $"Multiplayer.Common.{attributeTypeName}";

    private static string StringLiteral(string value) =>
        "@\"" + value.Replace("\"", "\"\"") + "\"";

    private static string Indent(string value, int spaces)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var prefix = new string(' ', spaces);
        return string.Join(
            "\n",
            value.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => line.Length == 0 ? string.Empty : prefix + line)
        );
    }

    private sealed class ChatCommandModel
    {
        public ChatCommandModel(
            INamedTypeSymbol type,
            string name,
            string[] aliases,
            string description,
            string usage,
            bool requiresHost
        )
        {
            Type = type;
            Name = name;
            Aliases = aliases;
            Description = description;
            Usage = usage;
            RequiresHost = requiresHost;
        }

        public INamedTypeSymbol Type { get; }
        public string Name { get; }
        public string[] Aliases { get; }
        public string Description { get; }
        public string Usage { get; }
        public bool RequiresHost { get; }
        public IEnumerable<string> AllNames => new[] { Name }.Concat(Aliases);
    }
}

internal static class DictionaryExtensions
{
    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        if (dictionary.ContainsKey(key))
            return false;

        dictionary.Add(key, value);
        return true;
    }
}
