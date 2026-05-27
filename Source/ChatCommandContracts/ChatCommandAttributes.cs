using System;

namespace Multiplayer.Common;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ChatCommandAttribute(string name, params string[] aliases) : Attribute
{
    public string Name { get; } = name;
    public string[] Aliases { get; } = aliases;
    public string Usage { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresHost { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ChatArgumentAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; set; } = string.Empty;
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ChatRestAttribute : Attribute;
