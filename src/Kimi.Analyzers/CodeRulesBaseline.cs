// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns baseline identity, validation and the one merge of complete observations with recorded bounds.
// Input: sorted TSV rows and file-local over-limit observations. Output: rule/stale diagnostics and Report rows.
// Keys sort by scope, rule and anchor; paths locate occurrences but do not identify type-scoped records.
// Must not infer source facts, retain ineffective bounds or accept an incomplete report for pruning.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kimi.Analyzers;

internal readonly struct CodeRuleKey(CodeRule rule, string scope, string anchor) : IComparable<CodeRuleKey>
{
    internal CodeRule Rule { get; } = rule;

    internal string Scope { get; } = scope;

    internal string Anchor { get; } = anchor;

    public int CompareTo(CodeRuleKey other)
    {
        var order = StringComparer.Ordinal.Compare(this.Scope, other.Scope);
        if (order == 0)
        {
            order = StringComparer.Ordinal.Compare(this.Rule.Id, other.Rule.Id);
        }

        return order == 0 ? StringComparer.Ordinal.Compare(this.Anchor, other.Anchor) : order;
    }
}

internal readonly struct CodeRuleObservation(CodeRuleKey key, string path, Location location, int value, int limit, CodeRuleAnnotation? annotation = null)
{
    internal CodeRuleKey Key { get; } = key;

    internal string Path { get; } = path;

    internal Location Location { get; } = location;

    internal int Value { get; } = value;

    internal int Limit { get; } = limit;

    internal CodeRuleAnnotation? Annotation { get; } = annotation;
}

internal sealed class CodeRulesBaseline
{
    private readonly List<Row> rows = [];

    internal static bool Allows(CodeRule rule, string path)
        => rule.Measure != CodeRuleMeasure.Forbidden && !path.StartsWith("src/Kimi/Compiler/Backend/", StringComparison.Ordinal) &&
            (rule.Group == "syntax_dispatch" || !path.StartsWith("src/Kimi/Compiler/Mir/", StringComparison.Ordinal));

