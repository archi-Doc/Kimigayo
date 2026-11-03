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

    // SPEC 8.11.3 on MIR (H0b §5.4): A4 judges a Contract's bound at the conformance's `Self is C` clause, and Binding reports no effect
    // issue. Getter's requirement forms `objref/Self` without `Self is ObjectPayload`, which Binding rejects independently (SPEC 8.7.3).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicCallsUseThePublicBoundEvenWhenTheBodyIsPure(bool bounded)
    {
        var source = "open struct Base\n    public virtual func get(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\n" + Reader + "()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        Assert.Equal(bounded ? string.Empty : "IncompatibleContractImplementation_Kd Self is Reader", MinimalEmissionTest.Problems(c));
        Assert.Equal(bounded, c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VirtualContractWitnessesNeedThePublicBound(bool bounded)
    {
        var source = "contract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\nopen struct Base\n    Self is Getter\n    public virtual func get(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        Assert.Equal(bounded ? string.Empty : "IncompatibleContractImplementation_Kd Self is Getter", MinimalEmissionTest.Problems(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FunctionItemInvocationUsesThePublicBound(bool bounded)
    {
        var source = "open struct Base\n    public virtual func get(self: objref/Self) -> i32\n" + (bounded ? "        effect confined\n" : string.Empty) + "        return 1\n" + Reader.Replace("=> value.get()", "\n        let operation = Base.get\n        return operation(value)", StringComparison.Ordinal) + "()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        Assert.Equal(bounded ? string.Empty : "IncompatibleContractImplementation_Kd Self is Reader", MinimalEmissionTest.Problems(c));
        Assert.Equal(bounded, c.Accepts);
    }

    [Theory]
    [InlineData("1", "State.count", true)]
    [InlineData("State.count", "2", false)]
    public void DirectBaseEffectsUseTheNearestImplementation(string original, string implementation, bool fails)
    {
        var source = "group State\n    public var count: i32 = 0\ncontract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\nopen struct A\n    public virtual func read(self: objref/Self) -> i32 => " + original + "\nopen struct B : A\n    override func read(self: objref/Self) -> i32 => " + implementation + "\nstruct C : B\n    Self is Getter\n    public func get(self: objref/Self) -> i32 => base.read()\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);

        // The mutable group storage is outside generation (P42): the collector's StaticStorage record (DECISIONS R5-M82, R5-M156).
        Assert.Equal((fails ? "IncompatibleContractImplementation_Kd Self is Getter | " : string.Empty) + "Unsupported_Kd public var count: i32 = 0", MinimalEmissionTest.Problems(c));
    }

    [Fact]
    public void ABaseBodyStillUsesDynamicContractsForNestedCalls()
    {
        const string Source = "contract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\nopen struct A\n    public virtual func read(self: objref/Self) -> i32 => self.other()\n    public virtual func other(self: objref/Self) -> i32 => 1\nstruct C : A\n    Self is Getter\n    public func get(self: objref/Self) -> i32 => base.read()\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        Assert.Equal("IncompatibleContractImplementation_Kd Self is Getter", MinimalEmissionTest.Problems(c));
    }

    // A4's record publishes no label, Note or related location yet (H0a §4.1): Binding's call location `value.get()`, its label and its
    // bound and conformance roles are lost until A4 publishes them.
    [Fact]
    public void MissingPublicBoundsAreExplainedAtTheCallInCliAndLsp()
    {
        const string Source = "open struct Base\n    public virtual func get(self: objref/Self) -> i32 => 1\n" + Reader + "let independent = missing";
        var path = Path.GetFullPath("virtual-effect-selection.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        var shown = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "IncompatibleContractImplementation_Kd");
        Assert.Equal("Self is Reader", shown.Text);
        Assert.Null(shown.Label);
        Assert.Null(shown.Note);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == shown.Code);
        Assert.Null(record.Related);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Equal(record.Message, sent.Message);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPublicEffectChecksReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func get(self: objref/Self) -> i32\n        effect confined\n        return 1\n" + Reader + "()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }
}
