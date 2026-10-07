// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualOwnershipTest(ITestOutputHelper output)
{
    private const string Shared = "open struct Base\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2\n()";

    [Fact]
    public void SharedBodiesReachOrdinaryOwnershipChecking()
    {
        var c = MinimalEmissionTest.Analyze(Shared);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Bodies, x => x.Function.IsVirtual);
        Assert.Contains(c.Ownership.Bodies, x => x.Function.IsOverride);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
    }

    [Fact]
    public void UnusedOverrideBodiesKeepTheirOrdinaryOwnershipErrors()
    {
        var c = MinimalEmissionTest.Analyze(Shared.Replace("-> i32 => 2", "-> i32\n        let text = \"value\"\n        let moved = text@move\n        Console.writeLine(text)\n        return 2", StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(DiagnosticCode.MovedPlace_Kd, Assert.Single(c.Ownership.Issues).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestructionInUnusedImplementationsMustSatisfyTheOriginalBound(bool inOverride)
    {
        var original = inOverride ? "        return 1" : "        _ = Noisy.init()\n        return 1";
        var implementation = inOverride ? "        _ = Noisy.init()\n        return 2" : "        return 2";
        var source = "struct Noisy\n    public init() => ()\n    drop => Console.writeLine(\"drop\")\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n" + original + "\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32\n" + implementation + "\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
        c.Ownership.ReportDiagnostics();
        Assert.Contains("confined", Assert.Single(TestDiagnostics.Of(c)).Label!, StringComparison.Ordinal);
        var quiet = MinimalEmissionTest.Analyze(source.Replace("Console.writeLine(\"drop\")", "()", StringComparison.Ordinal));
        Assert.True(quiet.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(quiet, null));
    }

    [Theory]
    [InlineData("owner.tail = 9", true)]
    [InlineData("let independent = 9", false)]
    public void VirtualResultsRetainTheWholePayloadLoan(string update, bool conflict)
    {
        var source = "open struct Base\n    public var value: i32 = 1\n    public virtual func view(self: objref/Self) -> ref/i32 => self.value@ref\nstruct Derived : Base\n    public var tail: i32 = 2\n    public init() => ()\nvar owner = Derived.init()@obj\nlet borrowed = owner.view()\n" + update + "\n_ = borrowed";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(!conflict, c.Ownership.Result.IsVerified);
        Assert.Equal(conflict, c.Ownership.Issues.Any(x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd));
    }

    [Fact]
    public void PendingConditionalDispatchHasALocatedUnsupportedRecordAndWritesNoIr()
    {
        var path = Path.GetFullPath("virtual-dispatch-pending.kimi");
        var c = MinimalEmissionTest.Analyze("contract Marker\nopen struct Base<T>\n    Self is Marker when T is Copy\n        public virtual func read(self: objref/Self) -> i32 => 1\n()", path);
        using var ir = new StringWriter();
        Assert.False(c.Emission.WriteIr(ir, out var failure));
        Assert.Empty(ir.ToString());
        c.Emission.ReportFailure(failure);
        var shown = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnsupportedEmission_Kd", shown.Code);
        Assert.Equal("virtual", shown.Text);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(last: DiagnosticPartition.Emission);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Unsupported, record.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("virtual slot-table and dispatch generation", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("no static-call fallback", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void CleanupFailuresClearAfterAnEditAndKeepTheirPublicEvidence()
    {
        const string Source = "struct Noisy\n    public init() => ()\n    drop => Console.writeLine(\"drop\")\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        _ = Noisy.init()\n        return 1\n()";
        var path = Path.GetFullPath("virtual-cleanup-bound.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        Assert.Single(c.Ownership.Issues);
        c.Ownership.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnsatisfiedEffectBound_Kd", shown.Code);
        Assert.Contains("confined", shown.Label!, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Contains(record.Related!, x => x.Role == "bound");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        Assert.Equal(record.Display!.Range, sent.Range);
        Assert.Contains("confined", sent.Message, StringComparison.Ordinal);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        var drop = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsDestructor);
        var donor = MinimalEmissionTest.Analyze(Source.Replace("Console.writeLine(\"drop\")", "()", StringComparison.Ordinal));
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsDestructor);
        Assert.True(KotoHelper.Replace(drop, drop.ExpressionBody!, changed.ExpressionBody!));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        c.Ownership.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("()", true)]
    [InlineData("Console.writeLine(\"drop\")", false)]
    public void WarmCleanupObligationsReuseTheirSummary(string operation, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public init() => ()\n    drop => " + operation + "\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        _ = Item.init()\n        return 1\n()");
        Assert.Equal(accepted, c.Ownership.Result.IsVerified);
        var consistent = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => consistent &= c.Ownership.Analyze().IsVerified == accepted, iterations: 64, warmupIterations: 32));
        Assert.True(consistent);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSharedBodyCheckingReusesOwnershipStorage()
    {
        var c = MinimalEmissionTest.Analyze(Shared);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var verified = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => verified &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.True(verified);
    }
}
