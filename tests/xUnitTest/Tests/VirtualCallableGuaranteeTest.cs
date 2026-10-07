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
    [Fact]
    public void UnboundVirtualItemsCheckAllPublicInputsForPreservingEarlierResults()
    {
        const string Source = "open struct Base\n    public virtual func identity(self: objref/Self) -> objref/Self\n        effect confined\n        return self\nfunc accept<F>(f: ref/F) -> ()\n    F is Callable<(objref/Base) -> objref/Base>\n        effect preserves results\n    return\npublic func main() => accept(Base.identity)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("ref", false, true)]
    [InlineData("uniq", false, false)]
    [InlineData("ref", true, true)]
    [InlineData("uniq", true, false)]
    public void SlotBoundsNeverHideTheEffectsOfUnboundInputs(string mode, bool slotPreserves, bool valid)
    {
        var source = "open struct Base\n    public virtual func identity(value: " + mode + "/i32, self: objref/Self) -> ref/i32 during value\n        effect confined\n" + (slotPreserves ? "        effect preserves results\n" : string.Empty) + "        return value\nfunc accept<F>(f: ref/F) -> ()\n    F is Callable<(" + mode + "/i32, objref/Base) -> ref/i32>\n        effect preserves results\n    return\npublic func main() => accept(Base.identity)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
        if (!valid)
        {
            Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AQuietBodyDoesNotPublishConfined(bool bounded)
    {
        var source = ConfinedProgram(bounded);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(bounded, c.Ownership.Result.IsVerified);
        if (!bounded)
        {
            Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
        }
    }

    [Fact]
    public void AChangedSlotRevokesTheItemGuarantee()
    {
        var c = MinimalEmissionTest.Analyze(ConfinedProgram(true));
        Assert.True(c.Ownership.Result.IsVerified);
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        var donor = MinimalEmissionTest.Analyze(ConfinedProgram(false));
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
    }

    [Fact]
    public void ASymbolicItemCannotUseTheSlotGuaranteeAsAnUnboundExclusion()
    {
        const string Source = "open struct Base<T>\n    public virtual func identity(value: uniq/i32, self: objref/Self) -> ref/i32 during value\n        effect confined\n        effect preserves results\n        return value\nfunc accept<T,F>(f: ref/F) -> ()\n    F is Callable<(uniq/i32, objref/Base<T>) -> ref/i32>\n        effect preserves results\n    return\nfunc use<T>() => accept(Base<T>.identity)\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
    }

    [Fact]
    public void TheItemFailureExplainsTheMissingPublicBoundInCliAndLsp()
    {
        var path = Path.GetFullPath("virtual-item-effects.kimi");
        var c = MinimalEmissionTest.Analyze(ConfinedProgram(false), path);
        c.Ownership.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnsatisfiedEffectBound_Kd", shown.Code);
        Assert.Equal("Base.read", shown.Text);
        Assert.Contains("public effect guarantee", shown.Label, StringComparison.Ordinal);
        Assert.Contains("Declare effect confined on the original virtual slot", shown.Advice, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Contains(record.Related!, x => x.Role == "bound");
        Assert.Contains(record.Related!, x => x.Role == "declaration" && x.Label == "original virtual slot");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("public effect guarantee", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("public effect guarantee", sent.Message, StringComparison.Ordinal);
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
        Assert.Equal(bounded, c.Ownership.Result.IsVerified);
        var consistent = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => consistent &= c.Ownership.Analyze().IsVerified == bounded, iterations: 64, warmupIterations: 32));
        Assert.True(consistent);
    }

    private static string ConfinedProgram(bool bounded)
        => "open struct Base\n    public virtual func read(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\nfunc accept<F>(f: ref/F) -> ()\n    F is Callable<(objref/Base) -> i32>\n        effect confined\n    return\npublic func main() => accept(Base.read)";
}
