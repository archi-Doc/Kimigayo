// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// R7-E1: Binding's problem records (docs/dev/DIAGNOSTICS.md 4.1): one problem per check key, a retried or abandoned attempt, and the facts the
// adapter reads. MinimalEmissionTest.Analyze asserts that no fact site of an analyzed program was id-less.
public sealed class BindingReportTest
{
    private const string Import = "group Native\n    #LibraryImport(\"kernel32\", \"VirtualFree\")\n    public unsafe func imported(a: bool) -> i32\n";

    private static readonly SourceRef Subject = new(0, 5);
    private static readonly SourceRef Other = new(0, 6);
    private static readonly DiagnosticRequirement Mismatch = DiagnosticRequirement.Binding(BindingFailure.TypeMismatch);
    private static readonly DiagnosticRequirement Located = DiagnosticRequirement.Binding(BindingFailure.Unsupported);

    [Fact]
    public void AKeyHasOneProblemAndKeepsItsFacts()
    {
        var report = new BindingReport();
        var first = report.Add(Problem(Subject, Mismatch), [new(ProblemFactKind.Span, Subject, 1)]);
        Assert.Equal(first, report.Add(Problem(Subject, Mismatch), [new(ProblemFactKind.Span, Subject, 2)]));
        Assert.Equal(1, report.Count);
        Assert.Equal(1, Assert.Single(report.Facts(first).ToArray()).Value);
        Assert.Equal(first, report.Find(Subject, Mismatch, 0));
        Assert.Equal(-1, report.Find(Subject, Mismatch, 1));
        Assert.Equal(-1, report.Find(Subject, Located, 0));
        Assert.Equal(-1, report.Find(Other, Mismatch, 0));
    }

    [Fact]
    public void ASubjectsProblemsAreInRecordingOrder()
    {
        var report = new BindingReport();
        int[] added =
        [
            report.Add(Problem(Subject, Mismatch)),
            report.Add(Problem(Other, Mismatch)),
            report.Add(Problem(Subject, Located)),
            report.Add(Problem(Subject, Mismatch, 1)),
        ];
        Assert.Equal([added[0], added[2], added[3]], Chain(report, Subject));
        Assert.Equal([added[1]], Chain(report, Other));
    }

    [Fact]
    public void KeptValuesAreTheFactsValues()
    {
        var report = new BindingReport();
        BoundType[] values = [BoundType.Boolean, BoundType.Unit];
        ProblemFact[] facts = [new(ProblemFactKind.Actual, Other, report.Keep(values[0])), new(ProblemFactKind.Expected, Other, report.Keep(values[1]))];
        var problem = report.Add(Problem(Subject, Mismatch), facts);
        Assert.Same(values[0], report.Value(report.Facts(problem)[0]));
        Assert.Same(values[1], report.Value(report.Facts(problem)[1]));
    }

    // A retried attempt: the subject's problems leave its chain and the key index, and the attempt records under the same key.
    [Fact]
    public void ARetriedAttemptRecordsItsOwnFacts()
    {
        var report = new BindingReport();
        var first = report.Add(Problem(Subject, Mismatch), [new(ProblemFactKind.Span, Subject, 1)]);
        report.Add(Problem(Other, Mismatch));
        Assert.Equal((first, first), report.Unlink(Subject));
        Assert.Equal(-1, report.First(Subject));
        Assert.Equal(-1, report.Find(Subject, Mismatch, 0));
        var second = report.Add(Problem(Subject, Mismatch), [new(ProblemFactKind.Span, Subject, 2)]);
        Assert.NotEqual(first, second);
        Assert.Equal(2, Assert.Single(report.Facts(second).ToArray()).Value);
        Assert.Equal(second, report.Find(Subject, Mismatch, 0));
        Assert.Equal([second], Chain(report, Subject));
    }

    // An abandoned attempt (PropertyTypeVisitor's snapshot): the chain comes back with its index, and what the attempt recorded goes.
    [Fact]
    public void AnAbandonedAttemptGivesTheSubjectItsChainBack()
    {
        var report = new BindingReport();
        int[] saved = [report.Add(Problem(Subject, Mismatch)), report.Add(Problem(Subject, Located))];
        var chain = report.Unlink(Subject);
        var attempt = report.Add(Problem(Subject, Mismatch, 1));
        report.Restore(Subject, chain);
        Assert.Equal(saved, Chain(report, Subject));
        Assert.Equal(saved[0], report.Find(Subject, Mismatch, 0));
        Assert.Equal(saved[1], report.Find(Subject, Located, 0));
        Assert.Equal(-1, report.Find(Subject, Mismatch, 1));
        Assert.NotEqual(attempt, report.Add(Problem(Subject, Mismatch, 1)));
    }

