// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The pipeline switch and the analysis entry points (plan 12.1, 13.2; R3–R5): <c>KIMI_PIPELINE</c> selects the default,
/// only the mir pipeline publishes the MIR's records and marks the compiler identity, the legacy output does not depend on it, and every
/// analysis notifies the difference ledger's observer.</summary>
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

    // Only the mir pipeline marks the check identity and adds its entry to the test artifact manifest.
    [Fact]
    public void OnlyMirMarksTheIdentity()
    {
        var mir = Compilation.DefaultPipeline == CompilerPipeline.Mir;
        Assert.Equal(mir ? Compilation.CompilerVersion + " [mir]" : Compilation.CompilerVersion, Compilation.CompilerIdentity);
        var legacy = Manifest(CompilerPipeline.Legacy);
        Assert.Equal(legacy.Take(7).Concat(["pipeline", "mir"]).Concat(legacy.Skip(7)), Manifest(CompilerPipeline.Mir));
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
        var images = new Dictionary<OwnershipFailure, HashSet<DiagnosticCode>>();
        foreach (var (failure, code) in OwnershipCodes())
        {
            Assert.Equal(failure == OwnershipFailure.ComparisonLoanConflict, code.IsLoanConflict());
            Assert.Equal(failure == OwnershipFailure.UnprovenOrigin, code.IsUnprovenOrigin());
            images.TryAdd(failure, []);
            images[failure].Add(code);
        }

        Assert.Equal(4, images[OwnershipFailure.ComparisonLoanConflict].Count);
        Assert.Equal(3, images[OwnershipFailure.UnprovenOrigin].Count);
    }

    /// <summary>Lists the code <see cref="OwnershipIssue.Code"/> gives each failure under every combination of the issue's other facts:
    /// a call or another source, an Origin obligation refuted or not, another obligation or none, a reservation or not, an activation or
    /// not.</summary>
    /// <returns>Each failure with each of its codes, repeated.</returns>
    internal static IEnumerable<(OwnershipFailure Failure, DiagnosticCode Code)> OwnershipCodes()
    {
        var c = CompilationTestHelper.Parse("f(1)");
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().First();
        BindingObligation[] obligations =
        [
            new(BindingObligationKind.OriginOutlives, call, default, Longer: new(OriginKind.Projection), Shorter: BoundOrigin.Static),
            new(BindingObligationKind.OriginOutlives, call, default, Longer: BoundOrigin.Static, Shorter: BoundOrigin.Static),
            new(BindingObligationKind.TypeRole, call, default),
        ];
        foreach (var failure in Enum.GetValues<OwnershipFailure>())
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

    private static string[] Manifest(CompilerPipeline pipeline)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = pipeline;
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
