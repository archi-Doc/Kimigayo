// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The pipeline switch and the analysis entry points (plan 12.1, 13.2; R3–R6): <c>KIMI_PIPELINE</c> selects the default,
/// every pipeline publishes the MIR's construction records, validator findings and checked set's records at check (DECISIONS R5-M156),
/// and the non-default pipeline marks the compiler identity. MIR code generation, the one code generator (DECISIONS R5-M160), writes the
/// module of an accepted program (R5 C0b1, X2). Every product generation request passes the generation entry's gate (R5 C0a): an analysis
/// with MIR records generates nothing, and <c>kimi test</c> publishes its records as <c>kimi build</c> does.</summary>
public sealed class PipelineSwitchTest
{
    // The MIR's one Unsupported record here, at `a == b`, a form the old path's check accepts (its generation refuses it): the tuple operator
    // without its comparison (MirBuildTest.Unbuilt; PD30's (e) row until R7).
    private static readonly string Unbuilt = MirBuildTest.Unbuilt.Declarations +
        $"func same(a: ref/{MirBuildTest.Unbuilt.Tuple}, b: ref/{MirBuildTest.Unbuilt.Tuple}) -> bool => a == b\nlet x = 1\n";

    // Every selection a compilation can make (DECISIONS R5-M156, R5-M160).
    private static readonly CompilerPipeline[] Pipelines = [CompilerPipeline.Legacy, CompilerPipeline.Mir];

    // The default is the pipeline KIMI_PIPELINE names, legacy when unset; any other value is a configuration error, not a fallback.
    [Fact]
    public void TheEnvironmentSelectsTheDefault()
    {
        Assert.Equal(CompilerPipeline.Legacy, Compilation.PipelineOf(null));
        Assert.Equal(CompilerPipeline.Legacy, Compilation.PipelineOf("legacy"));
        Assert.Equal(CompilerPipeline.Mir, Compilation.PipelineOf("mir"));
        Assert.Equal(Compilation.PipelineOf(Environment.GetEnvironmentVariable("KIMI_PIPELINE")), Compilation.DefaultPipeline);
        Assert.Equal(Compilation.DefaultPipeline, Compilation.CreateForTest().Pipeline);
        foreach (var value in new[] { "MIR", "Legacy", " mir", "both" })
        {
            Assert.Contains("KIMI_PIPELINE", Assert.Throws<InvalidOperationException>(() => Compilation.PipelineOf(value)).Message);
        }
    }

    // PLAN G199: the default is read once, by Compilation's type initializer, which the runtime runs once, so threads making the first
    // compilations at once all get the pipeline the environment names. A Nullable set on its first read could be read as its default
    // (legacy) by a thread racing the first one; no test can widen that window, so this pins the default as an initonly field of the
    // type initializer and races the first compilations of a fresh process, where Compilation's statics are new.
    [Fact]
    public Task ThreadsMakingTheFirstCompilationsAgreeOnTheDefaults()
        => FreshProcess.RunExplicit(typeof(PipelineSwitchTest), nameof(PipelineSwitchTest.RaceTheFirstCompilationsOfAFreshProcess));

