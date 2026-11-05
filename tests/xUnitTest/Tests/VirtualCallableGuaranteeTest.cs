// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualCallableGuaranteeTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AQuietBodyDoesNotPublishConfined(bool bounded)
    {
        var source = ConfinedProgram(bounded);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(bounded, c.Analysis.IsVerified);
        if (!bounded)
        {
            Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Problems).Code);
        }
    }

    [Fact]
    public void AChangedSlotRevokesTheItemGuarantee()
    {
        var c = MinimalEmissionTest.Analyze(ConfinedProgram(true));
        Assert.True(c.Analysis.IsVerified);
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        var donor = MinimalEmissionTest.Analyze(ConfinedProgram(false));
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        ParseTestHelper.Detach(changed, new CodeBlockKoto(changed.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analyze().IsVerified);
        Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Problems).Code);
    }

    // A4's record at the Callable use (H0b §5.4) has no label, Note or related location yet (H0a §4.1): Binding's "public effect
    // guarantee" label and its bound and original-slot roles are lost until A4 publishes them.
    [Fact]
    public void TheItemFailureReachesCliAndLspWithoutEvidence()
    {
        var path = Path.GetFullPath("virtual-item-effects.kimi");
        var c = MinimalEmissionTest.Analyze(ConfinedProgram(false), path);
        c.ReportAnalysis();
        var shown = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnsatisfiedEffectBound_Kd", shown.Code);
        Assert.Equal("Base.read", shown.Text);
        Assert.Null(shown.Label);
        Assert.Null(shown.Note);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Null(record.Related);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Equal(record.Message, sent.Message);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WarmItemChecksReuseContractOperands(bool bounded)
    {
        var c = MinimalEmissionTest.Analyze(ConfinedProgram(bounded));
        Assert.Equal(bounded, c.Analysis.IsVerified);
        var consistent = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => consistent &= c.Analyze().IsVerified == bounded));
        Assert.True(consistent);
    }

    private static string ConfinedProgram(bool bounded)
        => "open struct Base\n    public virtual func read(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\nfunc accept<F>(f: ref/F) -> ()\n    F is Callable<(objref/Base) -> i32>\n        effect confined\n    return\npublic func main() => accept(Base.read)";
}
