// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class StringFunctionEmissionTest
{
    private const string Echo = "func echo(text: string) -> string => text\n";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "Echo", Echo + "writeLine(echo(\"echo\"))", "echo\n", "echo=1" },
        { "Nested", Echo + "writeLine(echo(echo(\"nested\")))", "nested\n", "nested=1" },
        { "Discard", Echo + "echo(\"discard\")", string.Empty, "discard=1" },
        { "Unused", "func unused(text: string) => ()\nunused(\"unused\")", string.Empty, "unused=1" },
        { "Literal", "func make() -> string => \"literal\"\nwriteLine(make())", "literal\n", "literal=1" },
        { "Branch", "func choose(a: string, b: string, c: bool) -> string\n    return if c => a else => b\nwriteLine(choose(\"a\", \"b\", true))\nwriteLine(choose(\"a\", \"b\", false))", "a\nb\n", "a=2;b=2" },
        { "Conditional", "func maybe(text: string, c: bool)\n    if c => writeLine(text)\nmaybe(\"yes\", true)\nmaybe(\"no\", false)", "yes\n", "yes=1;no=1" },
        { "NamedUnit", "func f(a: string, gap: (), b: string, n: i32) -> string\n    writeLine(a)\n    if n == 7 => return b\n    return \"bad\"\nwriteLine(f(b: \"b\", gap: writeLine(\"gap\"), n: 7, a: \"a\"))", "gap\na\nb\n", "a=1;b=1;gap=1;bad=0" },
        { "Replace", Echo + "var text = \"old\"\ntext = echo(text)\ntext = echo(\"new\")\nwriteLine(text)", "new\n", "old=1;new=1" },
        { "Forward", Echo + "func forward(text: string) -> string\n    defer => writeLine(\"cleanup\")\n    return echo(text)\nwriteLine(forward(\"forward\"))", "cleanup\nforward\n", "cleanup=1;forward=1" },
        { "Loop", Echo + "var i = 0\nwhile i < 3\n    echo(\"iteration\")\n    i += 1", string.Empty, "iteration=3" },
        { "Recursion", "func repeat(text: string, n: i32) -> string\n    if n == 0 => return text\n    return repeat(text, n - 1)\nwriteLine(repeat(\"recursive\", 3))", "recursive\n", "recursive=1" },
        { "Snapshot", "func take() -> string\n    var text = \"before\"\n    defer => text = \"after\"\n    return text\nwriteLine(take())", "before\n", "before=1;after=1" },
        { "EarlyArgument", "func callee(a: string, b: string) => writeLine(\"bad\")\nfunc f()\n    callee(\"pending\", (return))\nf()", string.Empty, "pending=1;bad=0" },
        { "Shadow", "func writeLine(text: string) => ()\nwriteLine(\"shadow\")", string.Empty, "shadow=1" },
        { "Local", "public func main()\n    func local(text: string) -> string => text\n    writeLine(local(\"local\"))", "local\n", "local=1" },
        { "Empty", Echo + "writeLine(echo(\"\"))", "\n", "=1" },
        { "UnusedBody", "func unused(text: string) -> string => text\nwriteLine(\"ok\")", "ok\n", "ok=1" },
        { "MixedAbi", "func f(first => a: string, gap: (), n: i8, b: string, flag: bool, last => z: string) -> string\n    if flag and n == -7\n        writeLine(a)\n        writeLine(b)\n        return z\n    return \"bad\"\nwriteLine(f(last: \"z\", b: \"b\", n: -7, gap: (), first: \"a\", flag: true))", "a\nb\nz\n", "a=1;b=1;z=1;bad=0" },
        { "ResultArgument", Echo + "writeLine(echo(if true => echo(\"a\") else => echo(\"b\")))", "a\n", "a=1;b=0" },
        { "LoopResult", Echo + "func f(text: string) -> string\n    var n = 0\n    return loop\n        n += 1\n        if n < 3 => continue\n        exit echo(text)\nwriteLine(f(\"loop\"))", "loop\n", "loop=1" },
        { "DeferredCalls", Echo + "var n = 0\nloop\n    defer => echo(\"deferred\")\n    n += 1\n    if n < 3 => continue\n    exit", string.Empty, "deferred=3" },
        { "ResultAbandoned", Echo + "func take(a: string, b: ()) => ()\nfunc f()\n    take(echo(\"abandoned\"), (return))\nf()", string.Empty, "abandoned=1" },
        { "ParameterLoop", "func f(text: string, c: bool)\n    var n = 0\n    loop\n        n += 1\n        if n < 3 => continue\n        if c => writeLine(text)\n        exit\nf(\"yes\", true)\nf(\"no\", false)", "yes\n", "yes=1;no=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void OwnershipCrossesFunctionBoundaries(string name, string source, string stdout, string destructions)
    {
        name = "StringFunction" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        Assert.DoesNotContain("load %kimi.string", ir);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, destructions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedArgumentsDistinguishCallerAndCalleeDestructionOrder(bool transfer)
    {
        var source = "func choose(first: string, second: string, gap: ()) => ()\nfunc f()\n    choose(second: \"second\", first: \"first\", gap: " + (transfer ? "(return)" : "()") + ")\nf()";
        var name = transfer ? "StringFunctionCallerOrder" : "StringFunctionCalleeOrder";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty);
        int[] order = transfer ? [0, 1] : [1, 0];
        StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, "first=1;second=1", order: order);
        StringEmissionTest.WriteAuditedFixture(name + "WrongOrder", source, ir, string.Empty, "first=1;second=1", 122, string.Empty, order: order.Reverse().ToArray());
    }

    [Theory]
    [InlineData("ArgumentAbort", "func f(text: string, n: i32) => ()\nvar x = 2147483647\nf(\"pending\", x + 1)", 3, 14, "pending=0")]
    [InlineData("CalleeAbort", "func f(text: string) -> string\n    var x = 2147483647\n    x += 1\n    return text\nwriteLine(f(\"held\"))", 3, 5, "held=0")]
    [InlineData("ReturnAbort", "func f(text: string) -> string\n    defer\n        var x = 2147483647\n        x += 1\n    return text\nwriteLine(f(\"secured\"))", 4, 9, "secured=0")]
    public void AbortDoesNotDeliverOrDestroyOwnedValues(string name, string source, int line, int column, string destructions)
    {
        name = "StringFunction" + name;
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, destructions, 1, stderr);
    }

    [Fact]
    public void DivergentDeferHasNoReturnAndDoesNotForwardTheResultSlot()
    {
        const string Source = Echo + "func f(text: string) -> string\n    defer => loop => ()\n    return echo(text)\nwriteLine(f(\"held\"))";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies.Single(x => x.Function.Parameters.Count == 1 && x.Operations.Any(o => o.Source is LoopKoto));
        var function = module.GetFunction(c.Ownership.Bodies.ToList().IndexOf(body));
        Assert.DoesNotContain(function.Instructions, x => x.Opcode is EmissionOpcode.ReturnVoid or EmissionOpcode.ReturnScalar);
        Assert.All(body.Operations.Select((x, id) => (x, id)).Where(x => x.x.Kind == OwnershipOperationKind.Deliver), x => Assert.False(body.IsReachable(x.id)));
        var call = Assert.Single(function.Instructions, x => x.Callee?.ResultSlot == true);
        var result = function.GetOperands(call)[0];
        Assert.Equal(EmissionOperandKind.SlotAddress, function.SlotAddresses[(int)result.Value].Kind);
        var ir = ScalarEmissionTest.EmitFixture("StringFunctionDivergent", Source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Fact]
    public void ParameterProduceExplicitlyInitializesItsFlag()
    {
        var c = MinimalEmissionTest.Analyze("func f(text: string, c: bool)\n    if c => writeLine(text)\nf(\"text\", true)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Parameters.Count == 2);
        var function = module.GetFunction(c.Ownership.Bodies.ToList().IndexOf(body));
        var place = Assert.Single(function.LiveFlags);
        Assert.Equal(OwnershipPlaceKind.Parameter, body.Places[place].Kind);
        var producer = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce && x.Place == place);
        var scratch = new int[body.Places.Count + body.Operations.Count];
        Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
        var index = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.StoreLiveFlag && x.Operation == producer);
        Assert.True(index >= 0);
        var instruction = function.Instructions[index];
        Assert.Equal(1, instruction.Constant);
        function.Instructions.RemoveAt(index);
        Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
        function.Instructions.Insert(index, instruction);
        function.Instructions.Insert(index, instruction);
        Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
        function.Instructions.RemoveAt(index);
        Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
        body.OperationStorage[producer] = body.OperationStorage[producer] with { Kind = OwnershipOperationKind.Read };
        Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
    }

    [Theory]
    [InlineData("produce")]
    [InlineData("result")]
    [InlineData("argument_result")]
    [InlineData("parameter")]
    [InlineData("return_write")]
    public void MalformedStringFunctionPlansPublishNoIrAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Echo + "writeLine(echo(\"text\"))");
        var body = c.Ownership.Bodies.Single(x => x.Function.Parameters.Count == 0);
        var call = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call && ReferenceEquals(x.Source.BoundType, BoundType.String));
        if (defect is "parameter" or "return_write")
        {
            body = c.Ownership.Bodies.Single(x => x.Function.Parameters.Count == 1);
            var id = body.OperationStorage.FindIndex(x => x.Kind == (defect == "parameter" ? OwnershipOperationKind.Produce : OwnershipOperationKind.Deliver));
            if (defect == "parameter")
            {
                body.OperationStorage[id] = body.OperationStorage[id] with { Source = body.Function };
            }
            else
            {
                body.Deliveries[0] = body.Deliveries[0] with { Write = -1 };
            }
        }
        else if (defect == "produce")
        {
            body.OperationStorage[call + 1] = body.OperationStorage[call + 1] with { Kind = OwnershipOperationKind.Read };
        }
        else if (defect == "result")
        {
            body.OperationStorage[call] = body.OperationStorage[call] with { Place = -1 };
        }
        else
        {
            body.OperationStorage[call - 1] = body.OperationStorage[call - 1] with { Place = body.Operations[call].Place };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void ACallCannotOverwriteAnAlreadyInitializedResult()
    {
        var c = MinimalEmissionTest.Analyze(Echo + "let unit = ()\necho(\"text\")");
        var body = c.Ownership.Bodies.Single(x => x.Function.Parameters.Count == 0);
        var call = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call);
        var earlier = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Declare && ReferenceEquals(body.Places[x.Place].Type, BoundType.Unit));
        var literal = body.Operations.First(x => x.Kind == OwnershipOperationKind.Produce && x.Source is StringLiteralKoto);
        Assert.InRange(earlier, 0, call - 1);
        var place = body.Operations[call].Place;
        body.OperationStorage[earlier] = body.OperationStorage[earlier] with { Kind = OwnershipOperationKind.Produce, Place = place, Source = literal.Source };
        body.Solve();
        Assert.Empty(body.Issues);
        Assert.NotEqual(PlaceState.None, body.GetInputState(call, place) & PlaceState.MayInit);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out var error));
        Assert.Contains("already live", error);
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void NoncompletingArgumentDoesNotInitializeAStringCallResult()
    {
        const string Source = Echo + "func f()\n    echo((return))\nf()\nwriteLine(\"done\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies.Single(x => !x.Function.IsGenerated && x.Function.Parameters.Count == 0);
        var call = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call);
        Assert.True(call >= 0);
        Assert.False(body.IsReachable(call));
        Assert.False(body.IsReachable(call + 1));
        var function = module.GetFunction(c.Ownership.Bodies.ToList().IndexOf(body));
        Assert.DoesNotContain(function.Instructions, x => x.Callee?.ResultSlot == true);
        var ir = ScalarEmissionTest.EmitFixture("StringFunctionNeverArgument", Source, "done\n");
        StringEmissionTest.WriteAuditedFixture("StringFunctionNeverArgument", Source, ir, "done\n", "done=1");
    }

    [Fact]
    public void IncomingSlotsAndLogicalNamesHaveNoExtraAllocations()
    {
        var c = MinimalEmissionTest.Analyze("func f(a: string, gap: (), b: string) -> string => b\nwriteLine(f(\"a\", (), \"b\"))");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var function = Enumerable.Range(0, module.FunctionCount).Select(module.GetFunction).Single(x => x.Abi.ResultSlot);
        Assert.Equal(new[] { "ret", "a0", "a2" }, function.Abi.Parameters.Select(x => x.Name));
        Assert.Equal(2, function.SlotAddresses.Count(x => x.Kind == EmissionOperandKind.Argument));
        Assert.Single(function.SlotAddresses, x => x.Kind == EmissionOperandKind.ReturnAddress);
        Assert.All(function.Slots, x => Assert.Equal(EmissionOperandKind.SlotAddress, function.SlotAddresses[x.Place].Kind));
        using var writer = new StringWriter();
        module.WriteIr(writer);
        Assert.Contains("(ptr %ret, ptr %a0, ptr %a2)", writer.ToString());
    }

    [Fact]
    public void WarmStringFunctionPreparationAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Echo + "var i = 0\nwhile i < 3\n    writeLine(echo(echo(\"text\")))\n    i += 1");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var success = true;
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bytes);
    }
}
