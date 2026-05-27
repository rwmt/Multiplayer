using System;

namespace Multiplayer.Common.ChatCommands;

public abstract class ChatCommand<TArgs> : ChatCommand
{
    private ChatCommandParser<TArgs>? parser;

    public void SetParser(ChatCommandParser<TArgs> parser)
    {
        this.parser = parser;
    }

    public sealed override void Execute(ChatCommandContext context)
    {
        if (parser == null)
            throw new InvalidOperationException($"No generated parser was registered for {GetType().FullName}.");

        if (!parser(context, out var args, out var error))
        {
            context.Source.SendRawMsg(error ?? "Invalid command arguments.");
            return;
        }

        Execute(context, args);
    }

    protected abstract void Execute(ChatCommandContext context, TArgs args);
}
