namespace Multiplayer.Common.ChatCommands;

public sealed record ChatCommandRegistration(IChatCommand Command, ChatCommandInfo Info);
