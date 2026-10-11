// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns rule identity, baseline policy, limit-option names and diagnostic wording.
// Input: the adopted code rules. Output: immutable catalogue rows and NotConfigurable descriptors.
// A rule has one row; consumers never copy its policy into a second switch.
// Must not inspect source or hold per-compilation state.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Kimi.Analyzers;

internal enum CodeRuleMeasure
{
    Forbidden,
    Count,
    Maximum,
}

internal sealed class CodeRule
{
    internal static readonly DiagnosticDescriptor KR1001 = RuleDescriptor("KR1001", "Forbidden dependency", "use the owning phase's published contract");
    internal static readonly DiagnosticDescriptor KR1002 = RuleDescriptor("KR1002", "Forbidden member", "use the canonical published operation");
    internal static readonly DiagnosticDescriptor KR1003 = RuleDescriptor("KR1003", "Closed vocabulary limit", "reuse the closed vocabulary or obtain its owner's decision");
    internal static readonly DiagnosticDescriptor KR1004 = RuleDescriptor("KR1004", "Syntax-keyed side table", "publish the fact through its owning representation");
    internal static readonly DiagnosticDescriptor KR1005 = RuleDescriptor("KR1005", "Supports gate", "replace the gate with the owning operation or data row");
    internal static readonly DiagnosticDescriptor KR1006 = RuleDescriptor("KR1006", "Syntax-based MIR dispatch", "have Binding publish the decision");
    internal static readonly DiagnosticDescriptor KR1007 = RuleDescriptor("KR1007", "Overdue transitional declaration", "remove the temporary structure at its recorded stage");
    internal static readonly DiagnosticDescriptor KR1008 = RuleDescriptor("KR1008", "Unfiltered catch outside a boundary", "handle the specific failure at its boundary");
    internal static readonly DiagnosticDescriptor KR2001 = RuleDescriptor("KR2001", "Parameter count", "pass the owner's plan or split the responsibility");
    internal static readonly DiagnosticDescriptor KR2002 = RuleDescriptor("KR2002", "Out parameter count", "return the owning result");
    internal static readonly DiagnosticDescriptor KR2003 = RuleDescriptor("KR2003", "Failure text through an out parameter", "record a typed failure");
    internal static readonly DiagnosticDescriptor KR2004 = RuleDescriptor("KR2004", "Boolean parameter count", "make the operation's meaning explicit");
    internal static readonly DiagnosticDescriptor KR2005 = RuleDescriptor("KR2005", "Unnamed boolean literal argument", "name the boolean argument");
    internal static readonly DiagnosticDescriptor KR2006 = RuleDescriptor("KR2006", "Statement count", "split the responsibility");
    internal static readonly DiagnosticDescriptor KR2007 = RuleDescriptor("KR2007", "Control nesting", "flatten control flow or split the responsibility");
    internal static readonly DiagnosticDescriptor KR2008 = RuleDescriptor("KR2008", "Logical chain length", "name the decision or use its owning data");
    internal static readonly DiagnosticDescriptor KR2009 = RuleDescriptor("KR2009", "File token count", "split the file by responsibility");
    internal static readonly DiagnosticDescriptor KR2010 = RuleDescriptor("KR2010", "Optional parameter count", "make the operation's contract explicit");
    internal static readonly DiagnosticDescriptor KR3001 = RuleDescriptor("KR3001", "Physical line length", "format the statement across lines");
    internal static readonly DiagnosticDescriptor KR3002 = RuleDescriptor("KR3002", "Nested conditional expression", "name the decision or use explicit control flow");
    internal static readonly DiagnosticDescriptor KR3003 = RuleDescriptor("KR3003", "Computed deconstruction declaration", "declare one new local per statement in evaluation order");

    internal static readonly CodeRule[] All =
    [
        new(KR1001, "architecture", CodeRuleMeasure.Forbidden, string.Empty),
        new(KR1002, "architecture", CodeRuleMeasure.Forbidden, string.Empty),
        new(KR1003, "architecture", CodeRuleMeasure.Forbidden, string.Empty),
        new(KR1004, "architecture", CodeRuleMeasure.Count, string.Empty),
        new(KR1005, "architecture", CodeRuleMeasure.Maximum, string.Empty),
        new(KR1006, "syntax_dispatch", CodeRuleMeasure.Count, string.Empty),
        new(KR1007, "architecture", CodeRuleMeasure.Forbidden, string.Empty),
        new(KR1008, "architecture", CodeRuleMeasure.Count, string.Empty),
        new(KR2001, "accretion", CodeRuleMeasure.Maximum, "max_parameters"),
        new(KR2002, "accretion", CodeRuleMeasure.Maximum, "max_out_parameters"),
        new(KR2003, "accretion", CodeRuleMeasure.Count, string.Empty),
        new(KR2004, "accretion", CodeRuleMeasure.Maximum, "max_bool_parameters"),
        new(KR2005, "accretion", CodeRuleMeasure.Count, string.Empty),
        new(KR2006, "accretion", CodeRuleMeasure.Maximum, "max_statements"),
        new(KR2007, "accretion", CodeRuleMeasure.Maximum, "max_nesting_depth"),
        new(KR2008, "accretion", CodeRuleMeasure.Count, "max_logical_operands"),
        new(KR2009, "accretion", CodeRuleMeasure.Maximum, "max_file_tokens"),
        new(KR2010, "accretion", CodeRuleMeasure.Maximum, "max_optional_parameters"),
        new(KR3001, "density", CodeRuleMeasure.Count, "max_line_length"),
        new(KR3002, "density", CodeRuleMeasure.Count, string.Empty),
        new(KR3003, "density", CodeRuleMeasure.Count, string.Empty),
    ];

