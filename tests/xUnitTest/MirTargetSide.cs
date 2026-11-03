// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The MIR side of a test whose old rule went at R6-check H5 (DECISIONS R4-M136, refining R4-M50 and R4-M60): the Error records
/// the MIR side has, each at its exact subject, so a changed MIR verdict fails the test. It stays until H10a, with the difference
/// ledger.</summary>
internal static class MirTargetSide
{
    /// <summary>Asserts an analyzed compilation's MIR side (<see cref="MirDifferenceLedger.Sides"/>: Binding's and the MIR's records).</summary>
    /// <param name="compilation">The compilation after <see cref="Compilation.Analyze"/>.</param>
    /// <param name="mir">The MIR side's Error records: a code and its subject's span.</param>
    internal static void Check(Compilation compilation, params (DiagnosticCode Code, SyntaxLocation At)[] mir)
    {
        var (common, _, _, records) = MirDifferenceLedger.Sides(compilation, compilation.BuildMir());
        var source = Assert.Single(compilation.SourceModules.SelectMany(static x => x.SourceDocuments)).SourceText;
        Assert.Equal(Rows(source, mir), Observed([.. common, .. records]));
    }

    /// <summary>Asserts the MIR side of a source compiled as an Application without the old analysis, for a test whose own compilation is
    /// out of reach (a native fixture's, the command line's).</summary>
    /// <param name="source">The source, as the Application's one document.</param>
    /// <param name="mir">The MIR side's Error records: a code and its subject's span.</param>
    internal static void Check(string source, params (DiagnosticCode Code, SyntaxLocation At)[] mir)
    {
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

    // The expected records as code, offset and length.
    private static string[] Rows(string source, (DiagnosticCode Code, SyntaxLocation At)[] mir)
        => [.. mir.Select(x => (x.Code, Span: x.At.Resolve(source))).Select(static x => $"{x.Code} {x.Span.Start}+{x.Span.Length}")
            .Order(StringComparer.Ordinal)];

    // A side's Error records as code, offset and length; a record outside the program's document as code and location.
    private static string[] Observed(IEnumerable<MirDifferenceLedger.Entry> side)
        => [.. Errors(side).Select(static x => InProgram(x)
                ? $"{x.Code} {x.Node.Span.Start}+{x.Node.Span.Length}" : $"{x.Code} {LegacyRecords.Location(x.Node)}")
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<MirDifferenceLedger.Entry> Errors(IEnumerable<MirDifferenceLedger.Entry> side)
        => side.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error);

    private static bool InProgram(MirDifferenceLedger.Entry entry)
        => entry.Node.CodeContext?.SourceDocument?.Path is { } path && !path.StartsWith("compiler://", StringComparison.Ordinal);
}