    // The attempt of a subject that had no problem leaves none, and no other subject's problem takes its place.
    [Fact]
    public void AnAbandonedAttemptOfACleanSubjectLeavesNone()
    {
        var report = new BindingReport();
        report.Add(Problem(Other, Mismatch));
        var chain = report.Unlink(Subject);
        Assert.Equal(BindingReport.NoChain, chain);
        report.Add(Problem(Subject, Mismatch));
        report.Restore(Subject, chain);
        Assert.Equal(-1, report.First(Subject));
        Assert.Equal(-1, report.Find(Subject, Mismatch, 0));
    }

    [Fact]
    public void ClearEmptiesTheReport()
    {
        var report = new BindingReport();
        report.Add(Problem(Subject, Mismatch), [new(ProblemFactKind.Actual, Other, report.Keep(BoundType.Boolean))]);
        report.IdlessSites = 1;
        report.Clear();
        Assert.Equal(0, report.Count);
        Assert.Equal(0, report.IdlessSites);
        Assert.Equal(-1, report.First(Subject));
        Assert.Equal(-1, report.Find(Subject, Mismatch, 0));
        Assert.Equal(0, report.Add(Problem(Subject, Mismatch)));
        Assert.Equal(0, report.Keep(BoundType.Boolean));
    }

    // The bridge: the slot's issue names its problem, and a failure no site records as a problem has none.
    [Theory]
    [InlineData("let x: i32 = true", "TypeMismatch_Kd", true)]
    [InlineData(Import, "Unsupported_Kd", true)]
    [InlineData("let x = missing", "UnresolvedBinding_Kd", false)]
    public void AnIssueNamesItsProblem(string source, string code, bool recorded)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var issue = Assert.Single(c.Binding.Issues, x => x.Code.ToString() == code);
        Assert.Equal(recorded, issue.Problem >= 0);
    }

    // CheckBound runs the pass's checks again without its reset, so the records keep the facts the pass recorded: the syntax a mismatch
    // shows and the part a located record names, with their text.
    [Theory]
    [InlineData("func f() -> i32\n    let x: i32 = true\n    return x\n", "TypeMismatch_Kd")]
    [InlineData(Import, "Unsupported_Kd")]
    public void CheckBoundKeepsAProblemsFacts(string source, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var first = Assert.Single(TestDiagnostics.Of(c), x => x.Code == code);
        Assert.False(c.Binding.CheckBound().IsComplete);
        c.Binding.ReportDiagnostics();
        var second = Assert.Single(TestDiagnostics.Of(c), x => x.Code == code);
        Assert.Equal((first.Span, first.Message, first.Text, first.Note), (second.Span, second.Message, second.Text, second.Note));
        Assert.True(Assert.Single(c.Binding.Issues, x => x.Code.ToString() == code).Problem >= 0);
    }

    // A failure cleared inside a pass takes its problem with it, so the node's next attempt records its own facts. No program in the suite
    // reaches this with a linked problem, so the row drives ClearFailure itself and reads the private report.
    [Fact]
    public void AClearedFailureUnlinksItsProblem()
    {
        var c = MinimalEmissionTest.Analyze("let x: i32 = true");
        var issue = Assert.Single(c.Binding.Issues, static x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        var report = (BindingReport)typeof(Binding).GetField("report", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c.Binding)!;
        var subject = SourceRef.Written(issue.Node);
        Assert.Equal(issue.Problem, report.Find(subject, Mismatch, 0));
        c.Binding.ClearFailure(issue.Node);
        Assert.Equal(-1, report.Find(subject, Mismatch, 0));
        Assert.Equal(-1, report.First(subject));
    }

    private static BindingProblem Problem(SourceRef subject, DiagnosticRequirement requirement, ushort condition = 0)
        => new(DiagnosticCode.TypeMismatch_Kd, requirement, subject, condition);

    private static int[] Chain(BindingReport report, SourceRef subject)
    {
        List<int> chain = [];
        for (var i = report.First(subject); i >= 0; i = report.Next(i))
        {
            chain.Add(i);
        }

        return [.. chain];
    }
}