    internal static CodeRulesBaseline Read(SourceText text, string path, List<Diagnostic> errors)
    {
        var baseline = new CodeRulesBaseline();
        foreach (var line in text.Lines)
        {
            var content = line.ToString();
            if (content.Length == 0 || content.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = content.Split('\t');
            var location = Location.Create(path, line.Span, text.Lines.GetLinePositionSpan(line.Span));
            var rule = cells.Length == 5 ? CodeRule.Find(cells[0]) : null;
            if (rule is null || !int.TryParse(cells[3], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                value <= 0 || cells[1].Length == 0 || !CodeRulesConfiguration.RelativePath(cells[4]) || !Allows(rule, cells[4]) ||
                !ValidAnchor(rule, cells[2]))
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, location, "Invalid baseline row"));
                continue;
            }

            var key = new CodeRuleKey(rule, cells[1], cells[2]);
            if (baseline.rows.Count != 0 && baseline.rows[baseline.rows.Count - 1].Key.CompareTo(key) >= 0)
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, location, "Baseline keys must be unique and sorted by scope, rule and anchor"));
            }

            baseline.rows.Add(new Row(key, value, location));
        }

        return baseline;
    }

    internal void Complete(List<CodeRuleObservation> observations, string mode, Action<Diagnostic> report, CancellationToken cancellation)
    {
        observations.Sort(CompareObservations);
        var baselineIndex = 0;
        for (var start = 0; start < observations.Count;)
        {
            cancellation.ThrowIfCancellationRequested();
            var first = observations[start];
            var end = start + 1;
            var maximum = first.Value;
            var annotation = first.Annotation;
            var duplicate = false;
            while (end < observations.Count && first.Key.CompareTo(observations[end].Key) == 0)
            {
                maximum = Math.Max(maximum, observations[end].Value);
                if (observations[end].Annotation is { } next)
                {
                    duplicate |= annotation is not null;
                    annotation ??= next;
                }

                end++;
            }

            while (baselineIndex < this.rows.Count && this.rows[baselineIndex].Key.CompareTo(first.Key) < 0)
            {
                Stale(this.rows[baselineIndex++], mode, report, "No over-limit occurrence remains for");
            }

            Row? recorded = baselineIndex < this.rows.Count && this.rows[baselineIndex].Key.CompareTo(first.Key) == 0 ? this.rows[baselineIndex++] : null;
            var baselineValue = recorded?.Value ?? 0;
            var value = first.Key.Rule.Measure == CodeRuleMeasure.Count ? end - start : maximum;
            if (annotation is not null)
            {
                var problem = duplicate ? "Duplicate exception" : recorded.HasValue ? "Exception also has a baseline" :
                    value > annotation.Limit ? "Measured value " + value.ToString(CultureInfo.InvariantCulture) + " exceeds exception limit " + annotation.Limit.ToString(CultureInfo.InvariantCulture) : null;
                if (problem is not null)
                {
                    report(Diagnostic.Create(CodeRule.InvalidException, annotation.Location, problem + " for " + first.Key.Rule.Id));
                }

                if (mode == "Report")
                {
                    report(Report(first, value, annotation));
                }

                start = end;
                continue;
            }

            if (first.Key.Rule.Measure == CodeRuleMeasure.Maximum && value <= first.Limit)
            {
                if (recorded.HasValue)
                {
                    Stale(recorded.Value, mode, report, "No over-limit occurrence remains for");
                }

                start = end;
                continue;
            }

            if (recorded.HasValue && baselineValue > value)
            {
                Stale(recorded.Value, mode, report, "A smaller value (" + value.ToString(CultureInfo.InvariantCulture) + ") remains for");
            }

            for (var index = start; index < end; index++)
            {
                var observed = observations[index];
                var allowed = Allows(observed.Key.Rule, observed.Path) ? baselineValue : 0;
                if (mode == "Report")
                {
                    report(Report(observed, value, null));
                }
                else if (observed.Key.Rule.Measure == CodeRuleMeasure.Count ? value > allowed : observed.Value > Math.Max(observed.Limit, allowed))
                {
                    report(observed.Key.Rule.Violation(observed, end - start, allowed));
                }
            }

            start = end;
        }

        while (baselineIndex < this.rows.Count)
        {
            Stale(this.rows[baselineIndex++], mode, report, "No over-limit occurrence remains for");
        }
    }

    private static bool ValidAnchor(CodeRule rule, string anchor)
    {
        if (rule.Measure == CodeRuleMeasure.Maximum)
        {
            return anchor == "file" || (anchor.StartsWith("member:", StringComparison.Ordinal) && anchor.Length > 7) ||
                (anchor.StartsWith("local:", StringComparison.Ordinal) && anchor.Length > 6);
        }

        if (anchor.Length != 21 || !(anchor.StartsWith("node:", StringComparison.Ordinal) || anchor.StartsWith("line:", StringComparison.Ordinal)))
        {
            return false;
        }

        for (var i = 5; i < anchor.Length; i++)
        {
            if (!(anchor[i] is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareObservations(CodeRuleObservation left, CodeRuleObservation right)
    {
        var order = left.Key.CompareTo(right.Key);
        if (order == 0)
        {
            order = StringComparer.Ordinal.Compare(left.Path, right.Path);
        }

        return order == 0 ? left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start) : order;
    }

    private static Diagnostic Report(CodeRuleObservation observed, int value, CodeRuleAnnotation? annotation)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("rule", observed.Key.Rule.Id).Add("scope", observed.Key.Scope).Add("anchor", observed.Key.Anchor)
            .Add("value", value.ToString(CultureInfo.InvariantCulture)).Add("path", observed.Path)
            .Add("baselineAllowed", annotation is null && Allows(observed.Key.Rule, observed.Path) ? "true" : "false");
        if (annotation is not null)
        {
            properties = properties.Add("kind", "exception").Add("reason", annotation.Reason)
                .Add("limit", annotation.Limit.ToString(CultureInfo.InvariantCulture));
        }

        return Diagnostic.Create(CodeRule.Observation, observed.Location, properties, observed.Key.Rule.Id + " " + observed.Key.Scope + " " + observed.Key.Anchor);
    }

    private static void Stale(Row row, string mode, Action<Diagnostic> report, string reason)
        => report(CodeRule.StaleEntry(
            mode,
            row.Location,
            reason + " " + row.Key.Rule.Id + " " + row.Key.Scope + " " + row.Key.Anchor));

    private readonly struct Row(CodeRuleKey key, int value, Location location)
    {
        internal CodeRuleKey Key { get; } = key;

        internal int Value { get; } = value;

        internal Location Location { get; } = location;
    }
}
