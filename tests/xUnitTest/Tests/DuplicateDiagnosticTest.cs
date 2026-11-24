// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class DuplicateDiagnosticTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("let x = 1\nlet x = 2\nlet x = 3", "x", "x")]
    [InlineData("func f(x: i32) -> i32 => x\nfunc f(y: i32) -> i32 => y\nfunc f(z: i32) -> i32 => z", "f", "f(y: i32) -> i32")]
    public void LaterDeclarationsReferToTheFirst(string source, string name, string secondText)
    {
        var c = Analyze(source);
        var result = c.Diagnostics.Finalize(rejected: true);
        var errors = result.Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error =>
        {
            Assert.Equal(nameof(DiagnosticCode.DuplicateBinding_Kd), error.Code);
            Assert.Equal(DiagnosticCategory.Language, error.Category);
            Assert.Equal(name, Assert.Single(error.Reason!).Value);
            var first = Assert.Single(error.Related!);
            Assert.Equal("declaration", first.Role);
            Assert.Equal(0, first.Range!.Value.Start.Line);
        });
        Assert.Equal([1, 2], errors.Select(static x => x.Display!.Range!.Value.Start.Line));
        Assert.Equal(secondText, source.Substring(errors[0].Span!.Value.Start, errors[0].Span!.Value.Length));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    // SPEC 6.1, 9.1, 23.1.2 (R7-E2 E2-3b): a group joined by two pairwise checks (Signatures' and the scope's) is recorded once, after the
    // last: each later declaration's record (the first derived), as dev; a repeated loop binding is its own record (DiagnosticPrecisionTest).
    [Fact]
    public void AGroupJoinedByTwoChecksIsRecordedOnce()
        => Assert.Equal(
            "1*DeclarationOrderWarning_Kd 2*DuplicateBinding_Kd",
            DiagnosticCorpus.RecordProfile("group G\n    func f() => ()\n    func f() => ()\n    let f: i32 = 1\npublic func main() => ()\n", "Application"));

    // SPEC 9.1, 9.3, 7.3, 23.1.2 (R7-E2 E2-4's B1; the Front's r1, r1b, r1t): a constructor whose written `self` redeclares the contextual
    // receiver and whose Signature `(i32)` another constructor repeats is invalid, never a compiler defect. Since R7-E2 E2-4b's O-d (b) (B's
    // ruling 2, superseding E2-4's R2.1 B1 (a)) the receiver's problem and the Signature group's each take their own key (RecordNumbered),
    // so every independent failure is reported (M1: r1 one more record, r1b two) and no InternalInvariant_Kd appears. Distinct Signatures
    // without a written `self` are valid.
    [Theory]
    [InlineData("init(self: i32) => ()\n    init(x: i32) => ()", "2*DuplicateBinding_Kd 1*InvalidTypeFormation_Kd")] // r1
    [InlineData("init(self: i32) => ()\n    init(self: i32) => ()", "3*DuplicateBinding_Kd 2*InvalidTypeFormation_Kd")] // r1b
    [InlineData("init(x: i32) => ()\n    init(y: i64) => ()", "clean")] // r1t
    public void AWrittenSelfInADuplicateConstructorKeepsTheGroupsRecord(string constructors, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile("struct S\n    " + constructors + "\npublic func main() => ()\n", "Application"));

    // SPEC 6.1.2, 9.1, 18.4.1, 23.1.2 (R7-E2 E2-4b's gate, ruling 2): a declaration whose further Duplicate problem is recorded before the
    // group step keeps it beside the group's, each at its own key, never a compiler defect: a Semantics name repeating a Type parameter
    // beside a repeated target (d3c: M1, the Semantics name's record was lost), two Contracts named as an inherited Type member (d1c2) and
    // two root Contracts named `Kimi` (d2c2). The Contract pair's later has no record of its own on dev and here alike (an open issue,
    // not the group step's). Each record is its code and its related locations' labels, in result order.
    [Theory]
    [InlineData("func f<T, T/U, U>() -> () => ()\n", "DuplicateBinding_Kd|DuplicateBinding_Kd, first declaration")] // d3c
    [InlineData("func f<T, U>() -> () => ()\n", "")] // d3t
    [InlineData("open struct B\n    public struct N\n    public init() => ()\nstruct D: B\n    contract N\n    contract N\n    public init(): base() => ()\n", "DuplicateBinding_Kd|DuplicateBinding_Kd, first declaration")] // d1c2
    [InlineData("contract Kimi\ncontract Kimi\n", "DuplicateBinding_Kd")] // d2c2
    public void AFurtherDuplicateProblemKeepsItsOwnKey(string source, string records)
        => Assert.Equal(records, string.Join('|', DiagnosticCorpus.Check(source + "public func main() => ()\n").Diagnostics.Select(static x => x.Code + string.Concat(x.Related?.Select(static r => ", " + r.Label) ?? []))));

    // SPEC 6.1, 9.1, 23.1.2 (R7-E2 E2-3b, the Front's d4): the repeated Name is judged from the two declarations alone, so the later's
    // initializer's syntax error, which its own binding consulted, does not derive it (IsDerived's JudgedByDeclaredNames row): both are
    // reported, as dev's group walk did; the twin with another name reports the syntax error alone.
    [Theory]
    [InlineData("x", "1*DuplicateBinding_Kd 1*ExpectedSyntax_Kd")]
    [InlineData("y", "1*ExpectedSyntax_Kd")]
    public void ARepeatedNameIsReportedBesideItsInitializersSyntaxError(string name, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile("group G\n    let x: i32 = 1\n    let " + name + ": i32 = (1 +)\npublic func main() => ()\n", "Application"));

    // SPEC 9.1, F.3.3, 23.1.2 (R7-E2 E2-3b, the Front's k2): the repeated Signature is judged from the two Signatures alone (`unsafe` never
    // distinguishes them), so the later header's own recovery (a second modifier) does not derive it: both are reported, as dev's group
    // walk did; the twin without the second modifier reports the repeat alone.
    [Theory]
    [InlineData("unsafe virtual", "1*DuplicateBinding_Kd 1*MisplacedSyntax_Kd")]
    [InlineData("unsafe", "1*DuplicateBinding_Kd")]
    public void ARepeatedSignatureIsReportedBesideItsHeadersRecovery(string modifiers, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile("group G\n    func f() => ()\n    " + modifiers + " func f() => ()\npublic func main() => ()\n", "Application"));

    // SPEC 6.1, 9.3, 23.1.2 (R7-E2 E2-3b, the Front's k4): a Name bound twice by one loop is judged from the names alone, so the loop's
    // recovery (a nested slot list) does not derive it: both are reported; the twin with another name reports the syntax error alone.
    [Theory]
    [InlineData("a", "1*DuplicateBinding_Kd 1*ExpectedSyntax_Kd")]
    [InlineData("b", "1*ExpectedSyntax_Kd")]
    public void ARepeatedLoopNameIsReportedBesideItsLoopsRecovery(string name, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile("public func main()\n    for ((p, q), a, " + name + ") in [((1, 2), 3, 4)]\n        ()\n", "Application"));

    [Fact]
    public void TheFirstDeclarationFollowsConsumedSourceOrder()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("z-first.kimi", "group G\n    public func x() => ()"));
        c.Kotonoha.AddSource(new SourceDocument("a-second.kimi", "group G\n    public func x() => ()"));
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(["z-first.kimi", "a-second.kimi"], result.Sources.Select(static x => x.Path));
        Assert.Equal(1, error.Source);
        Assert.Equal(new SourceSpan(24, 3), error.Span);
        Assert.Equal(0, Assert.Single(error.Related!).Source);
    }

    [Theory]
    [InlineData("let x = 1\nlet y = 2\nlet z = 3")]
    [InlineData("func f(x: i32) -> i32 => x\nfunc f(x: bool) -> bool => x")]
    public void DistinctDeclarationsRemainValid(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Empty(c.Diagnostics.Finalize().Diagnostics);
    }

    [Fact]
    public void IndependentErrorsAndBlankLinesPreserveDuplicateEvidence()
    {
        const string Duplicates = "let x = 1\nlet x = 2";
        foreach (var suffix in new[] { string.Empty, "\nmissing()" })
        {
            var c = Analyze("\n" + Duplicates + suffix);
            var result = c.Diagnostics.Finalize(rejected: true);
            var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd));
            Assert.Equal(2, error.Display!.Range!.Value.Start.Line);
            Assert.Equal(1, Assert.Single(error.Related!).Range!.Value.Start.Line);
            Assert.DoesNotContain(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
            if (suffix.Length != 0)
            {
                Assert.Contains(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.UnresolvedBinding_Kd));
            }
        }
    }

    [Theory]
    [InlineData("alias A => Kimi\nalias A => Kimi.Console\nalias A => Kimi.Text", "A", 0)]
    [InlineData("contract C\nstruct S<T>\n    Self is C when T is Copy\n    Self is C when T is Owned\n    Self is C when T is Copy", "C", 2)]
    [InlineData("func f<T>(value: ref/T) -> i32 => 1\nspecialize func f<i32>(value: ref/i32) -> i32 => 2\nspecialize func f<i32>(value: ref/i32) -> i32 => 3\nspecialize func f<i32>(value: ref/i32) -> i32 => 4", "f", 1)]
    [InlineData("alias A => Kimi.Console\nalias A => ::Kimi.Console\nalias A => Kimi.Text\nalias A => Kimi", "A", 0)]
    public void PairwiseRecordersShareTheSameNormalization(string source, string name, int firstLine)
    {
        var c = Analyze(source);
        var result = c.Diagnostics.Finalize(rejected: true);
        var duplicates = result.Diagnostics.Where(static x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd)).ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.All(duplicates, error =>
        {
            Assert.Equal(name, Assert.Single(error.Reason!).Value);
            Assert.Equal(firstLine, Assert.Single(error.Related!).Range!.Value.Start.Line);
        });
        Assert.DoesNotContain(result.Diagnostics, static x => x.Code is nameof(DiagnosticCode.CheckFaulted_Kd) or nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    [Fact]
    public void RootRuntimeDeclarationsRemainSourceLocal()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        foreach (var path in new[] { "first.kimi", "second.kimi" })
        {
            c.Kotonoha.AddSource(new SourceDocument(path, "let x = 1\nfunc f() => ()"));
        }

        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Empty(c.Diagnostics.Finalize().Diagnostics);
    }

    [Fact]
    public void CliAndLspIdentifyTheLaterAndFirstDeclarations()
    {
        var path = Path.GetFullPath("DuplicateDiagnostic.kimi");
        var c = MinimalEmissionTest.Analyze("let x = 1\nlet x = 2", path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("DuplicateDiagnostic.kimi:2:5", console.Text, StringComparison.Ordinal);
        Assert.Contains("DuplicateDiagnostic.kimi:1:5", console.Text, StringComparison.Ordinal);
        Assert.Contains("'x' is declared again", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("'x' is declared again", sent.Message, StringComparison.Ordinal);
            if (capability)
            {
                Assert.Equal(new SourceRange(new(0, 4), new(0, 5)), Assert.Single(sent.RelatedInformation!).Location.Range);
            }
            else
            {
                Assert.Contains("DuplicateDiagnostic.kimi:1:5: first declaration", sent.Message, StringComparison.Ordinal);
            }

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    // SPEC 9.1 (R7-V2e): a member group's duplicate check hashes each function once and compares only functions of one SignatureHash, so
    // its work is linear in n overloads whose parameter Types differ in their top layer (plus one repeated pair), where the pairwise scan
    // compared n (n + 1) / 2 pairs; the repeat is still the one record, at the last `pick`, related to the first.
    [Fact]
    public void DuplicateSignatureChecksAreLinearInTheGroup()
    {
        var work = new[] { 64, 128, 256 }.Select(static n => Work(n)).ToArray();
        Assert.True(work[0] > 0 && work[2] - work[1] <= (2 * (work[1] - work[0])) + 16, string.Join(' ', work));

        static int Work(int overloads)
        {
            var types = string.Concat(Enumerable.Range(0, overloads).Select(static i => $"struct S{i}\n"));
            var group = string.Concat(Enumerable.Range(0, overloads).Select(static i => $"func pick(value: S{i}) -> i32 => {i}\n"));
            var c = CompilationTestHelper.ParseSuccess(types + group + "func pick(other: S0) -> i32 => 0\n");
            c.Bind();
            var count = c.Binding.DuplicateCheckWork;
            c.Binding.ReportDiagnostics();
            var error = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
            Assert.Equal(nameof(DiagnosticCode.DuplicateBinding_Kd), error.Code);
            Assert.Equal(2 * overloads, error.Display!.Range!.Value.Start.Line);
            Assert.Equal(overloads, Assert.Single(error.Related!).Range!.Value.Start.Line);
            return count;
        }
    }

    // SPEC 9.1 (R7-V2e): a function is compared with every later function of its SignatureHash, past an unequal one too. The four `d`
    // differ only at their parameter Type's third layer, so all share one hash; d1 and d3, d2 and d4 have one Signature each (the
    // review's q4). A slot inside a direct part hashes by its slot number under each binder, so `Array<T>` and `Array<U>` of two
    // one-parameter generics are one Signature (q7).
    [Theory]
    [InlineData("func d(a: Array<Array<Array<i32>>>) => ()\nfunc d(a: Array<Array<Array<i64>>>) => ()\nfunc d(b: Array<Array<Array<i32>>>) => ()\nfunc d(b: Array<Array<Array<i64>>>) => ()\n", "2*DuplicateBinding_Kd")] // q4
    [InlineData("func g<T>(a: Array<T>) => ()\nfunc g<U>(b: Array<U>) => ()\n", "1*DuplicateBinding_Kd")] // q7
    public void EveryLaterFunctionOfOneSignatureHashIsCompared(string functions, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(functions + "public func main() => ()\n", "Application"));

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public sealed class AllocationTests
    {
        [Fact]
        public void WarmDuplicateChecksReuseTheirGroups()
        {
            var c = CompilationTestHelper.ParseSuccess("func f(x: i32) -> i32 => x\nfunc f(y: i32) -> i32 => y\nfunc f(z: i32) -> i32 => z");
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        }
    }

    private static Compilation Analyze(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        return c;
    }
}
