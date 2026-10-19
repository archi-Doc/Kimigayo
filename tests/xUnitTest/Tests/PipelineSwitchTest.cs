// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The pipeline switch and the analysis entry points (plan 12.1, 13.2; R3–R5): <c>KIMI_PIPELINE</c> selects the default,
/// only the mir pipeline publishes the MIR's records and marks the compiler identity, the legacy output does not depend on it, and every
/// analysis notifies the difference ledger's observer. <c>KIMI_CODEGEN</c> (R5 PD4) selects MIR code generation under the mir pipeline
/// only, which collects the checked set and writes a module only where its coverage rows cover it (R5 C0b1). Every product generation
/// request passes the generation entry's gate (R5 C0a): an analysis with MIR records generates nothing, and <c>kimi test</c> publishes its
/// generation records as <c>kimi build</c> does.</summary>
public sealed class PipelineSwitchTest
{
    // A fill of a length its instance decides is the MIR's one Unsupported record here, a form no R3 unit builds.
    private const string Source = "func filled<length N>(value: i32) -> [N of i32] => [N of value]\nlet values = filled<2>(1)\n";

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

    // R5 PD4: KIMI_CODEGEN selects the old generator when unset or legacy, and MIR code generation only with KIMI_PIPELINE=mir; any other
    // value or combination is a configuration error. A compilation's code generator follows its pipeline, MIR code generation needing mir.
    [Fact]
    public void TheEnvironmentSelectsTheCodegenUnderMirOnly()
    {
        Assert.Equal(CompilerCodegen.Legacy, Compilation.CodegenOf(CompilerPipeline.Legacy, null));
        Assert.Equal(CompilerCodegen.Legacy, Compilation.CodegenOf(CompilerPipeline.Mir, "legacy"));
        Assert.Equal(CompilerCodegen.Mir, Compilation.CodegenOf(CompilerPipeline.Mir, "mir"));
        Assert.Equal(Compilation.CodegenOf(Compilation.DefaultPipeline, Environment.GetEnvironmentVariable("KIMI_CODEGEN")), Compilation.DefaultCodegen);
        Assert.Equal(Compilation.DefaultCodegen, Compilation.CreateForTest().Codegen);
        foreach (var (pipeline, value) in new[] { (CompilerPipeline.Legacy, "mir"), (CompilerPipeline.Mir, "MIR"), (CompilerPipeline.Mir, " mir"), (CompilerPipeline.Mir, "both") })
        {
            Assert.Contains("KIMI_CODEGEN", Assert.Throws<InvalidOperationException>(() => Compilation.CodegenOf(pipeline, value)).Message);
        }

        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Legacy;
        Assert.Throws<InvalidOperationException>(() => c.Codegen = CompilerCodegen.Mir);
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Mir;
        c.Pipeline = CompilerPipeline.Legacy;
        Assert.Equal(CompilerCodegen.Legacy, c.Codegen);
    }

    // Only the mir pipeline marks the check identity and adds its entry to the test artifact manifest, and MIR code generation its own.
    [Fact]
    public void OnlyMirMarksTheIdentity()
    {
        var identity = (Compilation.DefaultPipeline, Compilation.DefaultCodegen) switch
        {
            (CompilerPipeline.Legacy, _) => Compilation.CompilerVersion,
            (_, CompilerCodegen.Legacy) => Compilation.CompilerVersion + " [mir]",
            _ => Compilation.CompilerVersion + " [mir, codegen mir]",
        };
        Assert.Equal(identity, Compilation.CompilerIdentity);
        var legacy = Manifest(CompilerPipeline.Legacy, CompilerCodegen.Legacy);
        Assert.Equal(legacy.Take(7).Concat(["pipeline", "mir"]).Concat(legacy.Skip(7)), Manifest(CompilerPipeline.Mir, CompilerCodegen.Legacy));
        Assert.Equal(legacy.Take(7).Concat(["pipeline", "mir", "codegen", "mir"]).Concat(legacy.Skip(7)), Manifest(CompilerPipeline.Mir, CompilerCodegen.Mir));
    }

