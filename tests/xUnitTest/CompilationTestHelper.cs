// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

internal static class CompilationTestHelper
{
    /// <summary>The prefix of a library document's location.</summary>
    internal const string Library = "Kimi/";

    // Preparation and parsing only: callers choose when to Bind and analyze ownership.
    internal static Compilation Parse(string source, string? path = null)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        if (path is null)
        {
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        }
        else
        {
            c.Kotonoha.AddSource(new SourceDocument(path, source));
        }

        return c;
    }

    internal static Compilation ParseSuccess(string source, string? path = null)
    {
        var c = Parse(source, path);
        Assert.Empty(TestDiagnostics.Of(c));
        return c;
    }

    internal static Compilation BindSuccess(string source)
    {
        var c = ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        return c;
    }

    // Restore syntax without Binding or ownership analysis so invalidation stays observable.
    internal static Compilation Reload(Compilation c)
    {
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        Assert.Same(restored.Kotonoha, tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    internal static string WriteIr(Compilation c)
    {
        using var writer = new StringWriter();
        if (!c.Generate(writer))
        {
            Assert.Fail(MinimalEmissionTest.Describe(c));
        }

        return writer.ToString();
    }

    // Binding's warnings of the latest final pass, such as its HIR lints (SPEC 17.5), as ReportDiagnostics publishes them.
    internal static TestDiagnostic[] BindingWarnings(Compilation c)
    {
        c.Binding.ReportDiagnostics();
        return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Warning)];
    }

    // The Errors published once Binding reports the latest final pass, its HIR lints included (SPEC 14.3, 14.4, 14.5.2).
    internal static TestDiagnostic[] BindingErrors(Compilation c)
    {
        c.Binding.ReportDiagnostics();
        return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error)];
    }

    // The Errors a check publishes for an analyzed program: Binding's (its HIR lints included), the startup check's and the analysis's
    // (SPEC 23.1.2). The MIR must exist, so a caller that did not analyze fails instead of passing vacuously.
    internal static TestDiagnostic[] PublishedErrors(Compilation c)
    {
        Assert.NotEqual(0UL, c.ProgramVersion);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.ReportAnalysis();
        return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error)];
    }

    // An acceptance row's verdict (SPEC 8.3, 23.1.2): `records` is empty for a valid program, which has no Error and generates. Otherwise it
    // lists the program's Errors as `Code: source line`, trimmed and separated by ` | `, each pair once: SPEC 23 fixes neither the span inside
    // a line nor how many records one failed rule gives. `*` for a code or a line leaves it to the implementation, `Resource` for a code is
    // any code of that category, and a last entry `...` leaves open the records resting on the listed ones. Neither wildcard admits
    // Unsupported_Kd (the form is valid, SPEC 23.1.1) or InternalInvariant_Kd (a defect): an entry must name them.
    internal static void AssertRecordsAtLines(string form, string program, string records)
    {
        var c = MinimalEmissionTest.Analyze(program);
        AssertLines(form, program, records, PublishedErrors(c));
        if (records.Length == 0)
        {
            Assert.True(c.Generate(TextWriter.Null), $"{form}: {MinimalEmissionTest.Describe(c)}");
        }
    }

    // The same verdict over Binding's own records, for a rule a later phase's Unsupported_Kd would hide.
    internal static void AssertBindingRecordsAtLines(string form, string program, string records)
        => AssertLines(form, program, records, BindingErrors(MinimalEmissionTest.Analyze(program)));

    // The problem codes of a Comparison Loan conflict: the conflicting access decides the code (the census pairs, MirCensusTest.Pairs).
    internal static bool IsLoanConflict(this DiagnosticCode code)
        => code is DiagnosticCode.ComparisonLoanConflict_Kd or DiagnosticCode.CallReservationConflict_Kd or DiagnosticCode.CallActivationConflict_Kd or DiagnosticCode.PlacementActivationConflict_Kd;

    // The problem codes of an unproven Origin obligation: what is unproven decides the code (the census pairs, MirCensusTest.Pairs).
    internal static bool IsUnprovenOrigin(this DiagnosticCode code)
        => code is DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd;

    /// <summary>Gets a node's location as its document and start offset; a library document is <c>Kimi/</c> and its file name.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The location, or <c>?</c> without a node.</returns>
    internal static string Location(Koto? node)
        => node is null ? "?" : Document(node) + ":" + node.Span.Start;

    private static string Document(Koto? node)
    {
        var path = node?.CodeContext?.SourceDocument?.Path;
        return path is null ? "?" : path.StartsWith("compiler://", StringComparison.Ordinal) ? Library + path[(path.LastIndexOf('/') + 1)..] : path;
    }

    private static void AssertLines(string form, string program, string records, TestDiagnostic[] errors)
    {
        var actual = errors.Select(x => (x.Code, Line: LineAt(program, x.Span.Start))).Distinct().ToArray();
        var entries = records.Split(" | ", StringSplitOptions.RemoveEmptyEntries);
        var expected = entries.Where(static x => x != "...").ToArray();
        var named = expected.All(e => actual.Any(a => NamesRecord(e, a.Code, a.Line)));
        var unnamed = actual.Any(a => a.Code is nameof(DiagnosticCode.Unsupported_Kd) or nameof(DiagnosticCode.InternalInvariant_Kd) && !expected.Any(e => e.StartsWith($"{a.Code}: ", StringComparison.Ordinal)));
        var counted = entries.LastOrDefault() == "..." || actual.Length == expected.Length;
        Assert.True(named && !unnamed && counted, $"{form}: expected [{records}], found [{string.Join(" | ", actual.Select(static x => $"{x.Code}: {x.Line}"))}]");
    }

    private static bool NamesRecord(string entry, string code, string line)
    {
        var colon = entry.IndexOf(": ", StringComparison.Ordinal);
        var (wanted, at) = (entry[..colon], entry[(colon + 2)..]);
        var category = wanted == "Resource" && Enum.TryParse<DiagnosticCode>(code, out var parsed) && DiagnosticEntries.TryGet(parsed, out var written) &&
            written.Category == Kimi.Diagnostics.DiagnosticCategory.Resource;
        return (wanted == code || wanted == "*" || category) && (at == "*" || at == line);
    }

    private static string LineAt(string text, int offset)
    {
        var start = text.LastIndexOf('\n', Math.Max(offset - 1, 0)) + 1;
        var end = text.IndexOf('\n', offset);
        return text[start..(end < 0 ? text.Length : end)].Trim();
    }
}
