// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Plan R2a U11a, removed by U11b: with KIMI_FIT_SHADOW set, each outermost call selection compares the initializer bounds that the
// candidate rollback applies with those of the post-selection rule: a fit inside candidate evaluation is pure (FitsOriginOutlives),
// a fit outside it records at once, and the selected call's bounds are recorded by its judgment and the collector (CollectFitBounds).
// Every bound applied outside a selection is inventoried. "1" throws when a bound is on one side only or a pure answer differs; any
// other value only records. KIMI_FIT_SHADOW_LOG names a file each event is appended to.
public sealed partial class Binding
{
    private static readonly string? FitShadowMode = Environment.GetEnvironmentVariable("KIMI_FIT_SHADOW");
    private static readonly bool FitShadow = FitShadowMode is { Length: > 0 };
    private static readonly string? FitShadowLog = Environment.GetEnvironmentVariable("KIMI_FIT_SHADOW_LOG");
    private static readonly Lock FitShadowLock = new();
    private readonly List<ShadowKey>? shadowApplied = FitShadow ? new() : null;
    private readonly List<(ShadowKey Key, string Source, string Site)>? shadowRecords = FitShadow ? new() : null;
    private bool shadowFit;
    private int shadowWindow;

    private static void Log(string kind, string text)
    {
        if (FitShadowLog is { } path)
        {
            lock (FitShadowLock)
            {
                File.AppendAllText(path, $"{kind}\t{text}\n");
            }
        }
    }

    private static string Describe(in ShadowKey key)
        => $"{Text(key.Owner)}\t{Text(key.Use)}\t{OriginText(key.Variable)}\t{OriginText(key.Bound)}\t{key.Condition}";

    private static string OriginText(BoundOrigin origin)
        => origin.Kind == OriginKind.Intersection ? "(" + string.Join(" & ", origin.Operands.Select(OriginText)) + ")" : $"{origin.Kind}:{origin.Name}#{RuntimeHelpers.GetHashCode(origin)}";

    private static string Text(Koto node)
    {
        var text = node.ToString().Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return $"{node.GetType().Name}#{RuntimeHelpers.GetHashCode(node)}@{node.Span.Start}:{(text.Length > 80 ? text[..80] : text)}";
    }

    // The Binding frames above the fit machinery, innermost first.
    private static string Site()
    {
        var builder = new StringBuilder();
        foreach (var line in new StackTrace(2, false).ToString().Split('\n'))
        {
            var start = line.IndexOf("Kimi.Compiler.Binding.", StringComparison.Ordinal);
            var end = line.IndexOf('(', StringComparison.Ordinal);
            if (start >= 0 && end > start && line[(start + 22)..end] is var name &&
                name is not ("FitOrigin" or "ShadowFitOriginOutlives" or "FitOriginOutlives" or "BoundInitializerOrigin" or "FitsOriginOutlives" or "CollectInitializerBound"))
            {
                builder.Append(builder.Length == 0 ? string.Empty : "<").Append(name);
            }
        }

        return builder.ToString();
    }

    // A fit's FitOriginOutlives with its pure answer checked and its records marked as a fit's.
    private bool ShadowFitOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition)
    {
        var pure = this.FitsOriginOutlives(longer, shorter, use, condition);
        var fit = this.shadowFit;
        this.shadowFit = true;
        try
        {
            var applied = this.FitOriginOutlives(longer, shorter, use, condition);
            if (applied != pure)
            {
                Log("pure", $"{applied}\t{Text(use)}\t{OriginText(longer)}\t{OriginText(shorter)}");
                if (FitShadowMode == "1")
                {
                    throw new InvalidOperationException($"KIMI_FIT_SHADOW: the pure fit of `{OriginText(longer)}` outliving `{OriginText(shorter)}` differs.");
                }
            }

            return applied;
        }
        finally
        {
            this.shadowFit = fit;
        }
    }

    // The collector's record of a bound (FitsOriginOutlives while collectingFits).
    private void CollectInitializerBound(VariableKoto local, BoundOrigin variable, BoundOrigin bound, Koto use, ulong condition)
        => this.ShadowRecord(new(local, variable, bound, use, condition), "collect");

    // A bound recorded by the old path (`source` null: a fit's or the call judgment's) or by the collector. Outside a selection it is
    // applied at once and inventoried; inside, the new rule applies every record except a fit's inside candidate evaluation.
    private void ShadowRecord(ShadowKey key, string? source)
    {
        var site = Site();
        source ??= !this.shadowFit ? "judge" : site.Contains("EvaluateCallCandidate", StringComparison.Ordinal) ? "fit-trial" : "fit";
        if (this.candidateBoundDepth == 0)
        {
            Log("outside-" + source, $"{site}\t{Describe(key)}");
            return;
        }

        this.shadowRecords!.Add((key, source, site));
        if (source != "fit-trial")
        {
            this.shadowApplied!.Add(key);
        }
    }

    // The outermost selection ends: the bounds the rollback kept against those the new rule applies. A bound on one side only is a
    // mismatch; a bound on both sides a different number of times differs only in multiplicity, which applying it cannot observe
    // (a meet with itself and a repeated obligation change nothing).
    private void CompareShadowBounds()
    {
        var (records, appliedKeys) = (this.shadowRecords!, this.shadowApplied!);
        if (this.candidateBounds.Count + records.Count == 0)
        {
            return;
        }

        var window = $"{RuntimeHelpers.GetHashCode(this)}:{++this.shadowWindow}";
        var kept = new Dictionary<ShadowKey, int>();
        foreach (var (declaration, variable, bound, use, condition) in this.candidateBounds)
        {
            var key = new ShadowKey(declaration.Owner, variable, bound, use, condition);
            kept[key] = kept.GetValueOrDefault(key) + 1;
            Log("kept", $"{window}\t{Describe(key)}");
        }

        var applied = new Dictionary<ShadowKey, int>();
        foreach (var key in appliedKeys)
        {
            applied[key] = applied.GetValueOrDefault(key) + 1;
        }

        foreach (var (key, source, site) in records)
        {
            Log("record", $"{window}\t{source}\t{site}\t{Describe(key)}\t{kept.GetValueOrDefault(key)}\t{applied.GetValueOrDefault(key)}");
        }

        var mismatches = 0;
        foreach (var key in kept.Keys.Union(applied.Keys))
        {
            var (k, a) = (kept.GetValueOrDefault(key), applied.GetValueOrDefault(key));
            if (k != a)
            {
                var kind = k == 0 ? "applied-not-kept" : a == 0 ? "kept-not-applied" : "multiplicity";
                mismatches += kind == "multiplicity" ? 0 : 1;
                Log(kind, $"{window}\t{Describe(key)}\t{k}\t{a}");
            }
        }

        Log(mismatches == 0 ? "window-match" : "window-mismatch", $"{window}\t{this.candidateBounds.Count}\t{appliedKeys.Count}\t{records.Count}");
        appliedKeys.Clear();
        records.Clear();
        if (mismatches != 0 && FitShadowMode == "1")
        {
            throw new InvalidOperationException($"KIMI_FIT_SHADOW: {mismatches} initializer bounds differ between the candidate rollback and the post-selection rule.");
        }
    }

    // Koto and BoundOrigin compare by reference.
    private readonly record struct ShadowKey(Koto Owner, BoundOrigin Variable, BoundOrigin Bound, Koto Use, ulong Condition);
}