    internal static readonly DiagnosticDescriptor Complete = Descriptor("KR9000", "Code rules report complete", "Code rules report complete", DiagnosticSeverity.Info, WellKnownDiagnosticTags.CompilationEnd);
    internal static readonly DiagnosticDescriptor Observation = Descriptor("KR9001", "Code rule observation", "{0}", DiagnosticSeverity.Info);
    internal static readonly DiagnosticDescriptor Stale = Descriptor("KR9002", "Stale code rules entry", "{0}; remove or reduce the obsolete entry", DiagnosticSeverity.Warning);
    internal static readonly DiagnosticDescriptor Invalid = Descriptor("KR9003", "Invalid code rules configuration", "{0}; correct the code rules configuration", DiagnosticSeverity.Error);
    internal static readonly DiagnosticDescriptor Suppression = Descriptor("KR9004", "Forbidden code rule suppression", "{0}; remove the suppression", DiagnosticSeverity.Error);
    internal static readonly DiagnosticDescriptor InvalidException = Descriptor("KR9005", "Invalid code rule exception", "{0}; correct the exception's target, reason or bound", DiagnosticSeverity.Error);
    internal static readonly DiagnosticDescriptor StaleError = Descriptor("KR9002", "Stale code rules entry", "{0}; remove or reduce the obsolete entry", DiagnosticSeverity.Error);
    internal static readonly ImmutableArray<DiagnosticDescriptor> Supported = BuildSupported();

    internal CodeRule(DiagnosticDescriptor diagnostic, string group, CodeRuleMeasure measure, string limitOption)
    {
        this.Group = group;
        this.Measure = measure;
        this.LimitOption = limitOption;
        this.Diagnostic = diagnostic;
    }

    internal string Id => this.Diagnostic.Id;

    internal string Group { get; }

    internal CodeRuleMeasure Measure { get; }

    internal string LimitOption { get; }

    internal DiagnosticDescriptor Diagnostic { get; }

    internal static CodeRule? Find(string id)
    {
        foreach (var rule in All)
        {
            if (rule.Id == id)
            {
                return rule;
            }
        }

        return null;
    }

    internal static DiagnosticDescriptor Descriptor(string id, string title, string message, DiagnosticSeverity severity, params string[] tags)
        => new(
            id,
            title,
            message,
            "CodeRules",
            severity,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/archi-Doc/Kimigayo/blob/dev/docs/dev/COMPILER_ARCHITECTURE.md#code-rules",
            customTags: [WellKnownDiagnosticTags.NotConfigurable, .. tags]);

    internal static Diagnostic StaleEntry(string mode, Location location, string reason)
        => Microsoft.CodeAnalysis.Diagnostic.Create(mode == "Verify" ? StaleError : Stale, location, reason);

    internal Diagnostic Violation(CodeRuleObservation observed, int count, int baseline)
    {
        var bound = "baseline " + baseline.ToString(CultureInfo.InvariantCulture);
        if (this.Measure == CodeRuleMeasure.Count)
        {
            bound = "occurrences " + count.ToString(CultureInfo.InvariantCulture) + ", " + bound;
        }

        return Microsoft.CodeAnalysis.Diagnostic.Create(this.Diagnostic, observed.Location, observed.Key.Scope, observed.Value, observed.Limit, bound);
    }

    private static DiagnosticDescriptor RuleDescriptor(string id, string title, string repair)
        => Descriptor(id, title, title + ": {0} (value {1}, limit {2}; {3}); " + repair, DiagnosticSeverity.Error);

    private static ImmutableArray<DiagnosticDescriptor> BuildSupported()
    {
        var result = ImmutableArray.CreateBuilder<DiagnosticDescriptor>(All.Length + 6);
        foreach (var rule in All)
        {
            result.Add(rule.Diagnostic);
        }

        result.AddRange(Complete, Observation, Stale, Invalid, Suppression, InvalidException);
        return result.MoveToImmutable();
    }
}
