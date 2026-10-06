// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericClosureDispatchTest
{
    private const string Count = "func count<T>(value: T, depth: i32) -> i32\n    let action = func [value@move, depth] () -> i32 => if depth == 0 => 1 else => count(value@move, depth - 1) + 1\n    return action@move()\n";

    [Fact]
    public void CapturedGenericCallsSelectTheConcreteSpecialization()
    {
        const string Source = "func classify<T>(value: ref/T) -> i32 => 1\nspecialize func classify<i32>(value: ref/i32) -> i32 => 2\nfunc inspect<T>(value: T) -> i32\n    let action = func [value@move] () -> i32 => classify(value@ref)\n    return action()\nrequire inspect(7) == 2 and inspect(true) == 1 else => $abort(\"specialization\")";
        ScalarEmissionTest.EmitFixture("GenericClosureDispatchSpecialization", Source, string.Empty);
    }

    [Fact]
    public void RecursiveClosureCallsReuseTheContextAndDestroyTheFinalValueOnce()
    {
        const string Source = "struct Token\n    drop => Console.writeLine(\"drop\")\n" + Count + "require count(Token.init(), 4) == 5 else => $abort(\"recursion\")\nrequire count(true, 2) == 3 else => $abort(\"second\")\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture("GenericClosureDispatchRecursive", Source, "drop\ndone\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalCaptureMovesKeepExactlyOneOwner(bool move)
    {
        const string Source = "struct Item\n    drop => Console.writeLine(\"drop\")\nfunc consume(value: Item) => ()\nlet value = Item.init()\nlet action = func [value@move] (flag: bool) -> ()\n    if flag => consume(value@move)\naction@move(FLAG)\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture("GenericClosureDispatchConditional" + move, Source.Replace("FLAG", move ? "true" : "false", StringComparison.Ordinal), "drop\ndone\n");
    }

    [Fact]
    public void ConditionalCaptureFlagInitializationCannotBeOmitted()
    {
        var c = MinimalEmissionTest.Analyze("func consume(value: string) => ()\nlet text = \"owned\"\nlet action = func [text@move] (flag: bool) -> ()\n    if flag => consume(text@move)\naction@move(true)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.IsAnonymous);
        var function = module.GetFunction(c.Ownership.Bodies.ToList().IndexOf(body));
        var place = Assert.Single(function.LiveFlags);
        var produce = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce && x.Place == place);
        Assert.Equal(OwnershipValueKind.Capture, body.Values[produce].Kind);
        var index = function.Instructions.FindIndex(x => x.Operation == produce && x.Opcode == EmissionOpcode.StoreLiveFlag && x.Constant == 1);
        Assert.True(index >= 0);
        var scratch = new int[body.Places.Count + body.Operations.Count];
        Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
        function.Instructions.RemoveAt(index);
        Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRecursiveGenericClosurePlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Count + "require count(\"owned\", 4) == 5 else => $abort(\"warm\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
