// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The MIR target side of a test that asserts an old rule (DECISIONS R4-M136, refining R4-M50 and R4-M60): the test keeps its
/// default-pipeline assertion under its <c>OldRule</c> trait and asserts here the Error records the MIR side has, each at its exact
/// subject, so a changed MIR verdict fails the test. A trait value is the revision item that replaced the old rule (R4-M50) or
/// <c>ledger:&lt;hash&gt;:&lt;line&gt;</c>, the ledger row of an old verdict no item names: the compilation's ledger hash
/// (<see cref="MirDifferenceLedger.Hash(Compilation)"/>) and the line of the record one side has and the other lacks, a key that no code names.
/// Both go at R6-check with the old path; the MIR side then is the test's assertion.</summary>
internal static class MirTargetSide
{
    private const string LedgerKey = "ledger:";

    /// <summary>Asserts an analyzed compilation's MIR side (<see cref="MirDifferenceLedger.Sides"/>: Binding's and the MIR's records); a
    /// ledger key of the running test whose hash is the compilation's names a line on which the two sides differ.</summary>
    /// <param name="compilation">The compilation after <see cref="Compilation.Analyze"/>.</param>
    /// <param name="mir">The MIR side's Error records: a code and its subject's span.</param>
    internal static void Check(Compilation compilation, params (DiagnosticCode Code, SyntaxLocation At)[] mir)
    {
        var (common, old, passed, records) = MirDifferenceLedger.Sides(compilation, compilation.BuildMir());
        var source = Assert.Single(compilation.SourceModules.SelectMany(static x => x.SourceDocuments)).SourceText;
        Assert.Equal(Rows(source, mir), Observed([.. common, .. records]));
        var hash = MirDifferenceLedger.Hash(compilation);
        foreach (var key in Keys().Where(x => x.StartsWith(LedgerKey + hash + ":", StringComparison.Ordinal)))
        {
            var line = int.Parse(key[(LedgerKey.Length + hash.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            var (before, after) = (Lines(source, [.. common, .. old, .. passed]), Lines(source, [.. common, .. records]));
            Assert.True(before.Contains(line) != after.Contains(line), $"{key}: no record of line {line} differs between the sides.");
        }
    }

    /// <summary>Asserts the MIR side of a source compiled as an Application without the old analysis, so no ledger observes it, for a test
    /// whose own compilation is out of reach (a native fixture's, the command line's); its markers name revision items only.</summary>
    /// <param name="source">The source, as the Application's one document.</param>
    /// <param name="mir">The MIR side's Error records: a code and its subject's span.</param>
    internal static void Check(string source, params (DiagnosticCode Code, SyntaxLocation At)[] mir)
    {
        Assert.DoesNotContain(Keys(), static x => x.StartsWith(LedgerKey, StringComparison.Ordinal));
        var c = CompilationTestHelper.ParseSuccess(source, "Hello.kimi");
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        var (common, _, _, records) = MirDifferenceLedger.Sides(c, c.BuildMir());
        Assert.Equal(Rows(source, mir), Observed([.. common, .. records]));
    }

    /// <summary>Gets the span of a subject within a context that occurs once in the source, the whole context without a subject.</summary>
    /// <param name="context">The unique context.</param>
    /// <param name="subject">The subject's text in it, its first occurrence there.</param>
    /// <returns>The location.</returns>
    internal static SyntaxLocation At(string context, string? subject = null)
    {
        var skip = subject is null ? 0 : context.IndexOf(subject, StringComparison.Ordinal);
        Assert.True(skip >= 0, $"'{subject}' is not in '{context}'.");
        return new(context, Skip: skip, Length: subject?.Length);
    }

    // The running test's OldRule values; the helper serves only a test that has one.
    private static string[] Keys()
    {
        var keys = TestContext.Current.TestCase?.Traits.GetValueOrDefault("OldRule")?.ToArray() ?? [];
        Assert.NotEmpty(keys);
        return keys;
    }

    // The expected records as code, offset and length.
    private static string[] Rows(string source, (DiagnosticCode Code, SyntaxLocation At)[] mir)
        => [.. mir.Select(x => (x.Code, Span: x.At.Resolve(source))).Select(static x => $"{x.Code} {x.Span.Start}+{x.Span.Length}")
            .Order(StringComparer.Ordinal)];

    // A side's Error records as code, offset and length; a record outside the program's document as code and location.
    private static string[] Observed(IEnumerable<MirDifferenceLedger.Entry> side)
        => [.. Errors(side).Select(static x => InProgram(x)
                ? $"{x.Code} {x.Node.Span.Start}+{x.Node.Span.Length}" : $"{x.Code} {LegacyRecords.Location(x.Node)}")
            .Order(StringComparer.Ordinal)];

    // The 1-based lines of a side's Error records in the program's document.
    private static HashSet<int> Lines(string source, IEnumerable<MirDifferenceLedger.Entry> side)
        => [.. Errors(side).Where(InProgram).Select(x => source.AsSpan(0, x.Node.Span.Start).Count('\n') + 1)];

    private static IEnumerable<MirDifferenceLedger.Entry> Errors(IEnumerable<MirDifferenceLedger.Entry> side)
        => side.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error);

    private static bool InProgram(MirDifferenceLedger.Entry entry)
        => entry.Node.CodeContext?.SourceDocument?.Path is { } path && !path.StartsWith("compiler://", StringComparison.Ordinal);
}
