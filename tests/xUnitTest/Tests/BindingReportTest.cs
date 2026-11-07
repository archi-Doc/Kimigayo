// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
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
    private static readonly DiagnosticRequirement Internal = DiagnosticRequirement.Binding(BindingFailure.Internal);
    private static readonly DiagnosticRequirement Unproven = DiagnosticRequirement.Binding(BindingFailure.UnprovenConstraint);

    [Fact]
    public void AKeyHasOneProblemAndKeepsItsFacts()
    {
        var report = new BindingReport();
        var first = report.Add(Problem(Subject, Mismatch), [new(ProblemFactKind.Span, Subject, 1)]);
        Assert.Equal(first, report.Add(Problem(Subject, Mismatch), [new(ProblemFactKind.Span, Subject, 1)]));
        Assert.Equal(1, report.Count);
        Assert.Equal(1, Assert.Single(report.Facts(first).ToArray()).Value);
        Assert.Equal(first, report.Find(Subject, Mismatch, 0));
        Assert.Equal(-1, report.Find(Subject, Mismatch, 1));
        Assert.Equal(-1, report.Find(Subject, Located, 0));
        Assert.Equal(-1, report.Find(Other, Mismatch, 0));
    }

    // DIAGNOSTICS 4.3: for one key the code and facts are equal, a Type-valued fact by its Type and not its kept index; anything else is a
    // broken contract, and the problem keeps its first report.
    [Theory]
    [InlineData(DiagnosticCode.Unsupported_Kd, (byte)ProblemFactKind.Actual, 0, 1)]
    [InlineData(DiagnosticCode.TypeMismatch_Kd, (byte)ProblemFactKind.Expected, 0, 1)]
    [InlineData(DiagnosticCode.TypeMismatch_Kd, (byte)ProblemFactKind.Actual, 1, 1)]
    [InlineData(DiagnosticCode.TypeMismatch_Kd, (byte)ProblemFactKind.Actual, 0, 0)]
    [InlineData(DiagnosticCode.TypeMismatch_Kd, (byte)ProblemFactKind.Actual, 0, 2)]
    public void ADifferingReportOfAKeyIsABrokenContract(DiagnosticCode code, byte kind, int type, int count)
    {
        var report = new BindingReport();
        BoundType[] types = [BoundType.Boolean, BoundType.Unit];
        var fact = new ProblemFact(ProblemFactKind.Actual, Other, report.Keep(types[0]));
        var first = report.Add(Problem(Subject, Mismatch), [fact]);
        Assert.Equal(first, report.Add(Problem(Subject, Mismatch), [fact with { Value = report.Keep(types[0]) }]));
        Assert.Equal(1, report.Count);

        var differing = Enumerable.Repeat(new ProblemFact((ProblemFactKind)kind, Other, report.Keep(types[type])), count).ToArray();
        Assert.Equal(-1, report.Add(Problem(Subject, Mismatch) with { Code = code }, differing));
        Assert.Equal([first, report.Find(Subject, Internal, 0)], Chain(report, Subject));
        Assert.Equal(DiagnosticCode.InternalInvariant_Kd, report.Problem(report.Find(Subject, Internal, 0)).Code);
        Assert.Same(types[0], report.Value(Assert.Single(report.Facts(first).ToArray())));
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

    // A side that is no Type is a row with, packed above it, a count: the record says what the check found, by the adapter's text alone.
    [Theory]
    [InlineData("func f()\n    let g: () -> i32 = func (a: i32) -> i32 => 1\n", "found an anonymous function with 1 parameter")]
    [InlineData("func f()\n    let g: () -> i32 = func (a: i32, b: i32) -> i32 => 1\n", "found an anonymous function with 2 parameters")]
    [InlineData("func f()\n    let g: (i32) -> i32 = func () -> i32 => 1\n", "found an anonymous function with 0 parameters")]
    [InlineData("func f()\n    let x: i32 = func () => 1\n", "found an anonymous function")]
    [InlineData("func f()\n    let t: (i32, bool) = 4\n", "found integer literal")]
    [InlineData("func f()\n    let t: (f64, f64) = 2.5\n", "found floating-point literal")]
    public void AFormIsItsRowAndCount(string source, string found)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        Assert.EndsWith(found, record.Label);
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

    // An abandoned attempt gives each node of its syntax the chain it saved (PropertyTypeVisitor). A role's record at a Type node comes at the
    // final judgment, after every attempt, so no program reaches this; the row drives the visitor on such a node.
    [Fact]
    public void AnAbandonedPropertyAttemptRestoresItsNodesProblems()
    {
        var c = MinimalEmissionTest.Analyze("struct Holder<s/T, U>\n    let value: s/T\n    let other: s/U\n");
        var issue = Assert.Single(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        var report = (BindingReport)typeof(Binding).GetField("report", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c.Binding)!;
        var subject = SourceRef.Written(issue.Node);
        var chain = Chain(report, subject);
        Assert.Contains(issue.Problem, chain);

        var visitorType = typeof(Binding).GetNestedType("PropertyTypeVisitor", BindingFlags.NonPublic)!;
        var visitor = (KotoVisitor)Activator.CreateInstance(visitorType, c.Binding)!;
        visitor.Visit(issue.Node);
        Assert.Empty(Chain(report, subject));
        visitorType.GetMethod("Restore", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(visitor, [0]);
        Assert.Equal(chain, Chain(report, subject));
        Assert.Equal(issue.Problem, report.Find(subject, Unproven, 0));
    }

    // A node's records beyond its first are issues of their own, each by its code and condition, in a report that keeps them through CheckBound.
    [Fact]
    public void AFurtherRecordIsAnIssueOfItsOwn()
    {
        var c = MinimalEmissionTest.Analyze("func f(b: bool) -> ()\n    b++\npublic func main() => ()");
        var issues = c.Binding.Issues.Where(static x => x.Node.ToString() == "b++").ToArray();
        Assert.Equal([DiagnosticCode.InvalidAssignment_Kd, DiagnosticCode.NonNumericOperand_Kd], issues.Select(static x => x.Code));
        Assert.Equal(-1, issues[0].Problem);
        Assert.True(issues[1].Problem >= 0);
        Assert.False(c.Binding.CheckBound().IsComplete);
        Assert.Equal(issues.Select(static x => x.Code), c.Binding.Issues.Where(static x => x.Node.ToString() == "b++").Select(static x => x.Code));
    }

    // n parts at one node: a report takes one step (the key index, not the chain, DIAGNOSTICS 4.3), which a walk of the chain per report would
    // not finish in the bound. Conditions stop at ushort, so the parts spread over requirements.
    [Fact]
    public void ManyPartsAtOneNodeAreOneStepEach()
    {
        const int Parts = 400_000;
        var report = new BindingReport();
        var time = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < Parts; i++)
        {
            report.Add(Problem(Subject, new(DiagnosticPartition.Binding, (ushort)(i >> 16)), (ushort)i));
        }

        var chain = report.Unlink(Subject);
        report.Restore(Subject, chain);
        Assert.Equal(Parts, report.Count);
        Assert.InRange(time.ElapsedMilliseconds, 0, 30_000);
    }

    // The same, through the whole cold path: an import with n Types without a C counterpart has n records, at bytes linear in n.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AnImportsPartsTakeLinearBytes()
    {
        int[] parts = [128, 256, 512];
        var compilations = new Compilation[parts.Length];
        var bytes = AllocationMeasurement.MeasureSeries(parts.Length, i =>
        {
            var c = compilations[i] = CompilationTestHelper.Parse(ManyParts(parts[i]));
            return () => c.Bind();
        });
        Assert.True(bytes[0] < bytes[1] && MirBorrowsTest.Grows(bytes, 3), $"bytes {string.Join(", ", bytes)}");
        for (var i = 0; i < parts.Length; i++)
        {
            Assert.Equal(parts[i], compilations[i].Binding.Issues.Count(static x => x.Code == DiagnosticCode.InvalidImportSignature_Kd));
        }
    }

    private static string ManyParts(int count)
        => "group Native\n    #LibraryImport(\"kernel32\", \"VirtualFree\")\n    public unsafe func imported(" +
            string.Join(", ", Enumerable.Range(0, count).Select(static i => $"p{i}: string")) + ") -> i32\n";

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
