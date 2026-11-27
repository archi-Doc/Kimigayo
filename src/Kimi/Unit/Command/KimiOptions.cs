// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

// The command line's options (IMPL 20.3, 20.3.2, 20.5, 20.6.2; PLAN G1236, G1237): one table, a row per option, and one reader for every
// command, after lsp's debugger option has been read. Inputs: a command's name (its class's Name) and its arguments as the parser split them. Outputs: the values by row
// name, a Boolean or a listed value in the table's spelling; the inputs, in order; the invalid options, in order, each one text, which
// CommandExecution prints each of (exit 1) and TestOptions raises the first of (exit 2). The rule (the owner's O-525 and O-526): an
// option word (SimpleParserHelper.IsOptionName) names the row whose name equals it after their leading dashes, ignoring case; a word of
// dashes alone is an input; a value is never an option word; a Boolean value is read as bool.TryParse reads it, a listed value ignoring
// case; --locked and --no-build alone mean true; an option the command does not take, a removed one, a missing or invalid value and a
// repeated option are invalid. Invariants: each option one row; a command takes the rows naming it (IMPL's, with restore's --Target,
// the owner's O-527); consumers compare values exactly. Prohibitions: no command reads an option word itself and no other copy of the
// rule (the parser splits the line and dispatches the command); a new option or removal adds a row.
public class KimiOptions
{
    private static readonly string[] Compiling = [CheckCommand.Name, EmitCommand.Name, BuildCommand.Name, RunCommand.Name, TestCommand.Name];
    private static readonly string[] Testing = [TestCommand.Name];

    // Each option once: its name as IMPL spells it, its kind, the commands that take it and the values IMPL lists for it (a path, a
    // target, a number or a filter text has none: its consumer reads it, in its case).
    private static readonly (string Name, Kind Kind, string[] Commands, string[]? Values)[] Table =
    [
        ("--Target", Kind.Value, [.. Compiling, RestoreCommand.Name], null),
        ("--Debug", Kind.Boolean, Compiling, null),
        ("--locked", Kind.BareBoolean, Compiling, null),
        ("--Format", Kind.Value, [CheckCommand.Name, TestCommand.Name], ["text", "json"]),
        ("--Manifest", Kind.Value, [BuildCommand.Name], null),
        ("--no-build", Kind.BareBoolean, [RunCommand.Name], null),
        ("--Report", Kind.Value, [ToolchainCommand.Name], null),
        ("--list", Kind.Flag, Testing, null),
        ("--allow-empty", Kind.Flag, Testing, null),
        ("--no-parallel", Kind.Flag, Testing, null),
        ("--filter", Kind.Value, Testing, null),
        ("--case", Kind.Value, Testing, null),
        ("--jobs", Kind.Value, Testing, null),
        ("--timeout", Kind.Value, Testing, null),
        ("--recovery-grace", Kind.Value, Testing, null),
        ("--results-dir", Kind.Value, Testing, null),
        ("--case-diagnostic-count", Kind.Value, Testing, null),
        ("--case-diagnostic-bytes", Kind.Value, Testing, null),
        ("--case-log-bytes", Kind.Value, Testing, null),
        ("--run-diagnostic-count", Kind.Value, Testing, null),
        ("--run-diagnostic-bytes", Kind.Value, Testing, null),
        ("--run-log-bytes", Kind.Value, Testing, null),
        ("--ToolchainRoot", Kind.Removed, [], null),
        ("--LlvmBin", Kind.Removed, [], null),
    ];

    private enum Kind : byte
    {
        Value, // Takes the next word.
        Boolean, // Takes the next word, true or false.
        BareBoolean, // A Boolean that alone means true: it takes the next word only when that is true or false.
        Flag, // Takes no word: its presence.
        Removed, // Invalid on every command, named by its removal, whatever its value (Tatara B6b4, decision 20; IMPL 20.3.2).
    }

    public bool Locked { get; set; }

    public string Target { get; set; } = string.Empty;

    public bool Debug { get; set; } = false;

    /// <summary>Gets or sets the output form of <c>kimi check</c>: <c>text</c> renders the records, <c>json</c> writes the document of SPEC 23.3.</summary>
    public string Format { get; set; } = "text";

    public string? Manifest { get; set; }

    public bool NoBuild { get; set; }

    public string? Report { get; set; }

    /// <summary>Reads a command's arguments by the table (the rule at the top of this file).</summary>
    /// <param name="command">The command's name.</param>
    /// <param name="args">The arguments, as the parser split them.</param>
    /// <param name="inputs">The inputs, in order.</param>
    /// <param name="errors">The invalid options' texts, in order.</param>
    /// <returns>The values by row name; a flag's value is <c>true</c>.</returns>
    internal static Dictionary<string, string> Read(string command, string[] args, out List<string> inputs, out List<string> errors)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        inputs = [];
        errors = [];
        for (var i = 0; i < args.Length; i++)
        {
            var word = args[i];
            var name = word.AsSpan().TrimStart('-');
            if (!word.AsSpan().IsOptionName() || name.IsEmpty)
            {
                inputs.Add(word);
                continue;
            }

            var key = name.ToString();
            var row = Array.FindIndex(Table, x => x.Name.AsSpan(2).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (row < 0 || (Table[row].Kind != Kind.Removed && !Table[row].Commands.Contains(command)))
            {
                errors.Add("Unknown option: " + word);
                continue;
            }

            var (option, kind, _, listed) = Table[row];
            if (kind == Kind.Removed)
            {
                errors.Add(option + " was removed: Kimi carries its toolchain.");
                continue;
            }

            var next = i + 1 < args.Length && !args[i + 1].AsSpan().IsOptionName() ? args[i + 1] : null;
            var value = "true";
            if (kind is Kind.Value or Kind.Boolean || (kind == Kind.BareBoolean && bool.TryParse(next, out _)))
            {
                if (next is null)
                {
                    errors.Add("Missing value: " + option);
                    continue;
                }

                value = next;
                i++;
            }

            // A Boolean as bool.TryParse reads it (case and surrounding white space ignored), a listed value ignoring case (the owner's
            // O-525; the Middle's D1: today's reading), each kept in the table's spelling.
            var read = kind is Kind.Boolean or Kind.BareBoolean ? (bool.TryParse(value, out var truth) ? (truth ? "true" : "false") : null) :
                listed is null ? value : Array.Find(listed, x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (read is null)
            {
                errors.Add($"Invalid value: {option} {value}");
            }
            else if (!values.TryAdd(option, read))
            {
                errors.Add("Repeated option: " + option);
            }
        }

        return values;
    }

    /// <summary>The compiler options among a command's values.</summary>
    /// <param name="values">The values <see cref="Read"/> returned.</param>
    /// <returns>The options.</returns>
    internal static KimiOptions From(Dictionary<string, string> values)
    {
        return new()
        {
            Target = values.GetValueOrDefault("--Target") ?? string.Empty,
            Debug = Boolean("--Debug"),
            Locked = Boolean("--locked"),
            Format = values.GetValueOrDefault("--Format") ?? "text",
            Manifest = values.GetValueOrDefault("--Manifest"),
            NoBuild = Boolean("--no-build"),
            Report = values.GetValueOrDefault("--Report"),
        };

        bool Boolean(string name) => values.GetValueOrDefault(name) == "true";
    }
}
