// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

public class KimiOptions
{
    // The removed options, each with the text a command line naming it fails with, whatever its value (Tatara B6b4, decision 20). A
    // later removal adds a row.
    private static readonly (string Name, string Text)[] Removed =
    [
        ("ToolchainRoot", "--ToolchainRoot was removed: Kimi carries its toolchain."),
        ("LlvmBin", "--LlvmBin was removed: Kimi carries its toolchain."),
    ];

    [SimpleOption("locked")]
    public bool Locked { get; set; }

    [SimpleOption("Target")]
    public string Target { get; set; } = string.Empty;

    [SimpleOption("Debug")]
    public bool Debug { get; set; } = false;

    /// <summary>Gets or sets the output form of <c>kimi check</c>: <c>text</c> renders the records, <c>json</c> writes the document of SPEC 23.3.</summary>
    [SimpleOption("Format")]
    public string Format { get; set; } = "text";

    /// <summary>Reads a command line's removed options: one text per occurrence before <c>--</c>, in order. An occurrence is named by
    /// SimpleCommandLine's own rule for an option (an option word, <see cref="SimpleParserHelper.IsOptionName"/>, whose name after its
    /// leading '-' equals the option's, ignoring case: <c>SimpleParser.OptionEquals</c>, internal to the parser), so every spelling the
    /// parser binds as an option is named.</summary>
    /// <param name="commandLine">The command line (the text after <c>kimi </c>).</param>
    /// <returns>The texts; empty when the line names no removed option.</returns>
    internal static List<string> RemovedOptions(string commandLine)
    {
        var texts = new List<string>();
        if (!Array.Exists(Removed, x => commandLine.Contains(x.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return texts;
        }

        foreach (var argument in SimpleParserHelper.SplitArguments(commandLine, default))
        {
            if (argument == "--")
            {
                break;
            }

            var name = argument.AsSpan();
            if (name.IsOptionName())
            {
                name = name.TrimStart('-');
                foreach (var (removed, text) in Removed)
                {
                    if (name.Equals(removed, StringComparison.OrdinalIgnoreCase))
                    {
                        texts.Add(text);
                    }
                }
            }
        }

        return texts;
    }

    // SimpleCommandLine requires values for Boolean options. Preserve the specified
    // bare --locked and --no-build spellings while retaining explicit Boolean values and other options.
    internal static string ExpandFlags(string commandLine)
    {
        if (!commandLine.Contains("-locked", StringComparison.OrdinalIgnoreCase) &&
            !commandLine.Contains("-no-build", StringComparison.OrdinalIgnoreCase))
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

            if ((argument.Equals("--locked", StringComparison.OrdinalIgnoreCase) || argument.Equals("-locked", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--no-build", StringComparison.OrdinalIgnoreCase) || argument.Equals("-no-build", StringComparison.OrdinalIgnoreCase)) &&
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
