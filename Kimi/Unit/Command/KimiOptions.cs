// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

public class KimiOptions
{
    [SimpleOption("locked")]
    public bool Locked { get; set; }

    [SimpleOption("Target")]
    public string Target { get; set; } = string.Empty;

    [SimpleOption("Debug")]
    public bool Debug { get; set; } = false;

    [SimpleOption("LlvmBin")]
    public string? LlvmBin { get; set; }

    [SimpleOption("ToolchainRoot")]
    public string? ToolchainRoot { get; set; }

    [SimpleOption("AllowUnpinnedToolchain")]
    public bool AllowUnpinnedToolchain { get; set; }

    // SimpleCommandLine requires values for Boolean options. Preserve the specified
    // bare --locked spelling while retaining explicit Boolean values and other options.
    internal static string ExpandFlags(string commandLine)
    {
        if (!commandLine.Contains("-locked", StringComparison.OrdinalIgnoreCase))
        {
            return commandLine;
        }

        var arguments = SimpleParserHelper.SplitArguments(commandLine, default);
        var expanded = ExpandFlags(arguments);
        return ReferenceEquals(arguments, expanded) ? commandLine : string.Join(' ', expanded);
    }

    internal static string[] ExpandFlags(string[] arguments)
    {
        List<string>? expanded = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            if (argument == "--")
            {
                if (expanded is not null)
                {
                    for (; i < arguments.Length; i++)
                    {
                        expanded.Add(arguments[i]);
                    }
                }

                break;
            }

            if ((argument.Equals("--locked", StringComparison.OrdinalIgnoreCase) || argument.Equals("-locked", StringComparison.OrdinalIgnoreCase)) &&
                (i + 1 == arguments.Length || !bool.TryParse(arguments[i + 1], out _)))
            {
                if (expanded is null)
                {
                    expanded = new(arguments.Length + 1);
                    for (var j = 0; j < i; j++)
                    {
                        expanded.Add(arguments[j]);
                    }
                }

                expanded.Add(argument);
                expanded.Add("true");
            }
            else
            {
                expanded?.Add(argument);
            }
        }

        return expanded?.ToArray() ?? arguments;
    }
}