    // R5 K1, C0b1 (DECISIONS R5-M65): only MIR code generation collects the checked set; it writes the module of an output form whose items
    // its coverage rows cover, an Application's (with its entry) or a Library's, and otherwise writes nothing and records one Unsupported_Kd
    // in the Emission partition at each subject of a row not written yet, here a call (C3a) and a test build's TestEntry at each test
    // (C10a); never the old generator's module. The generation entry (R5 C0a) writes the old generator's module otherwise, as the adapter does.
    // R5 K11 (DECISIONS R5-M77): it generates the closure of the output roots, an Application's startup body, a test build's discovered
    // tests (not its top-level statements and their instance) and a Library's every root.
    [Theory]
    [InlineData(OutputKind.Application, false, new[] { "first()" }, new[] { "first", "top" })]
    [InlineData(OutputKind.Application, true, new[] { "first() => ()", "second() => ()" }, new[] { "first", "second" })]
    [InlineData(OutputKind.Library, false, new string[0], new[] { "first", "second", "top" })]
    public void MirCodegenWritesEachCoveredOutputForm(OutputKind output, bool test, string[] records, string[] generated)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = test;
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Legacy;
        Assert.True(c.Prepare(WindowsProfile.Target));
        var functions = test ? "#Test\nfunc first() => ()\n#Test\nfunc second() => ()\n" : "public func first() => ()\npublic func second() => ()\n";
        var statements = output == OutputKind.Library ? string.Empty : test ? "let x = pick(1)\n" : "first()\n";
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", functions + "func pick<T>(value: T) -> T => value@move\n" + statements));
        Assert.True(Analyze(c, output), MinimalEmissionTest.Describe(c, null));
        Assert.Null(c.Instances);
        using (var legacy = new StringWriter())
        {
            Assert.True(c.Generate(legacy));
            Assert.Equal(CompilationTestHelper.WriteIr(c), legacy.ToString());
        }

        c.Codegen = CompilerCodegen.Mir;
        Assert.True(Analyze(c, output), MinimalEmissionTest.Describe(c, null));
        if (test)
        {
            c.Tests.Discover(c); // As Project does after acceptance.
        }

        var set = Assert.IsType<CheckedSet>(c.Instances);
        var written = records.Length == 0;
        using (var writer = new StringWriter())
        {
            Assert.Equal(written, c.Generate(writer));
            Assert.Equal(written, writer.ToString().Length != 0);
            Assert.Equal(written && output == OutputKind.Application, writer.ToString().Contains("define void @__kimi_start()", StringComparison.Ordinal));
        }

        var items = set.Items.ToArray();
        Assert.Equal(generated, Enumerable.Range(0, items.Length).Where(set.IsGenerated).Select(x => items[x].Key.Body.Def.Decl?.Name ?? "top").Order());

