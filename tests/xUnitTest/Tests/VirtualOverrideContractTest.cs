// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualOverrideContractTest(ITestOutputHelper output)
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
        Assert.Same(implementation.Parameters[1].Type.TypeOf()!.Origin, implementation.BoundSymbol!.Type!.Origin);
        Assert.NotSame(implementation.Parameters[0].Type.TypeOf()!.Origin, implementation.BoundSymbol.Type.Origin);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(" during other", " during other", DiagnosticCode.OverrideContractMismatch_Kd)]
    [InlineData(" during static", " during static", DiagnosticCode.UnprovenOriginContract_Kd)]
    [InlineData(" during source", " during static", DiagnosticCode.UnprovenOriginContract_Kd)]
    public void RenamingOrNarrowingTheOriginalOriginsIsAContractError(string input, string result, DiagnosticCode expected)
    {
        var c = MinimalEmissionTest.Analyze(Original + "override func choose(self: objref/Self, value: ref/i32" + input + ") -> ref/i32" + result + " => value\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == expected);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedOriginInheritanceReusesStorage()
    {
        var c = MinimalEmissionTest.Analyze(Original + "override func choose(self: objref/Self, value: ref/i32) -> ref/i32 => value\n()");
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(complete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void PlaceResultsAndRepeatedInputBindersUseTheOriginalDependency(string receiver)
    {
        var source = "open struct Base\n    public virtual func choose(self: " + receiver + "/Self, a: ref/i32 during source, b: ref/i32 during source) -> place ref/i32 during source => a@follow\nstruct Derived : Base\n    override func choose(self: " + receiver + "/Self, a => x: ref/i32, b => y: ref/i32) -> place ref/i32 => x@follow\n()";
        var c = MinimalEmissionTest.Analyze(source);
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        var origin = implementation.Parameters[1].Type.TypeOf()!.Origin;
        Assert.Same(origin, implementation.Parameters[2].Type.TypeOf()!.Origin);
        Assert.Same(origin, implementation.BoundSymbol!.Type!.Origin);
        Assert.Equal(FunctionResultMode.PlaceRef, Binding.ResultModeOf(implementation.ReturnType));
        Assert.Equal(receiver == "objref", c.Binding.Result.IsComplete);
        Assert.Equal(receiver == "objref" ? 0 : 2, c.Binding.Issues.Count);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.Unsupported_Kd, x.Code));
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

    [Theory]
    [InlineData("")]
    [InlineData("        origin a outlives b\n")]
    public void OriginalOriginRelationsAreBodyPremises(string repeated)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func choose(self: objref/Self, a: ref/i32, b: ref/i32) -> ref/i32 during b\n        origin a outlives b\n        return a\nstruct Derived : Base\n    override func choose(self: objref/Self, a: ref/i32, b: ref/i32) -> ref/i32 during b\n" + repeated + "        return a\n()");
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.IsVerifiedOriginObligation(new(BindingObligationKind.OriginOutlives, implementation, BindingDeadline.BodyOrigins, Longer: implementation.Parameters[1].Type.TypeOf()!.Origin, Shorter: implementation.Parameters[2].Type.TypeOf()!.Origin)));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("origin a outlives b")]
    [InlineData("origin a == b")]
    [InlineData("origin a == static")]
    public void AnImplementationCannotProveAnAddedRelationUsingItself(string clause)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func choose(self: objref/Self, a: ref/i32, b: ref/i32) -> i32 => 1\nstruct Derived : Base\n    override func choose(self: objref/Self, a: ref/i32, b: ref/i32) -> i32\n        " + clause + "\n        return 2\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenOriginContract_Kd);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "UnprovenOriginContract_Kd");
        Assert.StartsWith("origin ", error.Text, StringComparison.Ordinal);
        Assert.Contains("not proven", error.Label!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" during first", " during second")]
    public void OriginalNamedEqualityIsInherited(string first, string? second = null)
    {
        second ??= first;
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func choose(self: objref/Self, a: ref/i32 during first, b: ref/i32 during second) -> ref/i32 during first\n        origin first == second\n        return a\nstruct Derived : Base\n    override func choose(self: objref/Self, a: ref/i32" + first + ", b: ref/i32" + second + ") -> ref/i32" + first + " => a\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedRelationInheritanceReusesStorage()
    {
        const string Source = "open struct Base\n    public virtual func choose(self: objref/Self, a: ref/i32 during first, b: ref/i32 during second) -> ref/i32 during second\n        origin first outlives second\n        return a\nstruct Derived : Base\n    override func choose(self: objref/Self, a: ref/i32 during first, b: ref/i32 during second) -> ref/i32 during second\n        origin first outlives second\n        return a\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(complete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void OriginProofDiagnosticsRetainTheOriginalContractInCliAndLsp()
    {
        const string Source = "open struct Base\n    public virtual func choose(self: objref/Self, a: ref/i32, b: ref/i32) -> i32 => 1\nstruct Derived : Base\n    override func choose(self: objref/Self, a: ref/i32, b: ref/i32) -> i32\n        origin a outlives b\n        return 2\n()";
        var path = Path.GetFullPath("virtual-origin-contract.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == "UnprovenOriginContract_Kd");
        Assert.Equal(DiagnosticCategory.Proof, record.Category);
        Assert.Contains(record.Related!, x => x.Role == "requirement");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("outlives", console.Text, StringComparison.Ordinal);
        Assert.Contains("original", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("not proven", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
