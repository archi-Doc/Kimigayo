// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns immutable configuration for one analyzer compilation.
// Input: Roslyn global options and the two AdditionalFiles. Output: validated paths, limits, lists and byte identities.
// Parse once; effective options must equal the checksummed globalconfig, so no other config can change policy.
// Must not read the filesystem or turn invalid/missing input into a silent default.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Kimi.Analyzers;

internal sealed class CodeRulesConfiguration
{
    internal const string Prefix = "kimi_code_rules.";
    internal static readonly string[] Stages = ["R0", "R1", "R2a", "R2b", "R3", "R4", "R5", "R6", "R7", "R8", "R9"];
    private readonly Dictionary<string, string[]> lists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> limits = new(StringComparer.Ordinal);

    private CodeRulesConfiguration(string root, string mode, AdditionalText baselineFile, SourceText baseline, AdditionalText configFile, SourceText config, ImmutableDictionary<string, string?> checksums)
    {
        this.Root = root.Replace('\\', '/').TrimEnd('/') + "/";
        this.Mode = mode;
        this.BaselineFile = baselineFile.Path;
        this.Baseline = baseline;
        this.Checksums = checksums;
        this.ConfigFile = configFile.Path;
        this.Config = config;
    }

    internal string Root { get; }

    internal string Mode { get; }

    internal int Stage { get; private set; } = -1;

    internal string BaselineFile { get; }

    internal SourceText Baseline { get; }

    internal ImmutableDictionary<string, string?> Checksums { get; }

    internal IReadOnlyDictionary<string, string[]> Lists => this.lists;

    private string ConfigFile { get; }

    private SourceText Config { get; }

