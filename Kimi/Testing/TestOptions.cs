// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Command;

namespace Kimi.Testing;

internal sealed class TestOptions
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    internal KimiOptions Compiler { get; } = new();

    internal string? Input { get; private set; }

    internal bool List => this.values.ContainsKey("--list");

    internal bool Json => this.Get("--format") == "json";

    internal bool AllowEmpty => this.values.ContainsKey("--allow-empty");

    internal int Jobs => this.values.ContainsKey("--no-parallel") ? 1 : this.Number("--jobs", Math.Max(1, Math.Min(Environment.ProcessorCount, 4)));

    internal static TestOptions Parse(string[] args)
    {
        var result = new TestOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (!name.StartsWith('-'))
            {
                if (result.Input is not null)
                {
                    throw new InvalidDataException("Test accepts one project, source, solution or directory input.");
                }

                result.Input = name;
                continue;
            }

            var flag = name is "--list" or "--allow-empty" or "--no-parallel" or "--locked";
            if (!flag && name is not ("--filter" or "--case" or "--jobs" or "--timeout" or "--recovery-grace" or "--format" or "--results-dir" or
                "--case-diagnostic-count" or "--case-diagnostic-bytes" or "--case-log-bytes" or "--run-diagnostic-count" or "--run-diagnostic-bytes" or "--run-log-bytes" or
                "--Target" or "--Debug" or "--LlvmBin" or "--ToolchainRoot" or "--AllowUnpinnedToolchain"))
            {
                throw new InvalidDataException("Unknown test option: " + name);
            }

            if (!result.values.TryAdd(name, flag ? "true" : ++i < args.Length ? args[i] : throw new InvalidDataException("Missing value: " + name)))
            {
                throw new InvalidDataException("Repeated option: " + name);
            }
        }

        if ((result.Get("--filter") is not null && result.Get("--case") is not null) ||
            (result.Get("--jobs") is not null && result.Get("--no-parallel") is not null) || result.Jobs == 0 || result.Get("--format") is not (null or "text" or "json"))
        {
            throw new InvalidDataException("Invalid or conflicting test selection, concurrency or format options.");
        }

        foreach (var (name, value) in result.values)
        {
            if (name.EndsWith("-count", StringComparison.Ordinal) || name.EndsWith("-bytes", StringComparison.Ordinal))
            {
                _ = result.Number(name, 0);
            }
        }

        result.Compiler.Target = result.Get("--Target") ?? string.Empty;
        result.Compiler.Debug = Boolean("--Debug");
        result.Compiler.Locked = result.Get("--locked") is not null;
        result.Compiler.LlvmBin = result.Get("--LlvmBin");
        result.Compiler.ToolchainRoot = result.Get("--ToolchainRoot");
        result.Compiler.AllowUnpinnedToolchain = Boolean("--AllowUnpinnedToolchain");
        _ = result.Settings(new());
        return result;

        bool Boolean(string name) => result.Get(name) is not { } value ? false : bool.TryParse(value, out var parsed) ? parsed : throw new InvalidDataException("Invalid Boolean: " + name);
    }

    internal string? Get(string name) => this.values.GetValueOrDefault(name);

    internal int Number(string name, int fallback) => this.values.TryGetValue(name, out var text) ?
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException("Invalid nonnegative integer: " + name) : fallback;

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
        settings.Validate();
        return settings;
    }
}
