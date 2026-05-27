namespace Multiplayer.Common.ChatCommands;

public delegate bool ChatCommandParser<TArgs>(ChatCommandContext context, out TArgs args, out string? error);