    internal static CodeRulesConfiguration? Read(AnalyzerOptions options, CancellationToken cancellation, List<Diagnostic> errors)
    {
        var global = options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!global.TryGetValue("build_property.KimiRepositoryRoot", out var root) || !AbsoluteRoot(root))
        {
            errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "KimiRepositoryRoot must be an absolute repository path"));
            return null;
        }

        var mode = global.TryGetValue("build_property.KimiCodeRulesMode", out var configuredMode) ? configuredMode : "Enforce";
        if (mode is not ("Enforce" or "Verify" or "Report"))
        {
            errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "KimiCodeRulesMode must be Enforce, Verify or Report"));
        }

        var configFile = File(options, "CodeRules.globalconfig", errors);
        var baselineFile = File(options, "CodeRules.baseline.tsv", errors);
        var config = configFile?.GetText(cancellation);
        var baseline = baselineFile?.GetText(cancellation);
        if (config is null || baseline is null)
        {
            errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "The code rules AdditionalFiles must be readable"));
            return null;
        }

        var checksums = ImmutableDictionary<string, string?>.Empty
            .Add("configChecksum", Hex(config.GetChecksum())).Add("configAlgorithm", config.ChecksumAlgorithm.ToString())
            .Add("baselineChecksum", Hex(baseline.GetChecksum())).Add("baselineAlgorithm", baseline.ChecksumAlgorithm.ToString());
        var result = new CodeRulesConfiguration(root, mode, baselineFile!, baseline, configFile!, config, checksums);

        // Another globalconfig with a higher level could otherwise change effective policy without changing this file.
        // Only the global section is read, so the validated root supplies the absolute path Roslyn requires.
        var parsed = AnalyzerConfig.Parse(config, Path.Combine(root, "CodeRules.globalconfig"));
        var declared = AnalyzerConfigSet.Create(new[] { parsed }).GlobalConfigOptions.AnalyzerOptions;
        foreach (var key in declared.Keys.Concat(global.Keys).Where(x => x.StartsWith(Prefix, StringComparison.Ordinal)).Distinct())
        {
            if (!declared.TryGetValue(key, out var expected) || !global.TryGetValue(key, out var actual) || expected != actual)
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, result.OptionLocation(key.Substring(Prefix.Length)), "Option differs from CodeRules.globalconfig: " + key));
            }
        }

        var requiredLimits = new HashSet<string>(CodeRule.All.Select(x => x.LimitOption).Where(x => x.Length != 0), StringComparer.Ordinal);
        foreach (var key in global.Keys)
        {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!global.TryGetValue(key, out var value))
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "Unreadable option: " + key));
                continue;
            }

            var name = key.Substring(Prefix.Length);
            if (name == "stage")
            {
                result.Stage = Array.IndexOf(Stages, value);
                if (result.Stage < 0)
                {
                    errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "Unknown removal stage: " + value));
                }
            }
            else if (requiredLimits.Contains(name))
            {
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0)
                {
                    result.limits.Add(name, limit);
                }
                else
                {
                    errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, name + " must be a positive integer"));
                }
            }
            else if (IsList(name))
            {
                var entries = value.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
                if (entries.Length == 0 || entries.Any(x => x.Length == 0))
                {
                    errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, name + " must contain entries"));
                }

                if (name.StartsWith("scope.", StringComparison.Ordinal) || name.StartsWith("layer.", StringComparison.Ordinal) || name == "catch_boundaries")
                {
                    if (entries.Any(x => !RelativePath(x)))
                    {
                        errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, name + " must contain normalized repository paths"));
                    }
                }

                result.lists.Add(name, entries);
            }
            else
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "Unknown option: " + key));
            }
        }

        foreach (var name in requiredLimits)
        {
            if (!result.limits.ContainsKey(name))
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "Missing limit: " + name));
            }
        }

        if (result.Stage < 0)
        {
            errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "A current removal stage is required"));
        }

        return errors.Count == 0 ? result : null;
    }

    internal static bool RelativePath(string path)
        => path.StartsWith("src/Kimi/", StringComparison.Ordinal) && path.IndexOf('\\') < 0 &&
            path.IndexOf("//", StringComparison.Ordinal) < 0 && path.IndexOf(':') < 0 &&
            !path.Split('/').Any(x => x is "." or "..");

    internal string? Relative(string absolute)
    {
        var normalized = absolute.Replace('\\', '/');
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalized.StartsWith(this.Root, comparison) ? normalized.Substring(this.Root.Length) : null;
    }

    internal int Limit(CodeRule rule) => rule.LimitOption.Length == 0 ? 0 : this.limits[rule.LimitOption];

    internal Location OptionLocation(string option)
    {
        foreach (var line in this.Config.Lines)
        {
            var content = line.ToString();
            var equals = content.IndexOf('=');
            if (equals >= 0 && content.Substring(0, equals).Trim() == Prefix + option)
            {
                return Microsoft.CodeAnalysis.Location.Create(this.ConfigFile, line.Span, this.Config.Lines.GetLinePositionSpan(line.Span));
            }
        }

        return Microsoft.CodeAnalysis.Location.Create(this.ConfigFile, default, default);
    }

    internal bool Applies(CodeRule rule, string path)
    {
        if (!path.StartsWith("src/Kimi/", StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = "scope." + rule.Group;
        if (this.lists.TryGetValue(prefix + ".include", out var includes) && !includes.Any(x => path.StartsWith(x, StringComparison.Ordinal)))
        {
            return false;
        }

        return !this.lists.TryGetValue(prefix + ".exclude", out var excludes) || !excludes.Any(x => path.StartsWith(x, StringComparison.Ordinal));
    }

    private static bool AbsoluteRoot(string root)
    {
        if (root.IndexOf('\0') >= 0)
        {
            return false;
        }

        var pathRoot = Path.GetPathRoot(root);
        return !string.IsNullOrEmpty(pathRoot) && (Path.DirectorySeparatorChar != '\\' ||
            (pathRoot.Length >= 3 && pathRoot[1] == ':' && (pathRoot[2] is '/' or '\\')) ||
            pathRoot.StartsWith("\\\\", StringComparison.Ordinal));
    }

    private static string Hex(ImmutableArray<byte> bytes) => string.Concat(bytes.Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));

    private static AdditionalText? File(AnalyzerOptions options, string name, List<Diagnostic> errors)
    {
        AdditionalText? found = null;
        foreach (var file in options.AdditionalFiles)
        {
            if (Path.GetFileName(file.Path) != name)
            {
                continue;
            }

            if (found is not null)
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "Duplicate AdditionalFile: " + name));
            }

            found = file;
        }

        if (found is null)
        {
            errors.Add(Diagnostic.Create(CodeRule.Invalid, Location.None, "Missing AdditionalFile: " + name));
        }

        return found;
    }

    private static bool IsList(string name)
        => name == "catch_boundaries" || name == "vocabularies" ||
            CodeRule.All.Any(x => name == "scope." + x.Group + ".include" || name == "scope." + x.Group + ".exclude") ||
            name.StartsWith("layer.", StringComparison.Ordinal) || name.StartsWith("deny_types.", StringComparison.Ordinal) ||
            name.StartsWith("deny_members.", StringComparison.Ordinal) || name.StartsWith("deny_layers.", StringComparison.Ordinal) ||
            name.StartsWith("allow_types.", StringComparison.Ordinal);
}
