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

    private const string DurationFailure = "Test durations require a positive integer and ms, s or m, at most 24 hours.";

    internal static TimeSpan Duration(string text)
        => TryDuration(text, out var duration) ? duration : throw new InvalidDataException(DurationFailure);

    // SPEC 23.1.2: each deadline, the budgets and each environment entry are independent; a null deadline has no duration to check.
    internal IReadOnlyList<string> Validate()
    {
        List<string>? failures = null;
        if (this.Timeout is null || this.RecoveryGrace is null)
        {
            (failures ??= []).Add("Test deadlines cannot be null.");
        }

        if ((this.Timeout is { } timeout && !TryDuration(timeout, out _)) || (this.RecoveryGrace is { } grace && !TryDuration(grace, out _)))
        {
            (failures ??= []).Add(DurationFailure);
        }

        if (this.DiagnosticCount < 0 || this.DiagnosticBytes < 0 || this.LogBytes < 0 || this.Environment is null)
        {
            (failures ??= []).Add("Test retention budgets must be nonnegative and Environment must be a map.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in this.Environment ?? new())
        {
            if (string.IsNullOrEmpty(key) || key.IndexOfAny(['=', '\0']) >= 0 || value is null || value.Contains('\0') ||
                key.Equals("TMP", StringComparison.OrdinalIgnoreCase) || key.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("KIMI_TEST_", StringComparison.OrdinalIgnoreCase) || !names.Add(key))
            {
                (failures ??= []).Add("Invalid, duplicate or reserved test environment name: " + key);
            }
        }

        return (IReadOnlyList<string>?)failures ?? [];
    }

    private static bool TryDuration(string text, out TimeSpan duration)
    {
        var suffix = text.EndsWith("ms", StringComparison.Ordinal) ? 2 : 1;
        var scale = text.EndsWith("ms", StringComparison.Ordinal) ? 1 : text.EndsWith('s') ? 1000 : text.EndsWith('m') ? 60000 : 0;
        duration = default;
        if (scale == 0 || text.Length <= suffix ||
            !long.TryParse(text.AsSpan(0, text.Length - suffix), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value <= 0 || value > 86400000 / scale)
        {
            return false;
        }

        duration = TimeSpan.FromMilliseconds(value * scale);
        return true;
    }
}
