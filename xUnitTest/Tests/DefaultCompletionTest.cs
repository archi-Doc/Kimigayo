// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class DefaultCompletionTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SelectedDefaultsAffectCallCompletionWithoutChangingItsType(bool supplied, bool forward)
    {
        const string Declaration = "func f(x: i32 = (loop => continue)) -> i32 => x\n";
        var call = supplied ? "f(3)\n" : "f()\n";
        var c = Parse(forward ? call + Declaration : Declaration + call);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        var invocation = Assert.Single(flow.Nodes.Keys.OfType<InvocationKoto>());
        Assert.Equal(supplied, flow.Nodes[invocation].CanCompleteNormally);
        Assert.NotEqual(ControlFlowType.Never, flow.Nodes[invocation].ExpressionType);
        var function = (FunctionKoto)invocation.BoundCall!.Target.Declaration;
        Assert.True(flow.Nodes[function].CanCompleteNormally);
        Assert.True(c.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("loop => continue")]
    [InlineData("scope: do\n    var n: i32 = loop => continue\n    n = 3\n    exit to scope: n")]
    [InlineData("scope: do\n    defer => loop => ()\n    exit to scope: 3")]
    public void NoncompletingDefaultsSatisfyRequireFailure(string expression)
    {
        var c = Parse("func f(x: i32 = (" + expression + ")) => ()\nfunc caller()\n    require true else => f()\ncaller()");
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        var call = Assert.Single(flow.Nodes.Keys.OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "f");
        Assert.False(flow.Nodes[call].CanCompleteNormally);
    }

    [Theory]
    [InlineData("If", "if f() => Console.writeLine(\"bad\")")]
    [InlineData("While", "while f() => Console.writeLine(\"bad\")")]
    [InlineData("Require", "require f() else => $abort(\"bad\")")]
    [InlineData("Result", "let n = scope: do => exit to scope: f()")]
    public void NoncompletingDefaultConditionsHaveNoRuntimeSuccessor(string name, string use)
        => ScalarEmissionTest.EmitFixture(
            Prefix + name,
            "func f(x: i32 = (loop => continue)) -> bool => true\nConsole.writeLine(\"begin\")\n" + use,
            "begin\n",
            timeoutMilliseconds: 200);

    [Fact]
    public void SuppliedDefaultsAndRequireSuccessStillExecute()
        => ScalarEmissionTest.EmitFixture(
            Prefix + "Supplied",
            "func f(x: i32 = (loop => continue)) -> bool => true\nrequire true else => f()\nif f(3) => Console.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void NestedDefaultsPropagateCompletion()
    {
        var c = Parse("f()\nfunc leaf(x: i32 = (loop => continue)) -> i32 => x\nfunc f(x: i32 = leaf()) -> i32 => x");
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        Assert.All(flow.Nodes.Where(x => x.Key is InvocationKoto), x => Assert.False(x.Value.CanCompleteNormally));
    }

    [Fact]
    public void RecursiveDefaultExpansionRemainsPending()
    {
        var c = Parse("func f(x: i32 = f()) -> i32 => x\nf()");
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.NotEmpty(flow.PendingBinding);
        Assert.False(c.Ownership.Analyze().IsVerified);
    }

    [Fact]
    public void WarmDefaultFlowReanalysisAllocatesNothing()
    {
        var c = Parse("f()\nfunc f(x: i32 = (loop => continue)) -> i32 => x\nf(3)");
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Equal(0, AllocationMeasurement.Measure(() => flow.Reanalyze(c.Kotonoha.RootKoto)));
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        Assert.Single(flow.Nodes, x => x.Key is InvocationKoto && !x.Value.CanCompleteNormally);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
        return c;
    }

#if DEBUG
    private const string Prefix = "DefaultCompletionDebug";
#else
    private const string Prefix = "DefaultCompletionRelease";
#endif
}
