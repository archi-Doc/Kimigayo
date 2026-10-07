// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualOverrideBindingTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("", "override func read(self: objref/Self) -> i32 => 2")]
    [InlineData("public func read(self: objref/Self) -> i32 => 1", "override func read(self: objref/Self) -> i32 => 2")]
    [InlineData("public virtual func read(self: objref/Self, n: i32) -> i32 => n", "override func read(self: objref/Self, n: bool) -> i32 => 2")]
    public void AnOverrideNeedsOneMatchingVirtualTarget(string original, string implementation)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    " + (original.Length == 0 ? "public func other() => ()" : original) + "\nstruct Derived : Base\n    " + implementation + "\n()");
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "MissingOverrideTarget_Kd");
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void BoundOverloadsRemainDistinctWhenTheirInputsCoincide()
    {
        const string Source = "open struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T) -> i32 => 1\n    public virtual func read(self: objref/Self, value: ref/i32) -> i32 => 2\nstruct Derived : Base<i32>\n    override func read(self: objref/Self, value: ref/i32) -> i32 => 3\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "AmbiguousOverrideTarget_Kd");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateImplementationsAcrossFragmentsHaveOneSlot(bool generic)
    {
        const string Source = "open struct Base\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2\nstruct Derived\n    override func read(self: objref/Self) -> i32 => 3\n()";
        var c = MinimalEmissionTest.Analyze(generic ? Source.Replace("struct Base", "struct Base<T>", StringComparison.Ordinal).Replace(": Base", ": Base<i32>", StringComparison.Ordinal) : Source);
        c.Binding.ReportDiagnostics();
        Assert.Single(TestDiagnostics.Of(c), x => x.Code == "DuplicateOverride_Kd");
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOverrideKeepsTheOriginalBoundContractAndOtherOverloads(bool reversed)
    {
        const string Base = "open struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T) -> i32 => 1\n    public func read(self: objref/Self, value: ref/bool) -> i32 => 2\n";
        const string Child = "open struct Middle<U> : Base<U>\nstruct Derived : Middle<i32>\n    override func read(self: objref/Self, value => renamed: ref/i32) -> i32 => 3\n";
        var c = MinimalEmissionTest.Analyze((reversed ? Child + Base : Base + Child) + "()");
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out var selected));
        Assert.True(selected.Slot.Original.IsVirtual);
        Assert.Equal("Base", selected.Slot.DeclaringType.Symbol!.Name);
        Assert.Same(BoundType.I32, selected.Slot.DeclaringType.Components[0]);
        Assert.NotNull(selected.Path?.Parent);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out var rebound));
        Assert.Equal(selected.Slot, rebound.Slot);
        Assert.Same(selected.Path, rebound.Path);
    }

    [Theory]
    [InlineData("objref/Base", true)]
    [InlineData("objref/Self", false)]
    public void OnlyTheReceiverMapsSelfToTheDerivedType(string otherType, bool matches)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self, other: objref/Self) -> i32 => 1\nstruct Derived : Base\n    override func read(self: objref/Self, other: " + otherType + ") -> i32 => 2\n()");
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.Equal(matches, c.Binding.TryGetVirtualOverride(implementation, out _));
        Assert.Equal(!matches, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.MissingOverrideTarget_Kd));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MatchingReusesStorageWithoutRepeatedAllocation()
    {
        const string Source = "open struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T) -> i32 => 1\nstruct Derived : Base<i32>\n    override func read(self: objref/Self, value: ref/i32) -> i32 => 2\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(complete);
        Assert.Empty(c.Binding.Issues);
    }

    [Fact]
    public void AnEditRevokesCorrespondenceAndRechecksTheContract()
    {
        const string Source = "open struct Base\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out _));
        var donor = MinimalEmissionTest.Analyze(Source.Replace("-> i32 => 2", "-> i64 => 2", StringComparison.Ordinal));
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(KotoHelper.Replace(implementation, implementation.ReturnType!, changed.ReturnType!));
        Assert.False(c.Binding.TryGetVirtualOverride(implementation, out _));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.OverrideContractMismatch_Kd);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out var selection));
        Assert.Same(BoundType.I32, selection.Slot.Original.BoundSymbol!.Type);
    }

    [Theory]
    [InlineData("different: i32", "i32", "different", "value", "different")]
    [InlineData("value: i32", "i64", "i64", "i32", "i64")]
    public void ContractDiagnosticsRetainFactsAndLocationsInCliAndLsp(string input, string resultType, string location, string required, string actual)
    {
        var source = "open struct Base\n    public virtual func read(self: objref/Self, value: i32) -> i32 => value\nstruct Derived : Base\n    override func read(self: objref/Self, " + input + ") -> " + resultType + " => 2\nlet independent = missing";
        var path = Path.GetFullPath("virtual-override.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        var diagnostic = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "OverrideContractMismatch_Kd");
        Assert.Equal(source.LastIndexOf(location, StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Contains(required, diagnostic.Label!, StringComparison.Ordinal);
        Assert.Contains(actual, diagnostic.Label!, StringComparison.Ordinal);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == diagnostic.Code);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(required, console.Text, StringComparison.Ordinal);
        Assert.Contains(actual, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains(required, sent.Message, StringComparison.Ordinal);
            Assert.Contains(actual, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
