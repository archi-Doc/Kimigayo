// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Testing;

/// <summary>The single project-level test execution configuration.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record TestSettings
{
    /// <summary>Gets or sets the finite case execution deadline.</summary>
    public string Timeout { get; set; } = "30s";

    /// <summary>Gets or sets the total case recovery grace.</summary>
    public string RecoveryGrace { get; set; } = "5s";

    /// <summary>Gets or sets the retained failure count per case.</summary>
    public int DiagnosticCount { get; set; } = 1000;

    /// <summary>Gets or sets the retained diagnostic bytes per case.</summary>
    public int DiagnosticBytes { get; set; } = 1048576;

    /// <summary>Gets or sets retained bytes per case stdout/stderr stream.</summary>
    public int LogBytes { get; set; } = 4194304;

    /// <summary>Gets or sets additional case environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal static TimeSpan Duration(string text)
    {
        var suffix = text.EndsWith("ms", StringComparison.Ordinal) ? 2 : 1;
        var scale = text.EndsWith("ms", StringComparison.Ordinal) ? 1 : text.EndsWith('s') ? 1000 : text.EndsWith('m') ? 60000 : 0;
        if (scale == 0 || text.Length <= suffix || !long.TryParse(text.AsSpan(0, text.Length - suffix), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0 || value > 86400000 / scale)
        {
            throw new InvalidDataException("Test durations require a positive integer and ms, s or m, at most 24 hours.");
        }

        return TimeSpan.FromMilliseconds(value * scale);
    }

    internal void Validate()
    {
        if (this.Timeout is null || this.RecoveryGrace is null)
        {
            throw new InvalidDataException("Test deadlines cannot be null.");
        }

        _ = Duration(this.Timeout);
        _ = Duration(this.RecoveryGrace);
        if (this.DiagnosticCount < 0 || this.DiagnosticBytes < 0 || this.LogBytes < 0 || this.Environment is null)
        {
            throw new InvalidDataException("Test retention budgets must be nonnegative and Environment must be a map.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in this.Environment)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOfAny(['=', '\0']) >= 0 || value is null || value.Contains('\0') ||
                key.Equals("TMP", StringComparison.OrdinalIgnoreCase) || key.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("KIMI_TEST_", StringComparison.OrdinalIgnoreCase) || !names.Add(key))
            {
                throw new InvalidDataException("Invalid, duplicate or reserved test environment name: " + key);
            }
        }
    }
}
