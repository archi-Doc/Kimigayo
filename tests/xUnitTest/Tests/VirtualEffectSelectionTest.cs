// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualEffectSelectionTest(ITestOutputHelper output)
{
    private const string Reader = "contract Reader\n    func read(self: ref/Self, value: objref/Base) -> i32\n        effect confined\nstruct Probe\n    Self is Reader\n    public func read(self: ref/Self, value: objref/Base) -> i32 => value.get()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicCallsUseThePublicBoundEvenWhenTheBodyIsPure(bool bounded)
    {
        var source = "open struct Base\n    public virtual func get(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\n" + Reader + "()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(!bounded, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd));
        Assert.Equal(bounded, c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VirtualContractWitnessesNeedThePublicBound(bool bounded)
    {
        var source = "contract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\nopen struct Base\n    Self is Getter\n    public virtual func get(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(!bounded, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FunctionItemInvocationUsesThePublicBound(bool bounded)
    {
        var source = "open struct Base\n    public virtual func get(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\n" + Reader.Replace("=> value.get()", "\n        let operation = Base.get\n        return operation(value)", StringComparison.Ordinal) + "()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(!bounded, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd));
    }

    [Theory]
    [InlineData("1", "State.count", true)]
    [InlineData("State.count", "2", false)]
    public void DirectBaseEffectsUseTheNearestImplementation(string original, string implementation, bool fails)
    {
        var source = "group State\n    public var count: i32 = 0\ncontract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\nopen struct A\n    public virtual func read(self: objref/Self) -> i32 => " + original + "\nopen struct B : A\n    override func read(self: objref/Self) -> i32 => " + implementation + "\nstruct C : B\n    Self is Getter\n    public func get(self: objref/Self) -> i32 => base.read()\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(fails, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd));
    }

    [Fact]
    public void ABaseBodyStillUsesDynamicContractsForNestedCalls()
    {
        const string Source = "contract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\nopen struct A\n    public virtual func read(self: objref/Self) -> i32 => self.other()\n    public virtual func other(self: objref/Self) -> i32 => 1\nstruct C : A\n    Self is Getter\n    public func get(self: objref/Self) -> i32 => base.read()\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void ASharedDynamicReceiverDoesNotInventExclusiveInputEffects()
    {
        const string Source = "open struct Base<T>\n    public let value: T\n    public virtual func touch(self: objref/Self)\n        effect confined\n        return\nstruct Probe {a, b}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let target: objref/Base<ref/i32 during a> during b\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        self.target.touch()\n        return .Some(self.target.value)\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceiverPositionDoesNotHideEarlierArgumentEffects(bool unbound)
    {
        var source = "open struct Base\n    public virtual func touch(value: uniq/i32, self: objref/Self)\n        effect confined\n        return\nstruct Probe {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let target: objref/Base during static\n    let value: uniq/i32 during a\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        " + (unbound ? "Base.touch(self.value, self.target)" : "self.target.touch(self.value)") + "\n        return .Some(self.value)\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        Assert.All(c.Binding.Issues, x => Assert.True(x.Code is DiagnosticCode.IncompatibleContractImplementation_Kd or DiagnosticCode.Unsupported_Kd, x.ToString()));
    }

    [Fact]
    public void MissingPublicBoundsAreExplainedAtTheCallInCliAndLsp()
    {
        const string Source = "open struct Base\n    public virtual func get(self: objref/Self) -> i32 => 1\n" + Reader + "let independent = missing";
        var path = Path.GetFullPath("virtual-effect-selection.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "IncompatibleContractImplementation_Kd");
        Assert.Equal("value.get()", shown.Text);
        Assert.Equal("a virtual call without the required public effect guarantee, which confined excludes", shown.Label);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == shown.Code);
        Assert.Contains(record.Related!, x => x.Role == "bound");
        Assert.Contains(record.Related!, x => x.Role == "conformance");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("virtual call without the required public effect guarantee", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("virtual call without the required public effect guarantee", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPublicEffectChecksReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func get(self: objref/Self) -> i32\n        effect confined\n        return 1\n" + Reader + "()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
    }
}
