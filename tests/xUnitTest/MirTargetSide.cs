// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The MIR side of a test (DECISIONS R5-M188, refining R4-M136, R4-M50 and R4-M60): the Error records that
/// decide a program's verdict, each at its exact subject, so a changed MIR verdict fails the test. Its rows are Binding's records except
/// the twins of A5's rows (until R7), then the publisher's MIR rows (<see cref="MirSide"/>).</summary>
internal static class MirTargetSide
{
    /// <summary>Asserts an analyzed compilation's MIR side (<see cref="MirSide"/>).</summary>
    /// <param name="compilation">The compilation after final Binding and the startup check.</param>
    /// <param name="mir">The MIR side's Error records: a code and its subject's span.</param>
    internal static void Check(Compilation compilation, params (DiagnosticCode Code, SyntaxLocation At)[] mir)
    {
        var source = Assert.Single(compilation.SourceModules.SelectMany(static x => x.SourceDocuments)).SourceText;
        Assert.Equal(Rows(source, mir), Observed(MirSide(compilation)));
    }

    /// <summary>Asserts the MIR side of a source compiled as an Application, bound and analyzed (<see cref="MirSide"/>), for a test whose
    /// own compilation is out of reach (a native fixture's, the command line's).</summary>
    /// <param name="source">The source, as the Application's one document.</param>
    /// <param name="mir">The MIR side's Error records: a code and its subject's span.</param>
    internal static void Check(string source, params (DiagnosticCode Code, SyntaxLocation At)[] mir)
    {
        var c = CompilationTestHelper.ParseSuccess(source, "Hello.kimi");
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(Rows(source, mir), Observed(MirSide(c)));
    }

    /// <summary>Lists a compilation's MIR side (R6-check H10a D3, D4) after <see cref="Compilation.Analyze"/>, which it runs; Analyze
    /// rebuilds and analyzes the MIR and clears the ControlFlow and Ownership partitions on every call, and it publishes nothing
    /// (<see cref="Compilation.ReportAnalysis"/> does): Binding's issues and HIR lint Error records in their order, without a twin (D4: an Origin relation record of a
    /// node <see cref="Binding.IsTwinRecord"/> names, whose A5 row stands for it until R7), then <c>Compilation.Problems</c>' rows as the
    /// publisher has them, without a requirement: every merged problem of the MIR's report under its own code (construction and collection
    /// records, derived problems), then each validator finding as <c>InternalInvariant_Kd</c>. It never runs the old analysis.</summary>
    /// <param name="compilation">The compilation after final Binding and the startup check.</param>
    /// <returns>The rows with their requirement (Binding's; none for a MIR row), code and subject.</returns>
    internal static List<(DiagnosticRequirement? Requirement, DiagnosticCode Code, Koto Node)> MirSide(Compilation compilation)
    {
        compilation.Analyze();
        var rows = new List<(DiagnosticRequirement? Requirement, DiagnosticCode Code, Koto Node)>();
        foreach (var issue in compilation.Binding.Issues)
        {
            if (!(issue.Failure == BindingFailure.OriginRelation && issue.Code.IsUnprovenOrigin() && compilation.Binding.IsTwinRecord(issue.Node)))
            {
                rows.Add((DiagnosticRequirement.Binding(issue.Failure), issue.Code, issue.Node));
            }
        }

        foreach (var (node, code) in compilation.Binding.LintErrorRecords)
        {
            rows.Add((DiagnosticRequirement.Binding(BindingFailure.None), code, node));
        }

        rows.AddRange(compilation.Problems.Select(x => ((DiagnosticRequirement?)null, x.Code, compilation.SyntaxOf(x.Subject))));
        return rows;
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
    private static string[] Observed(List<(DiagnosticRequirement? Requirement, DiagnosticCode Code, Koto Node)> side)
        => [.. side.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error)
            .Select(static x => InProgram(x.Node) ? $"{x.Code} {x.Node.Span.Start}+{x.Node.Span.Length}" : $"{x.Code} {CompilationTestHelper.Location(x.Node)}")
            .Order(StringComparer.Ordinal)];

    private static bool InProgram(Koto node)
        => node.CodeContext?.SourceDocument?.Path is { } path && !path.StartsWith("compiler://", StringComparison.Ordinal);
}
