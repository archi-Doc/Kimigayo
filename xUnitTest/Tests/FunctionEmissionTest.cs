// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FunctionEmissionTest
{
    private const string Snapshot = "func result() -> i32\n    var x = 1\n    defer => x = 2\n    return x\nif result() == 1 => writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "FunctionSimple", "func add(a: i32, b: i32) -> i32 => a + b\nif add(1, 2) == 3 => writeLine(\"ok\")", "ok\n" },
        { "FunctionMain", "public func main() -> ()\n    defer => writeLine(\"end\")\n    writeLine(\"body\")\n    return\n    writeLine(\"bad\")", "body\nend\n" },
        { "FunctionLocal", "public func main()\n    func twice(x: i32) -> i32 => x * 2\n    if twice(3) == 6 => writeLine(\"ok\")", "ok\n" },
        { "FunctionNamed", "func pair(left => v3: i32, right => p3: i32) -> i32 => v3 * 10 + p3\nvar x = 1\nif pair(right: x++, left: x++) == 21 and x == 3 => writeLine(\"ok\")", "ok\n" },
        { "FunctionNested", "func add(a: i32, b: i32) -> i32 => a + b\nvar x = 1\nlet y = add(x++, add(x++, x++))\nif y == 6 and x == 4 => writeLine(\"ok\")", "ok\n" },
        { "FunctionRecursion", "func fact(n: i32) -> i32\n    if n == 0 => return 1\n    return n * fact(n - 1)\nif fact(6) == 720 => writeLine(\"ok\")", "ok\n" },
        { "FunctionMutual", "func even(n: i32) -> bool => if n == 0 => true else => odd(n - 1)\nfunc odd(n: i32) -> bool => if n == 0 => false else => even(n - 1)\nif even(10) and odd(9) => writeLine(\"ok\")", "ok\n" },
        { "FunctionSnapshot", Snapshot, "ok\n" },
        { "FunctionUnit", "func f(a: (), b: i32, c: ()) -> i32 => b\nif f(writeLine(\"first\"), 7, writeLine(\"last\")) == 7 => writeLine(\"ok\")", "first\nlast\nok\n" },
        { "FunctionBool", "func opposite(b3: bool) -> bool => not b3\nvar x = opposite(false)\nif opposite(opposite(x)) => writeLine(\"ok\")", "ok\n" },
        { "FunctionReturnIf", "func choose(c: bool) -> i32\n    defer => writeLine(\"end\")\n    return if c => 21 / 2 else => 3 % 2\nif choose(true) == 10 and choose(false) == 1 => writeLine(\"ok\")", "end\nend\nok\n" },
        { "FunctionOverload", "func f(x: i32) -> i32 => x\nfunc f(x: bool) -> bool => x\nif f(1) == 1 and f(true) => writeLine(\"ok\")", "ok\n" },
        { "FunctionUnused", "func unused() => 123\npublic func main() => writeLine(\"ok\")", "ok\n" },
        { "FunctionSkipped", "func zero() -> i32 => 1 / 0\nif false => zero()\nif true or zero() == 0 => writeLine(\"ok\")", "ok\n" },
        { "FunctionTransferArgument", "func add(a: i32, b: i32) -> i32 => a + b\nlet x = outer: do\n    defer => writeLine(\"end\")\n    add(1, (inner: do => exit to outer: 7))\nif x == 7 => writeLine(\"ok\")", "end\nok\n" },
        { "FunctionReturnOperand", "func f() -> i32\n    defer => writeLine(\"end\")\n    return (inner: do => return 7)\nif f() == 7 => writeLine(\"ok\")", "end\nok\n" },
        { "FunctionNames", "func 日本語(v3: i32, p3: i32, b3: bool, checked3: i32, a0: i32) -> i32 => if b3 => v3 + p3 + checked3 + a0 else => 0\nif 日本語(1, 2, true, 3, 4) == 10 => writeLine(\"ok\")", "ok\n" },
        { "FunctionDeferredCalls", "func tick() => writeLine(\"tick\")\nfunc f(x: i32) -> i32\n    defer => tick()\n    if x == 1 => return x\n    return 2\nif f(1) + f(0) == 3 => writeLine(\"ok\")", "tick\ntick\nok\n" },
        { "FunctionConditionalTransfer", "func f(a: i32, b: i32) -> i32 => a + b\nlet x = outer: do\n    defer => writeLine(\"end\")\n    exit to outer: f(2, (if false => exit to outer: 9 else => 3))\nif x == 5 => writeLine(\"ok\")", "end\nok\n" },
        { "FunctionTransferThenArgument", "func f(a: i32, b: i32) -> i32 => a + b\nfunc side() -> i32\n    writeLine(\"bad\")\n    return 1\nlet x = outer: do\n    f((inner: do => exit to outer: 7), side())\nif x == 7 => writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsFunctions(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [InlineData("FunctionAbort", "func divide(x: i32) -> i32 => 1 / x\npublic func main()\n    defer => writeLine(\"bad\")\n    divide(0)", "", 1, 31)]
    [InlineData("FunctionArgumentAbort", "func f(x: i32) => writeLine(\"bad\")\ndefer => writeLine(\"bad\")\nf(1 / 0)", "", 3, 3)]
    [InlineData("FunctionDeferredAbort", "func f() -> i32\n    defer => writeLine(\"bad\")\n    defer => 1 / 0\n    return 7\nf()\nwriteLine(\"bad\")", "", 3, 14)]
    public void AbortDoesNotReturnOrUnwind(string name, string source, string stdout, int line, int column)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, 1, $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero\n");

    [Theory]
    [InlineData("FunctionNever", "func spin() -> Never => loop => ()\npublic func main()\n    writeLine(\"begin\")\n    spin()\n    writeLine(\"bad\")")]
    [InlineData("FunctionNeverArgument", "func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => writeLine(\"bad\")\nwriteLine(\"begin\")\nf(1, spin())")]
    [InlineData("FunctionDivergentReturn", "func f() -> i32\n    defer => loop => ()\n    return 1\nwriteLine(\"begin\")\nf()\nwriteLine(\"bad\")")]
    [InlineData("FunctionNeverFirstArgument", "func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => writeLine(\"bad\")\nfunc side() -> i32\n    writeLine(\"bad\")\n    return 1\nwriteLine(\"begin\")\nf(spin(), side())")]
    [InlineData("FunctionNeverExpression", "func spin() -> Never => loop => ()\nfunc f() -> i32 => spin()\nwriteLine(\"begin\")\nf()")]
    public void NonterminationHasNoFabricatedReturn(string name, string source)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, "begin\n", timeoutMilliseconds: 200);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Theory]
    [InlineData("let x = 1\nfunc capture() -> i32 => x\ncapture()")]
    [InlineData("public func main()\n    let x = 1\n    func capture() -> i32 => x\n    capture()")]
    [InlineData("group G\n    func f() => ()\n()")]
    [InlineData("func f(x?: i32 = 1) => ()\n()")]
    [InlineData("func f<T>() => ()\n()")]
    [InlineData("func f(x: string) => ()\n()")]
    [InlineData("func unused() => 1 << 1\n()")]
    [InlineData("public func main() -> i32 => 0")]
    [InlineData("public func main() => ()\n()")]
    [InlineData("func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => ()\nvar x = 1\nf(spin(), x++)")]
    [InlineData("func f(a: i32, b: i32) -> i32 => a + b\nvar x = 1\nlet y = outer: do\n    f((inner: do => exit to outer: 7), x++)")]
    public void UnsupportedSelectedBodiesAndInvalidStartupProduceNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void ParametersUseSsaAndOnlyMainIsCalledAtStartup()
    {
        var c = MinimalEmissionTest.Analyze("func f(v3: i32, ignored: (), checked3: bool) -> i32 => if checked3 => v3 else => 0\npublic func main()\n    if f(3, (), true) == 3 => writeLine(\"ok\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Equal(3, module.FunctionCount);
        var f = module.GetFunction(0);
        Assert.Empty(f.Slots);
        Assert.Equal(new[] { "a0", "a2" }, f.Abi.Parameters.Select(x => x.Name));
        Assert.Same(module.GetFunction(1).Abi, module.GetFunction(2).Instructions[0].Callee);
        using var writer = new StringWriter();
        module.WriteIr(writer);
        Assert.DoesNotContain("@__kimi_entry_body", writer.ToString());
    }

    [Fact]
    public void NeverCallsAndDivergentCleanupHaveNoReturnDelivery()
    {
        var c = MinimalEmissionTest.Analyze("func spin() -> Never => loop => ()\nfunc take(x: i32) -> i32 => x\npublic func main()\n    take(spin())\n    writeLine(\"bad\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var main = module.GetFunction(2);
        var call = Assert.Single(main.Instructions, x => x.Opcode == EmissionOpcode.Call);
        Assert.True(call.Callee!.NoReturn);
        Assert.Equal(EmissionOpcode.Unreachable, main.Instructions[^1].Opcode);
        Assert.DoesNotContain(main.Instructions, x => x.Opcode is EmissionOpcode.ReturnVoid or EmissionOpcode.ReturnScalar or EmissionOpcode.Phi);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "main");
        var invoke = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call && ((InvocationKoto)x.Source).BoundCall!.ReturnType == BoundType.Never);
        Assert.All(body.Edges.Where(x => x.From == invoke), x => Assert.Equal(OwnershipEdgeKind.Abort, x.Kind));

        c = MinimalEmissionTest.Analyze("func f() -> i32\n    defer => loop => ()\n    return 1\nf()");
        Assert.True(c.Emission.TryPrepare(out module, out error), error);
        body = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "f");
        Assert.All(body.Deliveries, x => Assert.False(body.IsReachable(x.Operation)));
        var f = module.GetFunction(1);
        Assert.False(f.Abi.NoReturn); // A fixed i32 signature does not become Never.
        Assert.DoesNotContain(f.Instructions, x => x.Opcode is EmissionOpcode.ReturnVoid or EmissionOpcode.ReturnScalar);
    }

    [Theory]
    [InlineData("delivery")]
    [InlineData("write")]
    [InlineData("value")]
    [InlineData("parameter")]
    [InlineData("call-result")]
    [InlineData("argument-value")]
    [InlineData("call-entry")]
    [InlineData("mapping")]
    public void MalformedFunctionPlansFailBeforeWritingAndRecover(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32) -> i32\n    var y = x\n    defer => y = 9\n    return y\nif f(3) == 3 => writeLine(\"ok\")");
        Assert.True(c.Emission.Validate(out var error), error);
        var f = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "f");
        var caller = Assert.Single(c.Ownership.Bodies, x => x.Function.IsGenerated);
        var call = caller.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call && x.Place >= 0);
        var entry = call - 1;
        if (mutation == "delivery")
        {
            f.Deliveries.Clear();
        }
        else if (mutation == "write")
        {
            f.Deliveries[0] = f.Deliveries[0] with { Write = -1 };
        }
        else if (mutation == "value")
        {
            f.Deliveries[0] = f.Deliveries[0] with { Value = f.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant && x.Constant == 9) };
        }
        else if (mutation == "parameter")
        {
            var id = f.Values.FindIndex(x => x.Kind == OwnershipValueKind.Parameter);
            f.Values[id] = f.Values[id] with { Constant = 1 };
        }
        else if (mutation == "call-result")
        {
            caller.Values[call] = default;
        }
        else if (mutation == "argument-value")
        {
            caller.ValueOperands[caller.Values[entry].Start] = call;
        }
        else if (mutation == "call-entry")
        {
            caller.OperationStorage[entry] = caller.Operations[entry] with { Kind = OwnershipOperationKind.Produce };
        }
        else
        {
            var plan = ((InvocationKoto)caller.Operations[call].Source).BoundCall!;
            plan.Set(plan.Target, plan.ReturnType, null, [1], [], operations: plan.ArgumentOperations);
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out error));
        Assert.NotNull(error);
        Assert.Empty(writer.ToString());
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out error), MinimalEmissionTest.Describe(c, error));
    }

    [Fact]
    public void AbiPoolReusesSignaturesWithoutRetainingSyntax()
    {
        var pool = new FunctionAbiPool();
        var old = RegisterTemporary(pool);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(old.Source.TryGetTarget(out _));

        var c = MinimalEmissionTest.Analyze("func renamed(x: i32) -> i32 => x\n()");
        var function = Assert.Single(c.Ownership.Bodies, x => !x.Function.IsGenerated).Function;
        var abi = pool.Get(0, function);
        Assert.Same(old.Abi, abi);
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.Same(abi, pool.Get(0, function));
        Assert.Equal("__kimi_f0", abi.Name);
        var changed = MinimalEmissionTest.Analyze("func renamed(x: bool) -> bool => x\n()");
        Assert.NotSame(abi, pool.Get(0, Assert.Single(changed.Ownership.Bodies, x => !x.Function.IsGenerated).Function));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmFunctionAnalysisAndWritingAllocateNothing(bool parameters)
    {
        var c = MinimalEmissionTest.Analyze(parameters ? "func f(a: i32, b: (), c: bool) -> i32 => if c => a else => 0\nvar x = 0\nwhile x < 100\n    x += f(c: true, b: (), a: 1)" : Snapshot);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(valid);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference<FunctionKoto> Source, FunctionAbi Abi) RegisterTemporary(FunctionAbiPool pool)
    {
        var c = MinimalEmissionTest.Analyze("func temporary(x: i32) -> i32 => x\n()");
        var function = Assert.Single(c.Ownership.Bodies, x => !x.Function.IsGenerated).Function;
        return (new(function), pool.Get(0, function));
    }
}
