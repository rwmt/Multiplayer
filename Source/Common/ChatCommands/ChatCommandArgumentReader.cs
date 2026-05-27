using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Multiplayer.Common.ChatCommands;

public static class ChatCommandArgumentReader
{
    private delegate bool Parser<T>(string raw, out T value);

    public static bool HasArgument(ChatCommandContext context, int index, string missingMessage, out string? error)
    {
        if (context.RawArgs.Count > index)
        {
            error = null;
            return true;
        }

        error = missingMessage;
        return false;
    }

    public static string JoinRest(IReadOnlyList<string> args, int startIndex)
    {
        if (startIndex >= args.Count)
            return string.Empty;

        var values = new string[args.Count - startIndex];
        for (var i = 0; i < values.Length; i++)
            values[i] = args[startIndex + i];

        return string.Join(" ", values);
    }

    public static bool TryParseInt(string raw, string name, out int value, out string? error)
    {
        return TryParse(raw, name, int.TryParse, out value, out error);
    }

    public static bool TryParseBool(string raw, string name, out bool value, out string? error)
    {
        return TryParse(raw, name, bool.TryParse, out value, out error);
    }

    public static bool TryParseFloat(string raw, string name, out float value, out string? error)
    {
        return TryParse(raw, name, TryParseFloatInvariant, out value, out error);
    }

    public static bool TryParseEnum<T>(string raw, string name, out T value, out string? error) where T : struct
    {
        return TryParse(raw, name, TryParseEnumIgnoreCase, out value, out error);
    }

    public static bool TryParsePlayer(ChatCommandContext context, string raw, string name, out ServerPlayer value, out string? error)
    {
        var server = MultiplayerServer.instance!;
        var exact = server.playerManager.Players.FirstOrDefault(player =>
            string.Equals(player.Username, raw, System.StringComparison.OrdinalIgnoreCase)
        );

        if (exact != null)
        {
            value = exact;
            error = null;
            return true;
        }

        var matches = server.playerManager.Players
            .Where(player => player.Username.Contains(raw, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 1)
        {
            value = matches[0];
            error = null;
            return true;
        }

        value = null!;
        error = matches.Count == 0
            ? "Couldn't find the player."
            : $"Player name '{raw}' is ambiguous: {string.Join(", ", matches.Select(player => player.Username))}.";
        return false;
    }

    private static bool TryParse<T>(string raw, string name, Parser<T> parseMethod, out T value, out string? error)
    {
        if (parseMethod(raw, out value))
        {
            error = null;
            return true;
        }

        error = InvalidValueMessage(name);
        return false;
    }

    private static bool TryParseFloatInvariant(string raw, out float value)
    {
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseEnumIgnoreCase<T>(string raw, out T value) where T : struct
    {
        return System.Enum.TryParse(raw, true, out value);
    }

    private static string InvalidValueMessage(string name) => $"Invalid value for '{name}'.";
}
