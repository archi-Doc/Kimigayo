// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

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
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var invocation = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal(supplied ? HirCompletion.Completes : HirCompletion.Diverges, HirTables.CompletionOf(invocation));
        var function = (FunctionKoto)invocation.CallOf()!.Target.Declaration;
        Assert.Equal(HirCompletion.Completes, HirTables.CompletionOf(function.ExpressionBody!));
    }

    [Theory]
    [InlineData("loop => continue")]
    [InlineData("label scope: do\n    var n: i32 = loop => continue\n    n = 3\n    exit to scope n\n")]
    [InlineData("label scope: do\n    defer => loop => ()\n    exit to scope 3\n")]
    public void NoncompletingDefaultsSatisfyRequireFailure(string expression)
    {
        // SPEC 14.11: the default does not complete, so neither does the failure body f(), and A3 records no RequireFallthrough_Kd
        // there; the third default's exit runs its diverging defer, which only the MIR sees (the HIR Completion column has no cleanup).
        var c = Parse("func f(x: i32 = (" + expression + ")) => ()\nfunc caller()\n    require true else => f()\ncaller()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("If", "if f() => Console.writeLine(\"bad\")")]
    [InlineData("While", "while f() => Console.writeLine(\"bad\")")]
    [InlineData("Require", "require f() else => $abort(\"bad\")")]
    [InlineData("Result", "let n = label scope: do => exit to scope f()")]
    public void NoncompletingDefaultConditionsHaveNoRuntimeSuccessor(string name, string use)
        => ScalarEmissionTest.EmitFixture(
            Prefix + name,
            "func f(x: i32 = (loop => continue)) -> bool => true\nConsole.writeLine(\"begin\")\n" + use,
            "begin\n",
            observationMilliseconds: 200);

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
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => Assert.Equal(HirCompletion.Diverges, HirTables.CompletionOf(x)));
    }

    [Fact]
    public void RecursiveDefaultUsesTheResolvedSignature()
    {
        var c = Parse("func f(x: i32 = f()) -> i32 => x\nf()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    private static Compilation Parse(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues));
        return c;
    }

#if DEBUG
    private const string Prefix = "DefaultCompletionDebug";
#else
    private const string Prefix = "DefaultCompletionRelease";
#endif

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void WarmDefaultFlowReanalysisAllocatesNothing()
        {
            var c = Parse("f()\nfunc f(x: i32 = (loop => continue)) -> i32 => x\nf(3)");
            var valid = c.Analyze().IsVerified;
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                c.Bind();
                valid &= c.Analyze().IsVerified;
            }));
            Assert.True(valid, MinimalEmissionTest.Describe(c));
            Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => HirTables.CompletionOf(x) == HirCompletion.Diverges);
        }
    }
}
