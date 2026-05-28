using System;
using System.Collections.Generic;
using System.Text;

namespace Multiplayer.Common.ChatCommands;

internal struct CommandTokenizer
{
    private const string MissingClosingQuoteError = "Invalid command arguments: missing closing quote.";

    private readonly string input;
    private int index;

    private CommandTokenizer(string input)
    {
        this.input = input;
        index = 0;
    }

    public static bool TryTokenize(string input, out string[] tokens, out string? error)
    {
        var tokenizer = new CommandTokenizer(input);
        return tokenizer.TryReadAll(out tokens, out error);
    }

    private bool TryReadAll(out string[] tokens, out string? error)
    {
        var result = new List<string>();

        while (true)
        {
            SkipWhitespace();
            if (index >= input.Length)
            {
                tokens = [.. result];
                error = null;
                return true;
            }

            if (!TryReadToken(out var token, out error))
            {
                tokens = [];
                return false;
            }

            result.Add(token);
        }
    }

    private void SkipWhitespace()
    {
        while (index < input.Length && char.IsWhiteSpace(input[index]))
            index++;
    }

    private bool TryReadToken(out string token, out string? error)
    {
        var tokenPartStart = index;
        StringBuilder? builder = null;

        while (index < input.Length && !char.IsWhiteSpace(input[index]))
        {
            if (input[index] == '"')
            {
                builder ??= new StringBuilder();
                builder.Append(input, tokenPartStart, index - tokenPartStart);

                index++;
                if (!TryReadQuotedToken(builder, out error))
                {
                    token = string.Empty;
                    return false;
                }

                tokenPartStart = index;
                continue;
            }

            ReadUnquotedToken();
        }

        if (builder == null)
        {
            token = input[tokenPartStart..index];
        }
        else
        {
            builder.Append(input, tokenPartStart, index - tokenPartStart);
            token = builder.ToString();
        }

        error = null;
        return true;
    }

    private bool TryReadQuotedToken(StringBuilder builder, out string? error)
    {
        while (index < input.Length)
        {
            var c = input[index++];

            if (c == '"')
            {
                error = null;
                return true;
            }

            if (c == '\\')
            {
                if (index >= input.Length)
                {
                    builder.Append('\\');
                    continue;
                }

                builder.Append(input[index++]);
                continue;
            }

            builder.Append(c);
        }

        error = MissingClosingQuoteError;
        return false;
    }

    private void ReadUnquotedToken()
    {
        while (index < input.Length && !char.IsWhiteSpace(input[index]) && input[index] != '"')
            index++;
    }
}
