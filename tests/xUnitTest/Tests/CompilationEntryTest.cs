// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Build;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The <c>Compilation</c> entry points (plan 12.1, 13.2): <c>Analyze</c> publishes the MIR's construction records, validator
/// findings and checked set's records as its <c>Problems</c> at check (DECISIONS R5-M156), and <c>ReportAnalysis</c> reports them; the MIR
/// decides acceptance (DECISIONS R5-M185). MIR code generation, the one code generator (DECISIONS R5-M160), writes the module of an
/// accepted program (R5 C0b1, X2). Every product generation request passes the generation entry's gate (R5 C0a): an analysis with MIR
/// records generates nothing, and <c>kimi test</c> publishes its records as <c>kimi build</c> does.</summary>
public sealed class CompilationEntryTest
{
    // The MIR's one Unsupported record here, at the selection `Call`: a selection through an identity premise (MirBuildTest.Unbuilt;
    // PLAN G168b).
    private const string Call = "s.0";

    private static readonly string Unbuilt = MirBuildTest.Unbuilt.Declarations +
        $"func same<T>(s: ref/T) -> i32\n    {MirBuildTest.Unbuilt.Premise}\n    return {Call}.a\nlet x = 1\n";

    // R5 K1, C0b1 (DECISIONS R5-M65, R5-M146): the checked set exists after check; MIR code generation writes the module of each output
    // form, an Application's (with its entry), a test build's (the test runtime's entry; C10a, DECISIONS R5-M136) or a Library's, never
    // the old generator's module.
    // R5 K11 (DECISIONS R5-M77): it generates the closure of the output roots, an Application's startup body, a test build's discovered
    // tests (not its top-level statements and their instance) and a Library's every root.
    [Theory]
    [InlineData(OutputKind.Application, false, new[] { "first", "top" })]
    [InlineData(OutputKind.Application, true, new[] { "first", "second" })]
    [InlineData(OutputKind.Library, false, new[] { "first", "second", "top" })]
    public void MirCodegenWritesEachOutputForm(OutputKind output, bool test, string[] generated)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = test;
        c.Project.ProjectFile.OutputKind = output;
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        var functions = test ? "#Test\nfunc first() => ()\n#Test\nfunc second() => ()\n" : "func first() => ()\nfunc second() => ()\n";
        var statements = output == OutputKind.Library ? string.Empty : test ? "let x = pick(1)\n" : "first()\n";
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", functions + "func pick<T>(value: T) -> T => value@move\n" + statements));
        Assert.True(c.Check(), MinimalEmissionTest.Describe(c)); // A test build's check discovers its tests (Compilation.Check).

        var set = Assert.IsType<CheckedSet>(c.Instances);
        using (var writer = new StringWriter())
        {
            Assert.True(c.Generate(writer));
            Assert.NotEqual(0, writer.ToString().Length);
            Assert.Equal(output == OutputKind.Application, writer.ToString().Contains("define void @__kimi_start()", StringComparison.Ordinal));
        }

        var items = set.Items.ToArray();
        Assert.Equal(generated, Enumerable.Range(0, items.Length).Where(set.IsGenerated).Select(x => items[x].Key.Body.Def.Decl?.Name ?? "top").Order());

