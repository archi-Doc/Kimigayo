// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class BaseCallSyntaxTest(ITestOutputHelper output)
{
    private const string Base = "open struct Base\n    public func read(self: objref/Self) -> i32 => 1\nstruct Derived : Base\n    ";

    [Theory]
    [InlineData("func read(self: objref/Self) -> i32 => base.read()")]
    [InlineData("func read(self: objref/Self) -> i32 => base.read<i32>(n: 1)")]
    [InlineData("func read(self: objref/Self) -> i32\n        let f = func[self]() -> i32 => base.read()\n        return f()")]
    public void DirectBaseCallsRoundTripWithoutBecomingConstructorCalls(string body)
    {
        var tree = ParseSuccess(Base + body);
        var written = Unparse(tree);
        Assert.Contains("base.read", written, StringComparison.Ordinal);
        AssertValid(Parse(written));
        Assert.All(KotoTree.Walk(tree.RootKoto).Where(x => !ReferenceEquals(x, tree.RootKoto)), x => Assert.NotNull(x.Parent));
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(tree))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        AssertValid(restored);
        Assert.Equal(written, Unparse(restored));
        Assert.All(KotoTree.Walk(tree.RootKoto).OfType<FunctionKoto>(), x => Assert.Null(x.BaseInitializer));
    }

    [Fact]
    public void AValidObjectBaseCallBindsAndEmits()
    {
        var c = MinimalEmissionTest.Analyze(Base + "func readAgain(self: objref/Self) -> i32 => base.read()\n()");
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("func f() => base", "base.name(arguments)")]
    [InlineData("func f() => base.read()", "derived instance function")]
    [InlineData("struct S\n    func f(self: objref/Self) => base.read()", "direct base")]
    [InlineData(Base + "func f(self: objref/Self) => base.read", "base.name(arguments)")]
    [InlineData(Base + "func f(self: objref/Self)\n        func local() => base.read()", "derived instance function")]
    [InlineData(Base + "func f(self: objref/Self) => func() -> i32 => base.read()", "explicit self capture")]
    public void InvalidBaseContextsExplainTheirCause(string source, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source + "\n()");
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "InvalidBaseCall_Kd");
        Assert.Equal("base", error.Text);
        Assert.Contains(expected, error.Label!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func[self]() -> i32 => base.read()")]
    [InlineData("func[self]() -> i32\n            let nested = func[self]() -> i32 => base.read()\n            return nested()")]
    public void ExplicitCapturesRetainTheLexicalBase(string closure)
    {
        var c = MinimalEmissionTest.Analyze(Base.Replace("objref", "ref", StringComparison.Ordinal) + "func readAgain(self: ref/Self) -> i32\n        let operation = " + closure + "\n        return operation()\n()");
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
        var reference = KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x.Akind == KotoKind.BaseReference);
        Assert.Equal(BindingSymbolKind.Capture, reference.SymbolOf()!.Kind);
    }

    [Fact]
    public void AnObjectViewCaptureReachesTheDirectBaseCall()
    {
        var c = MinimalEmissionTest.Analyze(Base + "func readAgain(self: objref/Self) -> i32\n        let operation = func[self]() -> i32 => base.read()\n        return operation()\n()");
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
        var reference = KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x.Akind == KotoKind.BaseReference);
        Assert.Equal(BindingSymbolKind.Capture, reference.SymbolOf()!.Kind);
    }

    [Fact]
    public void ConstructorInitializationKeepsItsOwnNode()
    {
        var tree = ParseSuccess("open struct Base\n    init(n: i32) => ()\nstruct Derived : Base\n    init(n: i32) : base(n) => ()\n    func f(self: objref/Self) => base.read()");
        var constructor = KotoTree.Walk(tree.RootKoto).OfType<FunctionKoto>().Single(x => x.BaseInitializer is not null);
        Assert.Equal(KotoKind.ConstructorReference, constructor.BaseInitializer!.Method.Akind);
        Assert.Single(KotoTree.Walk(tree.RootKoto), x => x.Akind == KotoKind.BaseReference);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedLexicalBaseCheckingReusesStorage()
    {
        var c = MinimalEmissionTest.Analyze(Base + "func readAgain(self: objref/Self) -> i32 => base.read()\n()");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
        Assert.Empty(c.Binding.Issues);
    }

    [Fact]
    public void InvalidBaseContextIsLocatedInCliAndLspWhileOtherErrorsSurvive()
    {
        const string Source = "func read() -> i32 => base.read()\nlet independent = missing";
        var path = Path.GetFullPath("base-call-context.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        var shown = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "InvalidBaseCall_Kd");
        Assert.Equal("base", shown.Text);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == shown.Code);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("derived instance function", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("derived instance function", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