    // Runs only in the process that ThreadsMakingTheFirstCompilationsAgreeOnTheDefaults starts, where nothing reads a Compilation
    // static before the threads make the first compilations.
    [Fact(Explicit = true)]
    public void RaceTheFirstCompilationsOfAFreshProcess()
    {
        var field = typeof(Compilation).GetField(nameof(Compilation.DefaultPipeline), BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(field is { IsInitOnly: true }, nameof(Compilation.DefaultPipeline));

        const int Count = 8;
        var (pipelines, errors) = (new CompilerPipeline[Count], new Exception?[Count]);
        using var start = new Barrier(Count);
        var threads = Enumerable.Range(0, Count).Select(i => new Thread(() =>
        {
            try
            {
                start.SignalAndWait();
                var c = Compilation.CreateForTest();
                pipelines[i] = c.Pipeline;
            }
            catch (Exception e)
            {
                errors[i] = e;
            }
        })).ToArray();
        Array.ForEach(threads, static x => x.Start());
        Array.ForEach(threads, static x => x.Join());
        Assert.All(errors, Assert.Null);
        Assert.All(pipelines, static x => Assert.Equal(Compilation.DefaultPipeline, x));
    }

    // The non-default pipeline marks the check identity and adds its entry to the test artifact manifest (DECISIONS R5-M156); the code
    // generator marks neither since its selection ended at G4 (DECISIONS R5-M160).
    [Fact]
    public void TheNonDefaultPipelineMarksTheIdentity()
    {
        var identity = Compilation.DefaultPipeline == CompilerPipeline.Mir ? Compilation.CompilerVersion + " [mir]" : Compilation.CompilerVersion;
        Assert.Equal(identity, Compilation.CompilerIdentity);
        var plain = Manifest(CompilerPipeline.Legacy);
        Assert.Equal(plain.Take(7).Concat(["pipeline", "mir"]).Concat(plain.Skip(7)), Manifest(CompilerPipeline.Mir));
        Assert.DoesNotContain("codegen", plain);
    }

    // R5 K1, C0b1 (DECISIONS R5-M65, R5-M146): the checked set exists on every pipeline; MIR code generation, on the process's pipeline
    // (either, DECISIONS R5-M156), writes the module of each output form, an Application's (with its entry), a test build's (the test
    // runtime's entry; C10a, DECISIONS R5-M136) or a Library's, never the old generator's module.
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
        Assert.True(c.Prepare(WindowsProfile.Target));
        var functions = test ? "#Test\nfunc first() => ()\n#Test\nfunc second() => ()\n" : "public func first() => ()\npublic func second() => ()\n";
        var statements = output == OutputKind.Library ? string.Empty : test ? "let x = pick(1)\n" : "first()\n";
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", functions + "func pick<T>(value: T) -> T => value@move\n" + statements));
        Assert.True(Analyze(c, output), MinimalEmissionTest.Describe(c));
        if (test)
        {
            c.Tests.Discover(c); // As Project does after acceptance.
        }

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
        var directory = Path.Combine(Path.GetTempPath(), "kimi-pipeline-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Unbuilt));
            Assert.False(Analyze(c, OutputKind.Application));
            Assert.Equal(DiagnosticCode.Unsupported_Kd, Assert.Single(c.Problems).Code);
            c.Project.Directory = directory;
            c.Project.Name = "Hello";
            c.Project.ProjectFile.OutputPath = "out/Hello.ll";
            using var writer = new StringWriter();
            Assert.False(c.Generate(writer));
            Assert.Empty(writer.ToString());
            Assert.False(EmissionArtifacts.Publish(c, null, out var path));
            Assert.Null(path);
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
        var directory = Path.Combine(Path.GetTempPath(), "kimi-pipeline-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "wide", Directory = directory };
            project.ProjectFile.Targets = [WindowsProfile.Target];
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

    // R5 K4b (DECISIONS R5-M156, superseding R5-M41's gate): the checked set's records are analysis problems on every pipeline,
    // control-flow Errors at their located subjects that reject the program.
    [Fact]
    public void EveryPipelinePublishesTheCollectorRecords()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var type = string.Concat(Enumerable.Repeat("Option<", 32)) + "ref/i32" + new string('>', 32);
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", $"func pick<T>(value: T) -> T => value@move\nlet v: {type} = .None\nlet w = pick(v@move)\n"));
        foreach (var pipeline in Pipelines)
        {
            c.Pipeline = pipeline;
            Assert.False(Analyze(c, OutputKind.Application));
            var problem = Assert.Single(c.Problems);
            Assert.Equal((DiagnosticCode.Unsupported_Kd, "pick(v@move)"), (problem.Code, c.SyntaxOf(problem.Subject).ToString()));
            Assert.Equal((false, 1), (c.Analysis.IsVerified, c.Analysis.ErrorCount));
            c.ReportAnalysis();
            var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
            Assert.Equal(("Unsupported_Kd", "pick(v@move)"), (record.Code, record.Text));
        }
    }

    // R5 K10c (DECISIONS R5-M52), SPEC 8.15.3 (DECISIONS R4-M134 Q8): a layout over 2^63 - 1 bytes is a Language error at check on every
    // pipeline, at its by-value use (the parameter's Type occurrence), a Language record of the checked set; code generation is not reached.
    [Fact]
    public async Task MirCodegenRejectsAnOversizedLayoutAtCheck()
    {
        const string Type = "([649657 of [92737 of [153092023 of u8]]], u8)";
        const string Over = "func over(value: " + Type + ") => ()";
        var directory = Path.Combine(Path.GetTempPath(), "kimi-pipeline-switch-" + Guid.NewGuid().ToString("N"));
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

        foreach (var pipeline in Pipelines)
        {
            var c = Compilation.CreateForTest();
            c.Pipeline = pipeline;
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Over + "\nlet x = 1\n"));
            Assert.False(Analyze(c, OutputKind.Application));
            var problem = Assert.Single(c.Problems);
            Assert.Equal((DiagnosticCode.OversizedLayout_Kd, Type), (problem.Code, c.SyntaxOf(problem.Subject).ToString()));
            c.ReportAnalysis();
            var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
            Assert.Equal(("OversizedLayout_Kd", Type, "layout larger than 2^63 - 1 bytes"), (record.Code, record.Text, record.Label));
        }
    }