        Assert.True(c.Generate(TextWriter.Null));
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
    }

    // R5 C0a (PD12's gate, step 1; DECISIONS R5-M64): every generation request passes the entry's gate, artifact publication included;
    // the records are the analysis's own, so generation adds none.
    [Fact]
    public void TheGateRefusesMirRecordsForEveryCaller()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-compilation-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Unbuilt));
            Assert.False(Analyze(c, OutputKind.Application));
            Assert.Equal(DiagnosticCode.Unsupported_Kd, Assert.Single(c.Problems).Code);
            c.Project.Directory = directory;
            c.Project.Name = "Hello";
            c.Project.ProjectFile.OutputPath = "out/Hello.ll";
            using var writer = new StringWriter();
            Assert.False(c.Generate(writer));
            Assert.Empty(writer.ToString());
            Assert.False(EmissionArtifacts.Publish(c, null));
            Assert.Empty(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
            Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // R5 C0a: kimi test publishes its records as kimi build does: a test build whose instance the backend does not generate reports the
    // located Unsupported_Kd at check (R5-M54; DECISIONS R5-M156), not failure text, and the command generates only a build its check
    // prepared.
    [Fact]
    public void TheTestCommandPublishesTheInstancesRecordAtCheck()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-compilation-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "wide", Directory = directory };
            project.ProjectFile.Targets = [TargetProfile.Windows.Target];
            const string Half = "func half<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value / 2\n";
            project.AddSource("main.kimi", Half + "#Test\nfunc wide()\n    let result = half(10@i128)\n");
            Assert.Null(project.PrepareTests(TestContext.Current.CancellationToken));
            Assert.Contains("Unsupported_Kd\n --> main.kimi:3:12", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("InternalInvariant_Kd", console.Text, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.ll", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // R5 K4b (DECISIONS R5-M156, superseding R5-M41's gate): the checked set's records are analysis problems, control-flow Errors at their
    // located subjects that reject the program: a chain of distinct functions whose 19th call forms a Type past IMPL A.4's height, the
    // collector's TypeHeightLimit_Kd at that call (R7-T1). Before R7-T1 the row read pick<Option^32<ref/i32>>'s storage-nesting gap; that
    // program is valid (SPEC 8.13.3, 8.15.3) and now an instance (InstanceCollectorTest.AFiniteTypePastBindingsBoundIsAnInstance, B's
    // ruling 14).
    [Fact]
    public void CheckPublishesTheCollectorRecords()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", InstanceCollectorTest.DeepSource("chain20")));
        Assert.False(Analyze(c, OutputKind.Application));
        var problem = Assert.Single(c.Problems);
        Assert.Equal((DiagnosticCode.TypeHeightLimit_Kd, true), (problem.Code, c.SyntaxOf(problem.Subject).ToString().StartsWith("f20<", StringComparison.Ordinal)));
        Assert.Equal((false, 1), (c.Analysis.IsVerified, c.Analysis.ErrorCount));
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("TypeHeightLimit_Kd", true), (record.Code, record.Text!.StartsWith("f20<", StringComparison.Ordinal)));
    }

    // R5 K10c (DECISIONS R5-M52), SPEC 8.15.3 (DECISIONS R4-M134 Q8): a layout over 2^63 - 1 bytes is a Language error at check, at its
    // by-value use (the parameter's Type occurrence), a Language record of the checked set; code generation is not reached.
    [Fact]
    public async Task MirCodegenRejectsAnOversizedLayoutAtCheck()
    {
        const string Type = "([649657 of [92737 of [153092023 of u8]]], u8)";
        const string Over = "func over(value: " + Type + ") => ()";
        var directory = Path.Combine(Path.GetTempPath(), "kimi-compilation-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "oversized", Directory = directory };
            project.AddSource("main.kimi", Over + "\nlet x = 1\n");
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            Assert.Contains("OversizedLayout_Kd", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerationResourceLimit_Kd", console.Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Over + "\nlet x = 1\n"));
        Assert.False(Analyze(c, OutputKind.Application));
        var problem = Assert.Single(c.Problems);
        Assert.Equal((DiagnosticCode.OversizedLayout_Kd, Type), (problem.Code, c.SyntaxOf(problem.Subject).ToString()));
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("OversizedLayout_Kd", Type, "layout larger than 2^63 - 1 bytes"), (record.Code, record.Text, record.Label));
    }

    // DECISIONS R5-M165: the Emission partition is the latest generation request's: the gate's step-1 refusal clears an earlier request's
    // record (here a publication failure's) and adds none.
    [Fact]
    public void AGateRefusalClearsAnEarlierEmissionRecord()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Unbuilt));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.False(c.Analyze().IsVerified);
        c.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.ArtifactPublicationFailed_Kd, c.Project.FilePath, note: "earlier");
        Assert.False(c.Generate(TextWriter.Null));
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
    }

    // DECISIONS R5-M156 (superseding R3-1): check publishes the MIR's construction records, here the unbuilt selection's (Unbuilt), as the
    // analysis's problems, control-flow Errors that reject the program and block generation.
    [Fact]
    public void CheckPublishesTheMirRecords()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Unbuilt));
        Assert.False(c.Analysis.IsVerified); // Nothing analyzed yet.
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.False(c.Analyze().IsVerified);
        var problem = Assert.Single(c.Problems);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, Call), (problem.Code, c.SyntaxOf(problem.Subject).ToString()));
        Assert.Equal((false, 1), (c.Analysis.IsVerified, c.Analysis.ErrorCount));
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("Unsupported_Kd", Call), (record.Code, record.Text));
        Assert.False(c.Generate(TextWriter.Null)); // the gate's step 1: the MIR's record, which check published, adds no Emission record
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
    }

    // R4-M71, PLAN G157 (8) (DIAGNOSTICS §4.1): a MIR record is a code without facts, published keyed by its location, so the records of a
    // for expansion's synthesized siblings $for.some and $for.none, which share their span, depth and kind, are one problem, not an
    // undefined order. No form records at them at check today, so the records are added to the analyzed version's program as a
    // construction rule adds them.
    [Fact]
    public void MirRecordsAtOneLocationAreOneProblem()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument(
            "Hello.kimi", "func sum(values: ref/Array<i32>) -> i32\n    var total = 0\n    for value in values\n        total += value\n    return total\npublic func main() => ()\n"));
        Assert.True(Analyze(c, OutputKind.Application), MinimalEmissionTest.Describe(c));
        var loop = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>().Single();
        var program = c.BuildMir();
        foreach (var arm in loop.Iteration!.Match.Arms)
        {
            program.Report(SourceRef.Of(arm.Pattern));
        }

        Assert.Equal(2, c.Problems.Count);
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("Unsupported_Kd", loop.Span), (record.Code, record.Span));
    }

    // R6-check H5 (DECISIONS R5-M185): the MIR decides acceptance. A program it rejects fails Analysis, its problem in Problems and
    // published by ReportAnalysis, where the old analysis accepted it (A5 P1's part borrow); a program it accepts is verified with no
    // problem, where the old analysis rejected it (`f()`'s result keeps no Loan live, PLAN G147 (7)).
    [Theory]
    [InlineData("var n = 7\nlet item = (n@ref, 5)\nlet x = item.1@ref\nn = 9\nrequire x == 5 else => $abort(\"part\")", "n = 9")]
    [InlineData("func use(x: uniq/i32, f: ref/(() -> ref/i32 during x)) -> i32\n    var old = x@follow@ref\n    let view = f()\n    x@follow = 9\n    return view@follow\npublic func main() => ()", null)]
    public void TheMirDecidesAcceptance(string source, string? conflict)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal((conflict is null, conflict is null ? 0 : 1), (c.Analyze().IsVerified, c.Analysis.ErrorCount));
        string[] expected = conflict is null ? [] : [$"{DiagnosticCode.ComparisonLoanConflict_Kd} {conflict}"];
        Assert.Equal(expected, c.Problems.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}"));
        c.ReportAnalysis();
        Assert.Equal(expected, TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => $"{x.Code} {x.Text}"));
    }

    // DECISIONS R4-M178 (PLAN G444): each public phase entry is a site that runs its walk on a compiler thread, so a valid program
    // nested deeper than a 1.5 MB stack holds, within U2's syntax depth 1,024, is accepted and generated from the test's thread. By
    // PLAN G444's triage, each Fact's depth exceeds what its own site's walk held on 1.5 MB (about the depth its comment names,
    // Release), so it needs its own site; without any site each overflowed (U1-b's fail-first runs; in a Debug host the CheckBound and
    // BuildMir Facts overflow first in the parser).
    [Fact]
    public void AddSourceParsesOnACompilerThread() // `if` 300; the parser overflowed from about 200.
        => AcceptsAndGenerates(Source(NestedIfs(300)));

    [Fact]
    public void CodeContextParseParsesOnACompilerThread() // Parentheses 600; the parser overflowed from about 380.
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, Parentheses(600));
        AcceptsAndGenerates(c);
    }

    [Fact]
    public void BindBindsOnACompilerThread() // Calls 200; the bind walk overflowed from about 89 in Debug, 138 in Release.
        => AcceptsAndGenerates(Source(Calls(200)));

    [Fact]
    public void CheckBoundPublishesOnACompilerThread() // Labeled `do` values 200; the HIR publish overflowed from about 150.
    {
        var c = Source(LabeledDos(200));
        AcceptsAndGenerates(c);
        Assert.True(c.Binding.CheckBound().IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void BuildMirBuildsOnACompilerThread() // Calls 480; the MIR builder overflowed from about 450.
        => AcceptsAndGenerates(Source(Calls(480)));

    // IMPL 21.3.2 (G144x U3, O-551 (a)): a directive outside excluded syntax whose Condition is an Error only through parts the syntax
    // depth limit cut cannot complete its selection, so the Compilation analyzes no semantic check: the cut's NestingLimit_Kd is its
    // record, whether the program is valid past the cut or not (`(cut) and false` too: the Error absorbs the other operand; c3-ifs-1030,
    // the parser's own skip of `true` under 1,019 parentheses). Fail-before: each I row also had dev's UnresolvedBinding_Kd at
    // `missing` (class 4, Q13).
    [Theory]
    [InlineData("m-dir-use-V", "NestingLimit_Kd@1:5")]
    [InlineData("m-dir-sel-I", "NestingLimit_Kd@1:5")]
    [InlineData("tw-dir-I", "NestingLimit_Kd@1:5")]
    [InlineData("t-se-return-V", "NestingLimit_Kd@2:9")]
    [InlineData("t-se-init-V", "NestingLimit_Kd@3:9")]
    [InlineData("t-se-return-I", "NestingLimit_Kd@2:9")]
    [InlineData("example-I", "NestingLimit_Kd@1:1028")]
    [InlineData("y-and-false-I", "NestingLimit_Kd@1:6")]
    [InlineData("c3-ifs-1030-I", "NestingLimit_Kd@6:1038")]
    // G144-37 N4, N5: a later #if's Condition is a source check and keeps its record (r1-src), as a target directive's does (r11).
    [InlineData("r1-src-I", "NestingLimit_Kd@1:5 | UnknownCompileTimeName_Kd@4:5")]
    [InlineData("r4-false-and-I", "NestingLimit_Kd@1:16")]
    [InlineData("r11-stacked-missing-I", "NestingLimit_Kd@1:5 | UnknownCompileTimeName_Kd@2:5")]
    // Pinned, not SPEC's record: SPEC 19.4 lists modifier placement among the source checks, so ProtectedPlacement_Kd@5:20 is owed.
    // It is lost through a check owned outside the source phase (Binding), removed by the open issue PLAN G1511 (G144-37): "SPEC 19.4
    // source checks owned by Binding are skipped in excluded syntax (an invalid program accepted: r1-prot-excl) and in a resting
    // Compilation (r1-prot)".
    [InlineData("r1-prot-I", "NestingLimit_Kd@1:5")]
    public void ADirectiveThatCannotCompleteItsSelectionRestsTheCompilation(string row, string records)
        => Assert.Equal(records, CheckedSites(Resting(row)));

    // SPEC 19.4, IMPL A.4 (G144x U3): a directive inside excluded syntax selects nothing, so its cut Condition rests nothing, in a body
    // and a member list too (review-design-u3 B1); a Condition the parser reads within L completes its selection at the parser's
    // count (c3-ifs-1023 to -1025, E: `true` at reader count 1,020). An `#if` whose target is missing has another part in error (SPEC
    // F.11: the target is the directive's), so its cut rests nothing (r12, G144-37 B2; fail-before: the rest dropped the
    // UnresolvedBinding_Kd at `missing`). Each keeps dev's records.
    [Theory]
    [InlineData("y-rest-in-excl-I", "NestingLimit_Kd@2:9 | UnresolvedBinding_Kd@5:23")]
    [InlineData("y-rest-in-excl-body-I", "NestingLimit_Kd@3:13 | UnresolvedBinding_Kd@6:23")]
    [InlineData("y-rest-in-excl-member-I", "NestingLimit_Kd@3:13 | UnresolvedBinding_Kd@6:23")]
    [InlineData("c3-ifs-1023-I", "UnresolvedBinding_Kd@10:23")]
    [InlineData("c3-ifs-1024-I", "UnresolvedBinding_Kd@10:23")]
    // The band open issue (PLAN G1512; G144-38): A.4's limit cuts this Condition, the parser's count does not, so the head
    // selects and keeps the UnresolvedBinding_Kd where IMPL 21.3.2 rests on NestingLimit_Kd alone; a known limit, rejected either way.
    [InlineData("c3-ifs-1025-I", "NestingLimit_Kd@6:1038 | UnresolvedBinding_Kd@10:23")]
    [InlineData("r12-no-target-eof-I", "UnresolvedBinding_Kd@1:23 | NestingLimit_Kd@2:5 | MissingSyntax_Kd@2:24001")]
    [InlineData("r12-no-target-body-I", "NestingLimit_Kd@2:9 | MissingSyntax_Kd@2:24005 | UnresolvedBinding_Kd@3:23")]
    public void ADirectiveThatSelectsNothingOrCompletesItsSelectionRestsNothing(string row, string records)
        => Assert.Equal(records, CheckedSites(Resting(row)));

    // IMPL 21.3.2 (G144-37 N4, r6): the rest is the Compilation's, not a document's, in either order of the module's documents: the
    // other document's source check keeps its record (UnknownCompileTimeName_Kd), its semantic ones go (dev's TypeMismatch_Kd,
    // MixedStartupBodies_Kd and UnresolvedBinding_Kd, class 4). Records are code@document:offset.
    [Theory]
    [InlineData("r6-later-doc-I", "NestingLimit_Kd@A.kimi:4 | UnknownCompileTimeName_Kd@B.kimi:4")]
    [InlineData("r6-later-doc-rev-I", "UnknownCompileTimeName_Kd@A.kimi:4 | NestingLimit_Kd@B.kimi:4")]
    public void ARestingCompilationRunsEveryDocumentsSourceChecks(string row, string records)
    {
        string[] documents = [$"#if {Ors(3000)}\n    func f() => ()\n", "#if missing\n    func g() => ()\n\nlet s: string = 1\npublic func main() => f()\n"];
        if (row == "r6-later-doc-rev-I")
        {
            Array.Reverse(documents);
        }

        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = OutputKind.Application;
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("A.kimi", documents[0]));
        c.Kotonoha.AddSource(new SourceDocument("B.kimi", documents[1]));
        c.Check();
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(records, string.Join(" | ", errors.Select(static x => $"{x.Code}@{x.Path}:{x.Span.Start}")));
    }

    // G144-39 (G144-38 N7): a root `#if` whose Condition is 1,100 terms joined by `or`, `true` but the one p from the end (0-based) or
    // every one an interpolated string, which a Condition does not admit (SPEC 19.2: PlainString has no interpolation). At the root the
    // reader's count is A.4's, so F-I's mark meets the module walk where its cut stands. A string above the cut keeps its
    // InvalidCompileTimeCondition_Kd beside the NL (no rest); at L (p 1,022) the string, whose segments take no mark, is the cut: its
    // mark stands one level above the node past L (ErrorKoto.SkipReach) and is cut as the string, NL alone, the rest (the invalid-form
    // record under a cut is commit (c)'s F-I move, as under the cut). Fail-before: p 1,022 and every-term threw "A skip mark at depth
    // 1024 lies under no cut" (CheckFaulted_Kd).
    // Rows: the interpolated term's place p (n7-interp-p<p>-I), or null for every term (n7-interp-all-I).
    public static TheoryData<int?, string> InterpolatedTerms => new()
    {
        { 1018, "NestingLimit_Kd@1:5 | InvalidCompileTimeCondition_Kd@1:653" },
        { 1019, "NestingLimit_Kd@1:5 | InvalidCompileTimeCondition_Kd@1:645" },
        { 1020, "NestingLimit_Kd@1:5 | InvalidCompileTimeCondition_Kd@1:637" },
        { 1021, "NestingLimit_Kd@1:5 | InvalidCompileTimeCondition_Kd@1:629" },
        { 1022, "NestingLimit_Kd@1:5 | NestingLimit_Kd@1:621" },
        { 1023, "NestingLimit_Kd@1:5" },
        { 1024, "NestingLimit_Kd@1:5" },
        { 1025, "NestingLimit_Kd@1:5" },
        { 1026, "NestingLimit_Kd@1:5" },
        { 1027, "NestingLimit_Kd@1:5" },
        { null, string.Join(" | ", ["NestingLimit_Kd@1:5", "NestingLimit_Kd@1:852", .. Enumerable.Range(78, 1022).Select(static i => $"InvalidCompileTimeCondition_Kd@1:{5 + (11 * i)}")]) },
    };

    [Theory]
    [MemberData(nameof(InterpolatedTerms))]
    public void AMarkAboveASlotThatTakesNoMarkStandsAtTheCut(int? interpolated, string records)
    {
        var terms = Enumerable.Repeat(interpolated is null ? "\"a\\(1)\"" : "true", 1100).ToArray();
        if (interpolated is { } p)
        {
            terms[1099 - p] = "\"a\\(1)\"";
        }

        Assert.Equal(records, CheckedSites($"#if {string.Join(" or ", terms)}\nfunc f() => ()\n\npublic func main() => ()\n"));
    }

    // `#if` with `true or` written `terms` times (G144x U3's `<ors>`): a left-deep chain past L at 1,025 terms.
    internal static string Ors(int terms)
        => string.Join(" or ", Enumerable.Repeat("true", terms));

    // A program checked as a Library build checks it, as its errors' sites (TestDiagnostics.ErrorSites).
    internal static string CheckedSites(string source)
        => TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source, output: OutputKind.Library), source);

    // G444's probe shapes: `public func f(c: bool) -> i32` with `depth` nested levels.
    internal static string NestedIfs(int depth)
    {
        var source = new System.Text.StringBuilder("func f(c: bool) -> i32\n");
        for (var i = 0; i < depth; i++)
        {
            source.Append(' ', 4 * (i + 1)).Append("if c\n");
        }

        return source.Append(' ', 4 * (depth + 1)).Append("let z = 1\n    return 1\n").ToString();
    }

    internal static string Parentheses(int depth)
        => $"func f(c: bool) -> i32\n    return {new string('(', depth)}1{new string(')', depth)}\n";

    internal static string Calls(int depth)
        => $"func g(x: i32) -> i32 => x\nfunc f(c: bool) -> i32\n    return {string.Concat(Enumerable.Repeat("g(", depth))}1{new string(')', depth)}\n";

    internal static string LabeledDos(int depth)
    {
        var source = new System.Text.StringBuilder("func f(c: bool) -> i32\n");
        for (var i = 0; i < depth; i++)
        {
            source.Append(' ', 4 * (i + 1)).Append($"let y{i}: i32 = label a{i}: do\n");
        }

        source.Append(' ', 4 * (depth + 1)).Append($"exit to a{depth - 1} 1\n");
        for (var i = depth - 1; i > 0; i--)
        {
            source.Append(' ', 4 * (i + 1)).Append($"exit to a{i - 1} y{i}\n");
        }

        return source.Append("    return y0\n").ToString();
    }

    // G144x U3's rows (design-u3 §6).
    private static string Resting(string row)
    {
        var ors = Ors(3000);
        return row switch
        {
            "m-dir-use-V" => $"#if {ors}\nfunc f() => ()\n\npublic func main() => f()\n",
            "m-dir-sel-I" => $"#if {ors}\nfunc f() -> i32 => missing\n\npublic func main() => ()\n",
            "tw-dir-I" => $"#if {Ors(2000)}\nfunc f() => ()\n#if true\nfunc g() -> i32 => missing\n\npublic func main() => ()\n",
            "t-se-return-V" => $"func g() -> i32\n    #if {ors}\n        return 1\n",
            "t-se-init-V" => $"func g() -> i32\n    let x: i32\n    #if {ors}\n        x = 1\n    return x\n",
            "t-se-return-I" => $"func g(b: bool) -> i32\n    #if {ors}\n        return 1\n    missing()\n",
            "example-I" => $"#if {new string('(', 1100)}debug{new string(')', 1100)}\n    func helper() -> i32 => 1\nfunc main() -> ()\n    let a = helper()\n    let b: i32 = missing\n",
            "y-and-false-I" => $"#if ({ors}) and false\nfunc f() => ()\n\npublic func main() => f()\n",
            "y-rest-in-excl-I" => $"#if false\n    #if {ors}\n        func f() => ()\n\npublic func main() => f()\n",
            "y-rest-in-excl-body-I" => $"#if false\n    func g() -> ()\n        #if {ors}\n            let x = 1\n\npublic func main() => missing\n",
            "y-rest-in-excl-member-I" => $"#if false\n    struct S\n        #if {ors}\n            let x: i32\n\npublic func main() => missing\n",
            "c3-ifs-1023-I" => NestedIfDirective(1012),
            "c3-ifs-1024-I" => NestedIfDirective(1013),
            "c3-ifs-1025-I" => NestedIfDirective(1014),
            "r12-no-target-eof-I" => $"public func main() => missing\n#if {ors}",
            "r12-no-target-body-I" => $"func h() -> ()\n    #if {ors}\npublic func main() => missing\n",
            "r1-src-I" => $"#if {ors}\n    func f() => ()\n\n#if missing\n    func g() => ()\n\npublic func main() => f()\n",
            "r4-false-and-I" => $"#if false and ({ors})\n    func f() => ()\n\npublic func main() => f()\n",
            "r11-stacked-missing-I" => $"#if {ors}\n#if missing\nfunc f() => ()\n\npublic func main() => f()\n",
            "r1-prot-I" => $"#if {ors}\n    func f() => ()\n\ngroup G\n    protected func h() => ()\n\npublic func main() => ()\n",
            _ => NestedIfDirective(1019),
        };
    }

    // `#if` with `parentheses` parentheses around `true` in four nested `if c` (G144x U3's c3 rows): the Condition's root stands at
    // IMPL A.4 depth 11 and at reader count 6, so `true` stands at 11 + parentheses and at 6 + parentheses.
    private static string NestedIfDirective(int parentheses)
    {
        var source = new System.Text.StringBuilder("func f(c: bool) -> i32\n");
        for (var i = 0; i < 4; i++)
        {
            source.Append(' ', 4 * (i + 1)).Append("if c\n");
        }

        return source.Append(' ', 20).Append("#if ").Append('(', parentheses).Append("true").Append(')', parentheses).Append('\n')
            .Append(' ', 24).Append("let z = 1\n    return 1\n\npublic func main() => missing\n").ToString();
    }

    private static Compilation Source(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Deep.kimi", source));
        return c;
    }

    // Checked and generated as a Library build runs them; Check is not a site, so each site is reached from the test's thread.
    private static void AcceptsAndGenerates(Compilation c)
    {
        c.Project.ProjectFile.OutputKind = OutputKind.Library;
        Assert.True(c.Check() && c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    private static bool Analyze(Compilation c, OutputKind output)
        => c.Bind().IsComplete && (c.IsTestBuild ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(output)).IsComplete && c.Analyze().IsVerified;
}
