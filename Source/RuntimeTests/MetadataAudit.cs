using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 1)
    throw new ArgumentException("Usage: MetadataAudit <RimWorld-1.7-mod-root>");

var root = Path.GetFullPath(args[0]);
var binaries = new Dictionary<string, string>
{
    ["client"] = Path.Combine(root, "AssembliesCustom", "Multiplayer.dll"),
    ["common"] = Path.Combine(root, "AssembliesCustom", "MultiplayerCommon.dll"),
    ["loader"] = Path.Combine(root, "Assemblies", "MultiplayerLoader.dll"),
};

var assemblies = new Dictionary<string, AssemblyMetadata>();
foreach (var (role, path) in binaries)
{
    if (!File.Exists(path))
        throw new FileNotFoundException($"Missing 1.7 {role} assembly", path);

    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata)
        throw new BadImageFormatException($"No CLR metadata in {path}");
    var reader = pe.GetMetadataReader();
    var definition = reader.GetAssemblyDefinition();
    var name = reader.GetString(definition.Name);
    var framework = ReadTargetFramework(reader);
    if (framework != ".NETCoreApp,Version=v10.0")
        throw new InvalidDataException($"{role} {name} targets {framework ?? "unknown"}, expected .NETCoreApp,Version=v10.0");

    var references = reader.AssemblyReferences
        .Select(handle => reader.GetAssemblyReference(handle))
        .Select(reference => new AssemblyReferenceInfo(reader.GetString(reference.Name), reference.Version))
        .ToArray();
    var harmonyReference = references.FirstOrDefault(reference => reference.Name == "0Harmony");
    if (harmonyReference?.Version.Major != 3)
        throw new InvalidDataException($"{role} must reference Harmony major 3; got {harmonyReference?.Version.ToString() ?? "no 0Harmony AssemblyRef"}.");
    var forbidden = references.FirstOrDefault(reference =>
        reference.Name.Contains("Prepatcher", StringComparison.OrdinalIgnoreCase)
        || reference.Name.Contains("Mono.Cecil", StringComparison.OrdinalIgnoreCase));
    if (forbidden is not null)
        throw new InvalidDataException($"{role} has forbidden AssemblyRef {forbidden.Name}, Version={forbidden.Version}");

    foreach (var handle in reader.TypeReferences)
    {
        var type = reader.GetTypeReference(handle);
        var ns = reader.GetString(type.Namespace);
        var typeName = reader.GetString(type.Name);
        if (ns.StartsWith("Prepatcher", StringComparison.Ordinal)
            || ns.StartsWith("Mono.Cecil", StringComparison.Ordinal)
            || typeName.Contains("ISyncMethodForPrepatcher", StringComparison.Ordinal))
            throw new InvalidDataException($"{role} contains forbidden TypeRef {ns}.{typeName}");
    }

    assemblies.Add(role, new AssemblyMetadata(name, framework!, references));
    Console.WriteLine($"PASS {role}: {name}; target framework {framework}");
    foreach (var reference in references.Where(reference =>
        reference.Name is "System.Runtime" or "Assembly-CSharp" or "0Harmony"))
        Console.WriteLine($"  AssemblyRef {reference.Name}, Version={reference.Version}");
}

var clientReferences = assemblies["client"].References;
var runtimeReference = clientReferences.FirstOrDefault(reference => reference.Name == "System.Runtime");
if (runtimeReference?.Version.Major != 10)
    throw new InvalidDataException("Client does not reference System.Runtime 10.");
var gameReference = clientReferences.FirstOrDefault(reference => reference.Name == "Assembly-CSharp");
if (gameReference is null || gameReference.Version.Major != 1 || gameReference.Version.Minor != 7)
    throw new InvalidDataException("Client does not reference the RimWorld 1.7 Assembly-CSharp API.");
foreach (var directory in new[] { Path.Combine(root, "Assemblies"), Path.Combine(root, "AssembliesCustom") })
foreach (var path in Directory.EnumerateFiles(directory, "*.dll"))
{
    var name = Path.GetFileName(path);
    var lowered = name.ToLowerInvariant();
    if (name == "0Harmony.dll" || lowered.Contains("prepatcher") || lowered.Contains("mono.cecil"))
        throw new InvalidDataException($"Unexpected bundled Harmony/Prepatcher assembly: {path}");
    if (name == "UnityStubs.dll" || name == "Assembly-CSharp.dll" || name == "Verse.dll"
        || name.StartsWith("UnityEngine", StringComparison.Ordinal))
        throw new InvalidDataException($"Game reference DLL must not be packaged: {path}");
}

Console.WriteLine("PASS 1.7 AssemblyRef and TypeRef audit: .NET 10, RimWorld 1.7 API, external Harmony, no Prepatcher/Cecil/game DLLs.");
Console.WriteLine("Metadata-only audit; no game assembly was loaded or executed.");

static string? ReadTargetFramework(MetadataReader reader)
{
    foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attribute = reader.GetCustomAttribute(handle);
        var constructorType = GetAttributeType(reader, attribute.Constructor);
        if (constructorType != ("System.Runtime.Versioning", "TargetFrameworkAttribute"))
            continue;

        var value = reader.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() != 1)
            throw new BadImageFormatException("Malformed TargetFrameworkAttribute custom-attribute blob.");
        return value.ReadSerializedString();
    }
    return null;
}

static (string Namespace, string Name) GetAttributeType(MetadataReader reader, EntityHandle constructor)
{
    EntityHandle parent = constructor.Kind switch
    {
        HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
        HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
        _ => default,
    };

    return parent.Kind switch
    {
        HandleKind.TypeReference => ReadTypeReference(reader, (TypeReferenceHandle)parent),
        HandleKind.TypeDefinition => ReadTypeDefinition(reader, (TypeDefinitionHandle)parent),
        _ => (string.Empty, string.Empty),
    };
}

static (string Namespace, string Name) ReadTypeReference(MetadataReader reader, TypeReferenceHandle handle)
{
    var type = reader.GetTypeReference(handle);
    return (reader.GetString(type.Namespace), reader.GetString(type.Name));
}

static (string Namespace, string Name) ReadTypeDefinition(MetadataReader reader, TypeDefinitionHandle handle)
{
    var type = reader.GetTypeDefinition(handle);
    return (reader.GetString(type.Namespace), reader.GetString(type.Name));
}

sealed record AssemblyMetadata(string Name, string TargetFramework, AssemblyReferenceInfo[] References);
sealed record AssemblyReferenceInfo(string Name, Version Version);
