// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class VirtualDeclarationTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("open struct Base\n    public virtual func read(self: objref/Self) -> i32 => 1")]
    [InlineData("struct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2")]
    [InlineData("open struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T) -> i32 => 1")]
    public void ContextualFunctionModifiersRoundTrip(string source)
    {
        var tree = ParseSuccess(source);
        AssertValid(Parse(Unparse(tree)));
        Assert.Contains(source.Contains("override", StringComparison.Ordinal) ? "override" : "virtual", Unparse(tree), StringComparison.Ordinal);
        Assert.All(KotoTree.Walk(tree.RootKoto).Where(x => !ReferenceEquals(x, tree.RootKoto)), x => Assert.NotNull(x.Parent));
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(tree))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        AssertValid(restored);
        Assert.Equal(Unparse(tree), Unparse(restored));
    }

    [Fact]
    public void OrdinaryNamesDoNotBecomeModifiers()
    {
        var tree = ParseSuccess("func virtual() -> i32 => 1\nfunc override() -> i32 => virtual()\nlet value = override()");
        AssertValid(Parse(Unparse(tree)));
    }

    [Theory]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void ValidDeclarationsAreRetainedWithoutStaticExecution(string receiver)
    {
        var source = $"open struct Base<T>\n    public virtual func read(self: {receiver}/Self, value: ref/T) -> i32 => 1\nstruct Derived : Base<i32>\n    override func read(self: {receiver}/Self, value: ref/i32) -> i32 => 2\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
        Assert.Equal(receiver == "objref" ? 0 : 2, c.Binding.Issues.Count);
        Assert.Equal(receiver == "objref", c.Binding.Result.IsComplete);
        Assert.Equal(receiver == "objref", c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.Equal(2, KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Count(x => x.IsVirtual || x.IsOverride));
    }

    [Theory]
    [InlineData("virtual func read() => ()", "direct struct")]
    [InlineData("group G\n    virtual func read() => ()", "direct struct")]
    [InlineData("func outer()\n    virtual func read() => ()", "direct struct")]
    [InlineData("struct Base\n    public virtual func read(self: objref/Self) => ()", "open struct")]
    [InlineData("open struct Base\n    virtual func read(self: objref/Self) => ()", "private")]
    [InlineData("open struct Base\n    private virtual func read(self: objref/Self) => ()", "private")]
    [InlineData("open struct Base\n    public virtual func read(self) => ()", "objref/Self")]
    [InlineData("open struct Base\n    public virtual func read() => ()", "objref/Self")]
    [InlineData("open struct Base\n    public virtual unsafe func read(self: objref/Self) => ()", "safe")]
    [InlineData("open struct Base\n    public unsafe virtual func read(self: objref/Self) => ()", "safe")]
    [InlineData("open struct Base\n    public virtual func read<T>(self: objref/Self) => ()", "function-owned")]
    [InlineData("open struct Base\n    virtual override func read(self: objref/Self) => ()", "combined")]
    [InlineData("struct Derived\n    public override func read(self: objref/Self) => ()", "inherits")]
    [InlineData("struct Derived\n    override func read(self: objref/Self, n: i32 = 1) => ()", "defaults")]
    [InlineData("struct Derived\n    override func read(self: objref/Self, ! n: i32) => ()", "inherits")]
    public void InvalidHeadersExplainTheRuleAtTheModifier(string source, string fact)
    {
        var c = MinimalEmissionTest.Analyze(source + "\n()");
        c.Binding.ReportDiagnostics();
        var diagnostic = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "InvalidVirtualDeclaration_Kd");
        var expected = source.IndexOf("virtual", StringComparison.Ordinal);
        if (expected < 0)
        {
            expected = source.IndexOf("override", StringComparison.Ordinal);
        }

        Assert.Equal(expected, diagnostic.Span.Start);
        Assert.Contains(fact, diagnostic.Label!, StringComparison.Ordinal);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        output.WriteLine(diagnostic.Explanation + "\n" + diagnostic.Label);
    }

    [Fact]
    public void ExcludedDeclarationsAndDuplicateModifiersKeepOrdinarySyntaxRules()
    {
        AssertValid(Parse("#if false\nvirtual func excluded() => ()\nlet value = 1"));
        const string Source = "open struct Base\n    public virtual virtual func read(self: objref/Self) => ()\nstruct Following";
        var tree = Parse(Source);
        var error = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Equal("DuplicateModifier_Kd", error.Code);
        Assert.Equal(Source.LastIndexOf("virtual", StringComparison.Ordinal), error.Span.Start);
        Assert.Contains(tree.RootKoto.NestedContainers, x => x.Name == "Following");
    }

    [Theory]
    [InlineData("effect confined\n        effect preserves results\n        return 1", false)]
    [InlineData("effect confined\n        effect confined\n        return 1", true)]
    public void EffectClausesUseTheCommonDeclarationChecks(string body, bool rejected)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        " + body + "\n()");
        Assert.Equal(rejected, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.InvalidEffectBound_Kd));
        Assert.Equal(!rejected, c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.DoesNotContain(TestDiagnostics.Of(c), x => x.Code == "CheckFaulted_Kd");
    }

    [Theory]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void PreservedResultsCannotDependOnTheReceiverBorrow(string receiver)
    {
        var c = MinimalEmissionTest.Analyze($"open struct Base\n    public virtual func read(self: {receiver}/Self) -> {receiver}/Self\n        effect preserves results\n        return self@{receiver}\n()");
        c.Binding.ReportDiagnostics();
        var diagnostic = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "InvalidEffectBound_Kd");
        Assert.Contains("receiver borrow", diagnostic.Label!, StringComparison.Ordinal);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedDeclarationCheckingReusesStorage()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32 => 1\n()");
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(complete);
        Assert.Empty(c.Binding.Issues);
    }

    [Fact]
    public void ModifierReasonSurvivesCliAndLspAndIndependentErrors()
    {
        const string Source = "open struct Base\n    public virtual func read(self) => ()\nlet independent = missing";
        var path = Path.GetFullPath("virtual-declaration.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == "InvalidVirtualDeclaration_Kd");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("objref/Self", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("objref/Self", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
