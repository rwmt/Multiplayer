using System.Collections.Generic;

namespace Multiplayer.Common.ChatCommands;

public sealed record ChatCommandContext(
    IChatSource Source,
    string CommandName,
    IReadOnlyList<string> RawArgs
);
