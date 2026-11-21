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
    // receiver and whose Signature `(i32)` another constructor repeats is invalid, never a compiler defect. The receiver's Duplicate keeps
    // the slot's failure (Structs' IndexSpecialReceiver), so the Signature group's record, made after Index under the same key with its
    // own facts, is the constructor's one record and no InternalInvariant_Kd appears (a fact-less record of its own there was one: the
    // E2-4 prototype's round 1). Distinct Signatures without a written `self` are valid.
    [Theory]
    [InlineData("init(self: i32) => ()\n    init(x: i32) => ()", "1*DuplicateBinding_Kd 1*InvalidTypeFormation_Kd")] // r1
    [InlineData("init(self: i32) => ()\n    init(self: i32) => ()", "1*DuplicateBinding_Kd 2*InvalidTypeFormation_Kd")] // r1b
    [InlineData("init(x: i32) => ()\n    init(y: i64) => ()", "clean")] // r1t
    public void AWrittenSelfInADuplicateConstructorKeepsTheGroupsRecord(string constructors, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile("struct S\n    " + constructors + "\npublic func main() => ()\n", "Application"));

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
