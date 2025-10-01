#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using UnityEngine;

namespace Multiplayer.Client.Util;

/// Only Windows support for now. Calling methods of this class from another OS is a no-op.
public static class FileAssoc
{
    public const string ReplayProgId = "RimworldMultiplayer.Replay.1";
    public const string ReplayExtension = ".rwmts"; // rimworld multiplayer save

    // Create the associations only for the current user.
    // RimWorld is not installed system-wide, so this works fine and avoids requiring admin rights.
    private static RegistryKey GetAssociationsRootKey() =>
        CreateSubKeyOrThrow(Registry.CurrentUser, @"Software\Classes");

    public static void Register()
    {
        if (!IsSupported()) return;
        var appPath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.GetCommandLineArgs()[0];
        appPath = Path.GetFullPath(appPath);
        RegisterApp(appPath);
        RegisterExtension(ReplayExtension);
    }

    public static void Remove()
    {
        if (!IsSupported()) return;
        var rootKey = GetAssociationsRootKey();
        rootKey.DeleteSubKeyTree(ReplayProgId, throwOnMissingSubKey: false);
        rootKey.DeleteSubKey(ReplayExtension, throwOnMissingSubKey: false);
    }

    public static bool IsRegistered()
    {
        if (!IsSupported()) return false;
        var rootKey = GetAssociationsRootKey();
        return rootKey.OpenSubKey(ReplayProgId) != null || rootKey.OpenSubKey(ReplayExtension) != null;
    }

    public static bool IsSupported() => Application.platform == RuntimePlatform.WindowsPlayer;

    private static void RegisterApp(string appPath)
    {
        // https://learn.microsoft.com/en-us/windows/win32/shell/fa-progids
        var rootKey = GetAssociationsRootKey();
        using var progIdKey = CreateSubKeyOrThrow(rootKey, ReplayProgId);
        progIdKey.SetValue("", "RimWorld Multiplayer Save");
        progIdKey.SetValue("FriendlyTypeName", "RimWorld Multiplayer Save");

        using (var defaultIcon = CreateSubKeyOrThrow(progIdKey, "DefaultIcon"))
        {
            defaultIcon.SetValue("", $"\"{appPath}\",0");
        }

        using (var command = CreateSubKeyOrThrow(progIdKey, @"shell\open\command"))
        {
            command.SetValue("", $"\"{appPath}\" -{MultiplayerStatic.MpHostReplayCmdLineArgName}=\"%1\"",
                RegistryValueKind.ExpandString);
        }
    }

    private static void RegisterExtension(string ext)
    {
        if (!ext.StartsWith(".")) ext = "." + ext;
        using var extKey = CreateSubKeyOrThrow(GetAssociationsRootKey(), ext);
        extKey.SetValue("", ReplayProgId);
    }

    private static RegistryKey CreateSubKeyOrThrow(RegistryKey parent, string subkey) =>
        parent.CreateSubKey(subkey) ??
        throw new InvalidOperationException($"Cannot access registry key {parent.Name}\\{subkey}");
}
