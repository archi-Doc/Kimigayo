// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class VirtualOverrideContractTest
{
    private const string Original = "open struct Base<T>\n    public virtual func choose(self: objref/Self, value: ref/T during source) -> ref/T during source => value\nstruct Derived : Base<i32>\n    ";

    [Theory]
    [InlineData("")]
    [InlineData(" during source")]
    public void OmittedAndWrittenOriginsKeepTheOriginalResultDependency(string annotation)
    {
        var c = MinimalEmissionTest.Analyze(Original + "override func choose(self: objref/Self, value: ref/i32" + annotation + ") -> ref/i32" + annotation + " => value\n()");
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out _));
        Assert.Same(implementation.Parameters[1].Type.BoundType!.Origin, implementation.BoundSymbol!.Type!.Origin);
        Assert.NotSame(implementation.Parameters[0].Type.BoundType!.Origin, implementation.BoundSymbol.Type.Origin);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
    }

    [Theory]
    [InlineData(" during other", " during other")]
    [InlineData(" during static", " during static")]
    [InlineData(" during source", " during static")]
    public void RenamingOrNarrowingTheOriginalOriginsIsAContractError(string input, string result)
    {
        var c = MinimalEmissionTest.Analyze(Original + "override func choose(self: objref/Self, value: ref/i32" + input + ") -> ref/i32" + result + " => value\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.OverrideContractMismatch_Kd);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedOriginInheritanceReusesStorage()
    {
        var c = MinimalEmissionTest.Analyze(Original + "override func choose(self: objref/Self, value: ref/i32) -> ref/i32 => value\n()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
    }

    [Theory]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void PlaceResultsAndRepeatedInputBindersUseTheOriginalDependency(string receiver)
    {
        var source = "open struct Base\n    public virtual func choose(self: " + receiver + "/Self, a: ref/i32 during source, b: ref/i32 during source) -> place ref/i32 during source => a@follow\nstruct Derived : Base\n    override func choose(self: " + receiver + "/Self, a => x: ref/i32, b => y: ref/i32) -> place ref/i32 => x@follow\n()";
        var c = MinimalEmissionTest.Analyze(source);
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        var origin = implementation.Parameters[1].Type.BoundType!.Origin;
        Assert.Same(origin, implementation.Parameters[2].Type.BoundType!.Origin);
        Assert.Same(origin, implementation.BoundSymbol!.Type!.Origin);
        Assert.Equal(FunctionResultMode.PlaceRef, Binding.ResultModeOf(implementation.ReturnType));
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
    }

    [Fact]
    public void InheritanceDoesNotHideUnrelatedBodyErrors()
    {
        var c = MinimalEmissionTest.Analyze(Original + "override func choose(self: objref/Self, value => renamed: ref/i32) -> ref/i32\n        let independent = missing\n        return renamed\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd && x.Node is IdentifierNameKoto { IdentifierName: "missing" });
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd && x.Node is IdentifierNameKoto { IdentifierName: "renamed" });
    }

    [Fact]
    public void AnUnresolvedOriginalResultDoesNotInventAContractMismatch()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> Missing => 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2\n()");
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd");
        Assert.DoesNotContain(TestDiagnostics.Of(c), x => x.Code == "OverrideContractMismatch_Kd");
    }
}
