// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualOverrideConditionTest(ITestOutputHelper output)
{
    private const string Helper = "group Operations\n    public func duplicate<V>(value: ref/V) -> V\n        V is Copy\n        return value@follow\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOverrideBodyInheritsTheOriginalConditionalPremise(bool block)
    {
        var original = block
            ? "contract Marker\nopen struct Base<T>\n    Self is Marker when T is Copy\n        public virtual func copy(self: objref/Self, value: ref/T) -> T => Operations.duplicate(value)\n"
            : "open struct Base<T>\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return Operations.duplicate(value)\n";
        var c = MinimalEmissionTest.Analyze(Helper + original + "struct Derived<U> : Base<U>\n    override func copy(self: objref/Self, value: ref/U) -> U => Operations.duplicate(value)\n()");
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
        Assert.True(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AContainingConditionMustFollowFromTheOriginalContract(bool inherited)
    {
        var condition = inherited ? "        T is Copy\n" : string.Empty;
        var c = MinimalEmissionTest.Analyze("contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32\n" + condition + "        return 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n()");
        if (inherited)
        {
            Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
        }
        else
        {
            c.Binding.ReportDiagnostics();
            Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnprovenOverrideCondition_Kd");
        }
    }

    [Fact]
    public void EnclosingDerivedConstraintsCanEstablishTheCondition()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived<U> : Base<U>\n    U is Copy\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
    }

    [Fact]
    public void InheritedPremisesFollowTheCompleteBoundBasePath()
    {
        var c = MinimalEmissionTest.Analyze(Helper + "open struct Base<T>\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return Operations.duplicate(value)\nopen struct Middle<V> : Base<V>\nstruct Derived<U> : Middle<U>\n    override func copy(self: objref/Self, value: ref/U) -> U => Operations.duplicate(value)\n()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
    }

    [Fact]
    public void DifferentConditionsDoNotAllowDuplicateImplementations()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\ncontract Another\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n    Self is Another when U is Owned\n        override func read(self: objref/Self) -> i32 => 3\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateOverride_Kd);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenOverrideCondition_Kd);
    }

    [Fact]
    public void ClosedInapplicabilityIsNotAContradictoryDefinitionPremise()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        return 1\nstruct Derived : Base<string>\n    override func read(self: objref/Self) -> i32 => 2\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Binding.Issues);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void EditingAnOriginalPremiseRevokesTheInheritedProof()
    {
        const string Source = "contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        return 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
        var donor = MinimalEmissionTest.Analyze(Source.Replace("T is Copy", "T is Owned", StringComparison.Ordinal));
        var original = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsVirtual);
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsVirtual);
        Assert.True(KotoHelper.Replace(original, original.TypeConstraints[0], changed.TypeConstraints[0]));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenOverrideCondition_Kd);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InheritedPremisesReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Helper + "contract Marker\nopen struct Base<T>\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return Operations.duplicate(value)\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func copy(self: objref/Self, value: ref/U) -> U => Operations.duplicate(value)\n()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
    }

    [Fact]
    public void ConditionDiagnosticsIdentifyTheUnprovenClauseInCliAndLsp()
    {
        const string Source = "contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived<U> : Base<U>\n    Self is Marker when U is Copy\n        override func read(self: objref/Self) -> i32 => 2\nlet independent = missing";
        var path = Path.GetFullPath("virtual-override-condition.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "UnprovenOverrideCondition_Kd");
        Assert.Equal("U is Copy", shown.Text);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == shown.Code);
        Assert.Equal(DiagnosticCategory.Proof, record.Category);
        Assert.Contains(record.Related!, x => x.Role == "declaration" && x.Label == "original virtual slot");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("original virtual slot", console.Text, StringComparison.Ordinal);
        Assert.Contains("U is Copy is not implied", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("own block cannot prove it", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
