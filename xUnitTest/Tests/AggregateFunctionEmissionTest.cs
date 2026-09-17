// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AggregateFunctionEmissionTest
{
    private const string Echo = "func echo(value: (string, i32)) -> (string, i32) => value\n";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "Echo", Echo + "let saved = echo((\"a\", 1))", string.Empty, "a=1" },
        { "Nested", Echo + "echo(echo((\"a\", 1)))", string.Empty, "a=1" },
        { "Replace", Echo + "var pair = (\"a\", 1)\npair = echo(pair)\npair = echo((\"b\", 2))", string.Empty, "a=1;b=1" },
        { "Loop", Echo + "var i = 0\nwhile i < 3\n    echo((\"a\", i))\n    i += 1", string.Empty, "a=3" },
        { "Forward", Echo + "func forward(value: (string, i32)) -> (string, i32)\n    defer => Console.writeLine(\"cleanup\")\n    return echo(value)\nforward((\"a\", 1))", "cleanup\n", "a=1;cleanup=1" },
        { "Conditional", Echo + "func maybe(value: (string, i32), flag: bool)\n    if flag => echo(value)\nmaybe((\"a\", 1), true)\nmaybe((\"b\", 2), false)", string.Empty, "a=1;b=1" },
        { "Recursion", "func repeat(value: (string, i32), n: i32) -> (string, i32)\n    if n == 0 => return value\n    return repeat(value, n - 1)\nrepeat((\"a\", 1), 3)", string.Empty, "a=1" },
        { "Selection", "func choose(a: (string, i32), b: (string, i32), flag: bool) -> (string, i32)\n    return if flag => a else => b\nchoose((\"a\", 1), (\"b\", 2), true)\nchoose((\"a\", 1), (\"b\", 2), false)", string.Empty, "a=2;b=2" },
        { "Array", "func echo(value: [2 of string]) -> [2 of string] => value\nlet array: [2 of string] = [\"a\", \"b\"]\necho(array)", string.Empty, "a=1;b=1" },
        { "NestedArray", "func echo(value: ([2 of string], (i128, string))) -> ([2 of string], (i128, string)) => value\nlet array: [2 of string] = [\"a\", \"b\"]\nlet value: ([2 of string], (i128, string)) = (array, (42, \"c\"))\necho(value)", string.Empty, "a=1;b=1;c=1" },
        { "Copy", "func echo(value: (i128, u8, f64, char)) -> (i128, u8, f64, char) => value\nlet value: (i128, u8, f64, char) = (42, 200, 2.5, 'a')\necho(value)\necho(value)", string.Empty, string.Empty },
        { "Zero", "func echo(value: [0 of string]) -> [0 of string] => value\nlet empty: [0 of string] = []\necho(echo(empty))", string.Empty, string.Empty },
        { "UnitArray", "func echo(value: [2 of ()]) -> [2 of ()] => value\nlet value: [2 of ()] = [(), ()]\necho(value)\necho(value)", string.Empty, string.Empty },
        { "Mixed", "func choose(first => a: (string, i32), gap: (), z: [0 of string], last => b: (string, i32), n: i8) -> (string, i32)\n    if n == -7 => return b\n    return a\nlet empty: [0 of string] = []\nchoose(last: (\"b\", 2), n: -7, z: empty, gap: Console.writeLine(\"gap\"), first: (\"a\", 1))", "gap\n", "a=1;b=1;gap=1" },
        { "Shared", "func choose(text: ref/string, other: ref/string, value: (string, i32)) -> (string, i32)\n    if text == other => return value\n    return (\"bad\", 0)\nlet text = \"test\"\nchoose(text, text, (\"a\", 1))\nConsole.writeLine(text)", "test\n", "a=1;bad=0;test=1" },
        { "Abandoned", Echo + "func take(value: (string, i32), gap: ()) => ()\nfunc f()\n    take(echo((\"a\", 1)), (return))\nf()", string.Empty, "a=1" },
        { "NeverArgument", Echo + "func f()\n    echo((return))\nf()\nConsole.writeLine(\"done\")", "done\n", "done=1" },
        { "Payload", Echo + "let nested = (echo((\"a\", 1)), \"b\")", string.Empty, "a=1;b=1" },
        { "Match", Echo + "func f() -> (string, i32)\n    return match true\n        _ => echo((\"a\", 1))\n        true => echo((\"b\", 2))\nf()", string.Empty, "a=1;b=0" },
        { "LoopResult", Echo + "func f() -> (string, i32)\n    return loop => exit echo((\"a\", 1))\nf()", string.Empty, "a=1" },
        { "Deferred", Echo + "var n = 0\nloop\n    defer => echo((\"a\", 1))\n    n += 1\n    if n < 3 => continue\n    exit", string.Empty, "a=3" },
        { "ArrayReturn", "func make() -> [2 of string] => [\"a\", \"b\"]\nlet value = make()", string.Empty, "a=1;b=1" },
        { "ZeroReturn", "func make() -> [0 of string] => []\nmake()", string.Empty, string.Empty },
        { "ParameterLoop", Echo + "func f(value: (string, i32), flag: bool)\n    var n = 0\n    loop\n        n += 1\n        if n < 3 => continue\n        if flag => echo(value)\n        exit\nf((\"a\", 1), true)\nf((\"b\", 2), false)", string.Empty, "a=1;b=1" },
        { "Unused", Echo + "Console.writeLine(\"done\")", "done\n", "done=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void WholeValuesCrossFunctionBoundaries(string name, string source, string stdout, string destruction)
    {
        name = "AggregateFunction" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        if (destruction.Length != 0)
        {
            StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, destruction);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedArgumentAcquisitionAndParameterCleanupHaveDifferentOrders(bool transfer)
    {
        var source = "func take(first: (string, i32), second: (string, i32), gap: ()) => ()\nfunc f()\n    take(second: (\"second\", 2), first: (\"first\", 1), gap: " + (transfer ? "(return)" : "()") + ")\nf()";
        var name = "AggregateFunctionOrder" + transfer;
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty);
        int[] order = transfer ? [0, 1] : [1, 0];
        StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, "first=1;second=1", order: order);
    }

    [Fact]
    public void ReturnSecuresTheValueBeforeDeferredReplacement()
    {
        const string Source = "func f() -> (string, i32)\n    var value = (\"before\", 1)\n    defer => value = (\"after\", 2)\n    return value\nf()";
        var ir = ScalarEmissionTest.EmitFixture("AggregateFunctionSnapshot", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateFunctionSnapshot", Source, ir, string.Empty, "before=1;after=1", order: [1, 0]);
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[1];
        var function = module.GetFunction(1);
        var delivery = Assert.Single(body.Deliveries, x => body.IsReachable(x.Operation));
        var secured = function.Instructions.FindIndex(x => x.Operation == delivery.Write && x.Opcode == EmissionOpcode.TransferAggregate);
        var cleanup = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.DestroyAggregate);
        var ret = function.Instructions.FindIndex(x => x.Operation == delivery.Operation && x.Opcode == EmissionOpcode.ReturnVoid);
        Assert.True(secured >= 0 && cleanup > secured && ret > cleanup);
    }

    [Theory]
    [InlineData("Argument", "func f(value: (string, i32), n: i32) => ()\nf((\"held\", 1), 2147483647 + 1)", 2, 16)]
    [InlineData("Body", "func f(value: (string, i32)) -> (string, i32)\n    var n = 2147483647\n    n += 1\n    return value\nf((\"held\", 1))", 3, 5)]
    [InlineData("Return", "func f(value: (string, i32)) -> (string, i32)\n    defer\n        var n = 2147483647\n        n += 1\n    return value\nf((\"held\", 1))", 4, 9)]
    public void AbortDoesNotUnwindOrDeliver(string name, string source, int line, int column)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        name = "AggregateFunctionAbort" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, "held=0", 1, stderr);
    }

    [Fact]
    public void DivergentCleanupCannotReturnOrForwardTheCallersResultSlot()
    {
        const string Source = Echo + "func f(value: (string, i32)) -> (string, i32)\n    defer => loop => ()\n    return echo(value)\nf((\"held\", 1))";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Operations.Any(o => o.Source is LoopKoto));
        var function = module.GetFunction(c.Ownership.Bodies.ToList().IndexOf(body));
        Assert.DoesNotContain(function.Instructions, x => x.Opcode is EmissionOpcode.ReturnVoid or EmissionOpcode.ReturnScalar or EmissionOpcode.DestroyAggregate);
        Assert.All(body.Deliveries, x => Assert.False(body.IsReachable(x.Operation)));
        var call = Assert.Single(function.Instructions, x => x.Callee?.ResultSlot == true);
        var result = function.GetOperands(call)[0];
        Assert.Equal(EmissionOperandKind.SlotAddress, function.SlotAddresses[(int)result.Value].Kind);
        var ir = ScalarEmissionTest.EmitFixture("AggregateFunctionDivergent", Source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Fact]
    public void LogicalNamesAndIncomingAddressesDoNotRequireExtraSlots()
    {
        const string Source = "func f(a: (string, i32), gap: (), empty: [0 of string], b: (string, i32)) -> (string, i32) => b\nlet empty: [0 of string] = []\nf((\"a\", 1), (), empty, (\"b\", 2))";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(1);
        Assert.Equal(new[] { "ret", "a0", "a3" }, function.Abi.Parameters.Select(x => x.Name));
        Assert.Equal(2, function.SlotAddresses.Count(x => x.Kind == EmissionOperandKind.Argument));
        Assert.Single(function.SlotAddresses, x => x.Kind == EmissionOperandKind.ReturnAddress);
        Assert.All(function.Slots, x => Assert.Equal(EmissionOperandKind.SlotAddress, function.SlotAddresses[x.Place].Kind));
        Assert.DoesNotContain(function.Abi.Parameters, x => x.Attributes.Length != 0);
    }

    [Fact]
    public void ZeroByteArgumentsAndResultsKeepLogicalOwnershipWithoutStorage()
    {
        const string Source = "func echo(value: [0 of string]) -> [0 of string] => value\nlet empty: [0 of string] = []\necho(echo(empty))";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(1);
        Assert.Empty(function.Abi.Parameters);
        Assert.False(function.Abi.ResultSlot);
        Assert.Empty(function.Slots);
        Assert.Contains(function.Instructions, x => x.Opcode == EmissionOpcode.ReturnVoid);
        var body = c.Ownership.Bodies[0];
        Assert.Equal(2, body.Operations.Count(x => x.Kind == OwnershipOperationKind.CallEntry));
        foreach (var call in body.Operations.Select((op, id) => (op, id)).Where(x => x.op.Kind == OwnershipOperationKind.Call))
        {
            Assert.NotEqual(-1, call.op.Place);
            Assert.Equal(PlaceState.None, body.GetInputState(call.id, call.op.Place) & PlaceState.MayInit);
            Assert.Equal(OwnershipOperationKind.Produce, body.Operations[call.id + 1].Kind);
        }

        Assert.Empty(module.GetFunction(0).Slots);
        var invalid = MinimalEmissionTest.Analyze(Source + "\necho(empty)");
        Assert.False(invalid.Ownership.Result.IsVerified);
    }

    [Fact]
    public void CopyArgumentsUseIndependentAcquiredStorage()
    {
        const string Source = "func echo(value: (i32, i32)) -> (i32, i32) => value\nlet value = (1, 2)\necho(value)\necho(value)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var function = module.GetFunction(0);
        var local = Assert.Single(body.Places, x => x.Kind == OwnershipPlaceKind.Local);
        var acquisitions = body.Operations.Select((op, id) => (op, id)).Where(x => x.op.Kind == OwnershipOperationKind.Consume && x.op.Place == local.Id).ToArray();
        Assert.Equal(2, acquisitions.Length);
        foreach (var acquisition in acquisitions)
        {
            Assert.Equal(AcquisitionKind.Copy, acquisition.op.Acquisition);
            Assert.Contains(function.Instructions, x => x.Operation == acquisition.id && x.Opcode == EmissionOpcode.TransferAggregate && x.Place == acquisition.op.Input && x.Constant == local.Id);
        }
    }

    [Fact]
    public void ConditionalParameterFlagStartsAtProduceAndCannotBeOmitted()
    {
        var c = MinimalEmissionTest.Analyze(Echo + "func f(value: (string, i32), flag: bool)\n    if flag => echo(value)\nf((\"a\", 1), true)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Parameters.Count == 2);
        var function = module.GetFunction(c.Ownership.Bodies.ToList().IndexOf(body));
        var place = Assert.Single(function.LiveFlags);
        Assert.Equal(OwnershipPlaceKind.Parameter, body.Places[place].Kind);
        var produce = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce && x.Place == place);
        var index = function.Instructions.FindIndex(x => x.Operation == produce && x.Opcode == EmissionOpcode.StoreLiveFlag && x.Constant == 1);
        Assert.True(index >= 0);
        var scratch = new int[body.Places.Count + body.Operations.Count];
        Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
        function.Instructions.RemoveAt(index);
        Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingNormalInitializationIsRejectedEvenForZeroByteResults(bool zero)
    {
        var source = zero ? "func echo(value: [0 of string]) -> [0 of string] => value\nlet empty: [0 of string] = []\necho(empty)" : Echo + "echo((\"a\", 1))";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var call = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call);
        body.OperationStorage[call + 1] = body.Operations[call + 1] with { Kind = OwnershipOperationKind.Read };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("argument")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void InconsistentFunctionPlansAreRejectedAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Echo + "echo((\"a\", 1))");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var call = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call);
        if (defect == "result")
        {
            body.OperationStorage[call] = body.Operations[call] with { Place = -1 };
        }
        else if (defect == "argument")
        {
            body.OperationStorage[call - 1] = body.Operations[call - 1] with { Place = body.Operations[call].Place };
        }
        else
        {
            body = c.Ownership.Bodies[1];
            if (defect == "return")
            {
                body.Deliveries[0] = body.Deliveries[0] with { Write = -1 };
            }
            else
            {
                var produce = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce);
                body.OperationStorage[produce] = body.Operations[produce] with { Source = body.Function };
            }
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void PhysicalSignatureCacheReusesShapesWithoutRetainingSyntax()
    {
        var pool = new FunctionAbiPool();
        var layouts = new AggregateLayoutPool();
        var tree = Populate(pool, layouts, out var first);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(tree.IsAlive);
        var c = MinimalEmissionTest.Analyze(Echo.Replace("echo", "renamed") + "()");
        Assert.Same(first, pool.Get(0, c.Ownership.Bodies[1].Function, layouts));
        var empty = MinimalEmissionTest.Analyze("func f(value: [0 of string]) -> [0 of string] => value\n()");
        var omitted = pool.Get(0, empty.Ownership.Bodies[1].Function, layouts);
        Assert.NotSame(first, omitted);
        Assert.Empty(omitted.Parameters);
        GC.KeepAlive(layouts);
    }

    [Fact]
    public void WarmBorrowedAndAggregateFunctionPreparationAllocatesNothing()
    {
        const string Source = "func f(text: ref/string, value: (string, i32)) -> (string, i32) => value\nvar n = 0\nlet text = \"text\"\nloop\n    defer => f(text, (\"a\", n))\n    n += 1\n    if n < 3 => continue\n    exit";
        var c = MinimalEmissionTest.Analyze(Source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var success = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Bind().IsComplete;
        }

        var bindingBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Binding.CheckStartup(OutputKind.Application);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Populate(FunctionAbiPool pool, AggregateLayoutPool layouts, out FunctionAbi abi)
    {
        var c = MinimalEmissionTest.Analyze(Echo + "()");
        var function = c.Ownership.Bodies[1].Function;
        abi = pool.Get(0, function, layouts);
        layouts.Clear();
        return new WeakReference(function);
    }
}
