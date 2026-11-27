// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Checking;
using Kimi.Command;

namespace Kimi.Testing;

internal sealed class TestOptions
{
    private readonly Dictionary<string, string> values;
    private readonly List<string> inputs;
    private readonly List<string> errors;

    private TestOptions(string[] args) => this.values = KimiOptions.Read(TestCommand.Name, args, out this.inputs, out this.errors);

    internal KimiOptions Compiler { get; private set; } = new();

    internal string? Input => this.inputs.Count == 0 ? null : this.inputs[0];

    /// <summary>Gets the full path of <c>--results-dir</c>, or <see langword="null"/> for the input's default.</summary>
    internal string? ResultsDirectory { get; private set; }

    internal bool List => this.values.ContainsKey("--list");

    internal bool Json => this.Get("--Format") == "json";

    internal bool AllowEmpty => this.values.ContainsKey("--allow-empty");

    internal int Jobs => this.values.ContainsKey("--no-parallel") ? 1 : this.Number("--jobs", Math.Max(1, Math.Min(Environment.ProcessorCount, 4)));

    /// <summary>Reads test's arguments (<see cref="KimiOptions.Read"/>) before any check, so the result form reads its <c>--Format</c>
    /// also when an option is invalid (<see cref="Validate"/>).</summary>
    /// <param name="args">The arguments, as the parser split them.</param>
    /// <returns>The options read.</returns>
    internal static TestOptions Read(string[] args) => new(args);

    /// <summary>Raises test's first invalid option (IMPL 20.3.2, 20.6.4: 2), a removed one named by its removal (Tatara B6b4), then the
    /// semantic checks of IMPL 20.6.2.</summary>
    internal void Validate()
    {
        if (this.errors.Count != 0)
        {
            throw new CommandInputException(this.errors[0]);
        }

        if (this.inputs.Count > 1)
        {
            throw new CommandInputException("Test accepts one project, source, solution or directory input.");
        }

        if ((this.Get("--filter") is not null && this.Get("--case") is not null) ||
            (this.Get("--jobs") is not null && this.Get("--no-parallel") is not null) || this.Jobs == 0)
        {
            throw new CommandInputException("Invalid or conflicting test selection or concurrency options.");
        }

        foreach (var (name, value) in this.values)
        {
            if (name.EndsWith("-count", StringComparison.Ordinal) || name.EndsWith("-bytes", StringComparison.Ordinal))
            {
                _ = this.Number(name, 0);
            }
        }

        this.Compiler = KimiOptions.From(this.values);
        _ = this.Settings(new());
        if (this.Get("--results-dir") is { } directory)
        {
            // The value is named here, an invalid one an option error before any project is prepared (IMPL 20.6.2, 20.6.4: 2).
            try
            {
                this.ResultsDirectory = Path.GetFullPath(directory);
            }
            catch (Exception ex) when (CheckInputSource.IsReadFailure(ex))
            {
                throw new CommandInputException("Invalid path: --results-dir: " + ex.Message);
            }
        }
    }

    internal string? Get(string name) => this.values.GetValueOrDefault(name);

    internal int Number(string name, int fallback) => this.values.TryGetValue(name, out var text) ?
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw new CommandInputException("Invalid nonnegative integer: " + name) : fallback;

    internal TestSettings Settings(TestSettings defaults)
    {
        var settings = defaults with
        {
            Timeout = this.Get("--timeout") ?? defaults.Timeout,
            RecoveryGrace = this.Get("--recovery-grace") ?? defaults.RecoveryGrace,
            DiagnosticCount = this.Number("--case-diagnostic-count", defaults.DiagnosticCount),
            DiagnosticBytes = this.Number("--case-diagnostic-bytes", defaults.DiagnosticBytes),
            LogBytes = this.Number("--case-log-bytes", defaults.LogBytes),
        };
        // A command option error, not an Input record: the command reports its first (TestCommand's error list).
        return settings.Validate() is [var failure, ..] ? throw new CommandInputException(failure) : settings;
    }
}