        Assert.Equal(written, c.WriteIr(TextWriter.Null, out var failure));
        Assert.Equal(written, failure is null);
        Assert.Equal(written, c.Validate(out _));
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.All(errors, static x => Assert.Equal("Unsupported_Kd", x.Code));
        Assert.Equal(records, errors.Select(static x => x.Text).Order(StringComparer.Ordinal));
    }

    // R5 C0a (PD12's gate, step 1; DECISIONS R5-M64): every generation request passes the entry's gate, artifact publication included,
    // which ran the old generator on a program with MIR records before; the records are the analysis's own, so generation adds none.
    [Fact]
    public void TheGateRefusesMirRecordsForEveryCaller()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-pipeline-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var c = Compilation.CreateForTest();
            c.Pipeline = CompilerPipeline.Mir;
            c.Codegen = CompilerCodegen.Legacy;
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Source));
            Assert.False(Analyze(c, OutputKind.Application));
            Assert.Equal(DiagnosticCode.Unsupported_Kd, Assert.Single(c.Problems).Code);
            c.Project.Directory = directory;
            c.Project.Name = "Hello";
            c.Project.ProjectFile.OutputPath = "out/Hello.ll";
            using var writer = new StringWriter();
            Assert.False(c.Generate(writer));
            Assert.Empty(writer.ToString());
            Assert.False(EmissionArtifacts.Publish(c, out var path, out var failure));
            Assert.Null(path);
            Assert.Null(failure);
            Assert.Empty(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
            Assert.False(c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // R5 C0a: kimi test publishes its generation records through the entry, as kimi build does: a test build the old generator refuses
    // reports the located Unsupported_Kd, not failure text. Under MIR code generation the check rejects the instance first (R5-M54).
    [Fact]
    public void TheTestCommandPublishesTheGenerationRecords()
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
            var c = project.PrepareTests(TestContext.Current.CancellationToken);
            Assert.Equal(Compilation.DefaultCodegen == CompilerCodegen.Mir, c is null);
            if (c is not null)
            {
                var stem = Path.Combine(directory, "tests");
                Assert.False(project.PublishArtifacts(c, new(stem + ".ll", stem + ".link.json", stem + ".link.build.json", stem + ".O2"), null, out _));
            }

            Assert.Contains("Unsupported_Kd", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerationFailed_Kd", console.Text, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.ll", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // R5 K4b (DECISIONS R5-M41): under MIR code generation the checked set's records are analysis problems too, control-flow Errors at
    // their located subjects that reject the program, while the mir pipeline alone does not collect.
    [Fact]
    public void MirCodegenPublishesTheCollectorRecords()
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Legacy;
        Assert.True(c.Prepare(WindowsProfile.Target));
        var type = string.Concat(Enumerable.Repeat("Option<", 32)) + "ref/i32" + new string('>', 32);
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", $"func pick<T>(value: T) -> T => value@move\nlet v: {type} = .None\nlet w = pick(v@move)\n"));
        Assert.True(Analyze(c, OutputKind.Application), MinimalEmissionTest.Describe(c, null));

        c.Codegen = CompilerCodegen.Mir;
        Assert.False(Analyze(c, OutputKind.Application));
        var problem = Assert.Single(c.Problems);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, "pick(v@move)"), (problem.Code, c.SyntaxOf(problem.Subject).ToString()));
        Assert.Equal((false, 1), (c.Analysis.IsVerified, c.Analysis.ErrorCount));
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("Unsupported_Kd", "pick(v@move)"), (record.Code, record.Text));
    }

    // R5 K10c (DECISIONS R5-M52): under MIR code generation a layout over 2^63 - 1 bytes is a Language error at check, at its by-value use;
    // until G2 the default pipeline accepts it at check and the old generator refuses generation with its generation limit's code, without
    // throwing.
    [Fact]
    public async Task MirCodegenRejectsAnOversizedLayoutAtCheck()
    {
        const string Over = "func over(value: ([649657 of [92737 of [153092023 of u8]]], u8)) => ()";
        var directory = Path.Combine(Path.GetTempPath(), "kimi-pipeline-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "oversized", Directory = directory };
            project.AddSource("main.kimi", Over + "\nlet x = 1\n");
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            Assert.Contains("GenerationResourceLimit_Kd", console.Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Legacy;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Over + "\nlet x = 1\n"));
        Assert.True(Analyze(c, OutputKind.Application), MinimalEmissionTest.Describe(c, null));

        c.Codegen = CompilerCodegen.Mir;
        Assert.False(Analyze(c, OutputKind.Application));
        var problem = Assert.Single(c.Problems);
        Assert.Equal((DiagnosticCode.OversizedLayout_Kd, Over), (problem.Code, c.SyntaxOf(problem.Subject).ToString()));
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("OversizedLayout_Kd", Over["func ".Length..], "layout larger than 2^63 - 1 bytes"), (record.Code, record.Text, record.Label));
    }

    // Under mir the MIR's records are the analysis's problems and control-flow Errors that reject the program and block generation;
    // switching back to legacy gives the old path's diagnostics and IR unchanged.
    [Fact]
    public void OnlyMirPublishesTheMirRecords()
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Legacy;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Source));
        var (diagnostics, ir) = Check(c);
        Assert.Empty(c.Problems);

        c.Pipeline = CompilerPipeline.Mir;
        Assert.False(c.Analysis.IsVerified); // The switch revokes the analysis.
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.False(c.Analyze().IsVerified);
        var problem = Assert.Single(c.Problems);
        Assert.Equal(DiagnosticCode.Unsupported_Kd, problem.Code);
        Assert.Equal("[N of value]", c.SyntaxOf(problem.Subject).ToString());
        Assert.Equal((false, 1), (c.Analysis.IsVerified, c.Analysis.ErrorCount));
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(("Unsupported_Kd", "[N of value]"), (record.Code, record.Text));
        Assert.False(c.WriteIr(TextWriter.Null, out var failure));
        Assert.Contains("MIR", failure);
        Assert.False(c.Validate(out _));

        c.Pipeline = CompilerPipeline.Legacy;
        Assert.Equal((diagnostics, ir), Check(c));
    }

    // R4-M17: on both pipelines, every Analyze raises Compilation.Analyzed once, after the old path's records and, under mir, the MIR's
    // are final: the observer reads the analysis Analyze returns, the MIR's Unsupported record included.
    [Fact]
    public void AnalyzeNotifiesTheObserverOnce()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Source));
        var observed = new List<AnalysisResult>();
        void Observe(Compilation x)
        {
            if (ReferenceEquals(x, c))
            {
                observed.Add(x.Analysis);
            }
        }

        Compilation.Analyzed += Observe;
        try
        {
            foreach (var (pipeline, errors) in new[] { (CompilerPipeline.Legacy, 0), (CompilerPipeline.Mir, 1) })
            {
                c.Pipeline = pipeline;
                Assert.True(c.Bind().IsComplete);
                Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
                var result = c.Analyze();
                Assert.Equal(errors, result.ErrorCount);
                Assert.Equal([result], observed);
                observed.Clear();
            }
        }
        finally
        {
            Compilation.Analyzed -= Observe;
        }
    }

    // Tests read a failure that OwnershipIssue.Code maps to several codes through one helper each: its codes are exactly the images
    // of that failure, whatever the issue's other facts.
    [Fact]
    public void TheMultiCodeHelpersAreTheOwnershipMapping()
    {
        var images = new Dictionary<OwnershipRequirement, HashSet<DiagnosticCode>>();
        foreach (var (failure, code) in OwnershipCodes())
        {
            Assert.Equal(failure == OwnershipRequirement.ComparisonLoanConflict, code.IsLoanConflict());
            Assert.Equal(failure == OwnershipRequirement.UnprovenOrigin, code.IsUnprovenOrigin());
            images.TryAdd(failure, []);
            images[failure].Add(code);
        }

        Assert.Equal(4, images[OwnershipRequirement.ComparisonLoanConflict].Count);
        Assert.Equal(3, images[OwnershipRequirement.UnprovenOrigin].Count);
    }

    /// <summary>Lists the code <see cref="OwnershipIssue.Code"/> gives each failure under every combination of the issue's other facts:
    /// a call or another source, an Origin obligation refuted or not, another obligation or none, a reservation or not, an activation or
    /// not.</summary>
    /// <returns>Each failure with each of its codes, repeated.</returns>
    internal static IEnumerable<(OwnershipRequirement Failure, DiagnosticCode Code)> OwnershipCodes()
    {
        var c = CompilationTestHelper.Parse("f(1)");
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().First();
        BindingObligation[] obligations =
        [
            new(BindingObligationKind.OriginOutlives, call, default, Longer: new(OriginKind.Projection), Shorter: BoundOrigin.Static),
            new(BindingObligationKind.OriginOutlives, call, default, Longer: BoundOrigin.Static, Shorter: BoundOrigin.Static),
            new(BindingObligationKind.TypeRole, call, default),
        ];
        foreach (var failure in Enum.GetValues<OwnershipRequirement>())
        {
            foreach (var source in new Koto[] { call, c.Kotonoha.RootKoto })
            {
                foreach (var obligation in obligations.Cast<BindingObligation?>().Append(null))
                {
                    for (var facts = 0; facts < 4; facts++)
                    {
                        yield return (failure, new OwnershipIssue(source, failure, Reservation: (facts & 1) - 1, Activation: facts >= 2, Obligation: obligation).Code);
                    }
                }
            }
        }
    }

    private static (string Diagnostics, string Ir) Check(Compilation c)
    {
        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        c.Binding.ReportStartupDiagnostics();
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        c.ReportAnalysis();
        return (string.Join('\n', TestDiagnostics.Of(c).Select(static x => x.ToString())), CompilationTestHelper.WriteIr(c));
    }

    private static bool Analyze(Compilation c, OutputKind output)
        => c.Bind().IsComplete && (c.IsTestBuild ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(output)).IsComplete && c.Analyze().IsVerified;

    private static string[] Manifest(CompilerPipeline pipeline, CompilerCodegen codegen)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = pipeline;
        c.Codegen = codegen;
        c.IsTestBuild = true;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Tests.kimi", "#Test\nfunc sample() => ()"));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckTestStartup().IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        c.Tests.Discover(c);
        return c.Tests.ManifestFields;
    }
}