    // DECISIONS R5-M156 (superseding R3-1): every pipeline publishes the MIR's construction records, here the unbuilt tuple operator's
    // (PD30's (e) row), as the analysis's problems, control-flow Errors that reject the program and block generation; switching the
    // pipeline revokes the analysis and changes no record.
    // DECISIONS R5-M165: the Emission partition is the latest generation request's: the gate's step-1 refusal clears an earlier request's
    // record (here a publication failure's) and adds none.
    [Fact]
    public void AGateRefusalClearsAnEarlierEmissionRecord()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Unbuilt));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.False(c.Analyze().IsVerified);
        c.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.ArtifactPublicationFailed_Kd, c.Project.FilePath, note: "earlier");
        Assert.False(c.Generate(TextWriter.Null));
        Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
    }

    [Fact]
    public void EveryPipelinePublishesTheMirRecords()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Unbuilt));
        foreach (var pipeline in Pipelines)
        {
            c.Pipeline = pipeline;
            Assert.False(c.Analysis.IsVerified); // Nothing analyzed yet, or the switch revoked the analysis.
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.False(c.Analyze().IsVerified);
            var problem = Assert.Single(c.Problems);
            Assert.Equal((DiagnosticCode.Unsupported_Kd, "a == b"), (problem.Code, c.SyntaxOf(problem.Subject).ToString()));
            Assert.Equal((false, 1), (c.Analysis.IsVerified, c.Analysis.ErrorCount));
            c.ReportAnalysis();
            var record = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
            Assert.Equal(("Unsupported_Kd", "a == b"), (record.Code, record.Text));
            Assert.False(c.Generate(TextWriter.Null)); // the gate's step 1: the MIR's record, which check published, adds no Emission record
            Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        }
    }

    // An accepted program round-trips across a pipeline switch: the switch revokes the analysis, and the next one gives the same diagnostics
    // and IR, since every pipeline publishes the same records (DECISIONS R5-M156).
    [Fact]
    public void AnAcceptedProgramIsTheSameOnEitherPipeline()
    {
        const string Source = "struct Box\n    public let v: i32\n    public init(v: i32) => self.v = v\nfunc twice(b: ref/Box) -> i32 => b.v * 2\n" +
            "let b = Box.init(21)\nrequire twice(b@ref) == 42 else => $abort(\"twice\")\nConsole.writeLine(\"ok\")\n";
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Legacy; // Whatever the process's pipeline, so that each switch below changes it.
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Source));
        var legacy = Check(c);
        c.Pipeline = CompilerPipeline.Mir;
        Assert.False(c.Analysis.IsVerified);
        Assert.Equal(legacy, Check(c));
        c.Pipeline = CompilerPipeline.Legacy;
        Assert.False(c.Analysis.IsVerified);
        Assert.Equal(legacy, Check(c));
    }

    // R4-M71, PLAN G157 (8) (DIAGNOSTICS §4.1): a MIR record is a code without facts, published keyed by its location, so the records of a
    // for expansion's synthesized siblings $for.some and $for.none, which share their span, depth and kind, are one problem, not an
    // undefined order. No form records at them at check today, so the records are added to the analyzed version's program as a
    // construction rule adds them.
    [Fact]
    public void MirRecordsAtOneLocationAreOneProblem()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
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

    // R6-check H5 (DECISIONS R5-M185): the MIR decides acceptance on every pipeline. A program it rejects fails Analysis, its problem in
    // Problems and published by ReportAnalysis, where the old analysis accepted it (A5 P1's part borrow); a program it accepts is
    // verified with no problem, where the old analysis rejected it (`f()`'s result keeps no Loan live, PLAN G147 (7)).
    [Theory]
    [InlineData("var n = 7\nlet item = (n@ref, 5)\nlet x = item.1@ref\nn = 9\nrequire x == 5 else => $abort(\"part\")", "n = 9")]
    [InlineData("func use(x: uniq/i32, f: ref/(() -> ref/i32 during x)) -> i32\n    var old = x@follow@ref\n    let view = f()\n    x@follow = 9\n    return view@follow\npublic func main() => ()", null)]
    public void TheMirDecidesAcceptance(string source, string? conflict)
    {
        foreach (var pipeline in Pipelines)
        {
            var c = Compilation.CreateForTest();
            c.Pipeline = pipeline;
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.Equal((conflict is null, conflict is null ? 0 : 1), (c.Analyze().IsVerified, c.Analysis.ErrorCount));
            string[] expected = conflict is null ? [] : [$"{DiagnosticCode.ComparisonLoanConflict_Kd} {conflict}"];
            Assert.Equal(expected, c.Problems.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}"));
            c.ReportAnalysis();
            Assert.Equal(expected, TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => $"{x.Code} {x.Text}"));
        }
    }

    // A checked program's published diagnostics and IR.
    private static (string Diagnostics, string Ir) Check(Compilation c)
    {
        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        c.Binding.ReportStartupDiagnostics();
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        c.ReportAnalysis();
        return (string.Join('\n', TestDiagnostics.Of(c).Select(static x => x.ToString())), CompilationTestHelper.WriteIr(c));
    }

    private static bool Analyze(Compilation c, OutputKind output)
        => c.Bind().IsComplete && (c.IsTestBuild ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(output)).IsComplete && c.Analyze().IsVerified;

    private static string[] Manifest(CompilerPipeline pipeline)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = pipeline;
        c.IsTestBuild = true;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Tests.kimi", "#Test\nfunc sample() => ()"));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckTestStartup().IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        c.Tests.Discover(c);
        return c.Tests.ManifestFields;
    }
}
