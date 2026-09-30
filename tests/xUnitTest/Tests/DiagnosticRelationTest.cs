// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>Input relations of the diagnostic evaluation (docs/dev/DIAGNOSTICS.md §9.3) over the mutation corpus.</summary>
public class DiagnosticRelationTest
{
    public static TheoryData<string> MutationNames => [.. DiagnosticCorpus.Mutations.Select(static x => x.Name)];

    // A blank line outside strings and documentation comments changes positions only.
    [Theory]
    [MemberData(nameof(MutationNames))]
    public void ABlankLineChangesPositionsOnly(string name)
    {
        var source = DiagnosticCorpus.Apply(DiagnosticCorpus.Mutation(name));
        var before = Publish(source);
        var after = Publish("\n" + source);
        Assert.NotEmpty(before);
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i] with { Span = new(before[i].Span.Start + 1, before[i].Span.Length) }, after[i]);
        }
    }

    // For the same check, input and prerequisites, an unrelated problem leaves the existing records and explanations
    // unchanged. Ownership analysis requires complete Binding, so a base with ownership records gets an unrelated
    // ownership problem, which keeps that prerequisite.
    [Theory]
    [MemberData(nameof(MutationNames))]
    public void AnUnrelatedProblemLeavesExistingRecordsUnchanged(string name)
    {
        var source = DiagnosticCorpus.Apply(DiagnosticCorpus.Mutation(name));
        var before = Publish(source, out var ownership);
        var (unrelated, code) = ownership
            ? ("\nfunc unrelated()\n    let text = \"a\"\n    let first = text@move\n    let second = text@move\n", nameof(Kimi.DiagnosticCode.MovedPlace_Kd))
            : ("\nfunc unrelated() -> i32\n    return true\n", nameof(Kimi.DiagnosticCode.TypeMismatch_Kd));
        var after = Publish(source + unrelated, out _);
        Assert.All(before, record => Assert.Contains(record, after));
        Assert.Equal(code, Assert.Single(after, record => !before.Contains(record)).Code);
    }

    // Publishes every front-end phase as a check does.
    private static TestDiagnostic[] Publish(string source)
        => Publish(source, out _);

    private static TestDiagnostic[] Publish(string source, out bool ownership)
    {
        var c = MinimalEmissionTest.Analyze(source);
        ownership = c.Ownership.Issues.Count != 0;
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.Ownership.ControlFlow!.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        return TestDiagnostics.Of(c, "Hello.kimi");
    }
}
