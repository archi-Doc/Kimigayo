// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ResultEmissionTest
{
    internal const string Nested = "var i = 0\nwhile i < 1000000\n    let x = if i < 5 => (if true => i else => -1) else => i\n    if x != i => writeLine(\"bad\")\n    i += 1\nif i == 1000000 => writeLine(\"ok\")";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "ResultIf", "var c = false\nlet x = if c => 1 else => 2\nif x == 2 => writeLine(\"ok\")" },
        { "ResultElseIf", "var c = 2\nlet x = if c == 0 => 10 else if c == 1 => 20 else => 30\nif x == 30 => writeLine(\"ok\")" },
        { "ResultBool", "let x = if false => false else => true\nif x and true => writeLine(\"ok\")" },
        { "ResultYield", "let x = if true\n    yield 1\nelse\n    yield 2\nif x == 1 => writeLine(\"ok\")" },
        { "ResultDo", "let x = do => 3\nif x == 3 => writeLine(\"ok\")" },
        { "ResultDoExit", "let x = work: do\n    exit to work: 4\nif x == 4 => writeLine(\"ok\")" },
        { "ResultLabeledDo", "let x = work: do\n    if true => exit to work: 5\n    exit to work: 6\nif x == 5 => writeLine(\"ok\")" },
        { "ResultLabeledIf", "let x = choice: if true\n    if true => yield to choice: 7\n    yield 8\nelse\n    yield 9\nif x == 7 => writeLine(\"ok\")" },
        { "ResultLoop", "var i = 0\nlet x = loop\n    i += 1\n    if i < 3 => continue\n    exit i\nif x == 3 => writeLine(\"ok\")" },
        { "ResultLoopExits", "var c = false\nlet x = loop\n    if c => exit 1\n    exit 2\nif x == 2 => writeLine(\"ok\")" },
        { "ResultOuterLoop", "let x = outer: loop\n    loop\n        exit to outer: 3\nif x == 3 => writeLine(\"ok\")" },
        { "ResultOneInput", "var c = true\nwhile true\n    let x = if c => 1 else => exit\n    if x == 1 => writeLine(\"ok\")\n    exit" },
        { "ResultNestedLoop", Nested },
        { "ResultReadOrder", "var x = 2\nlet y = x + (if true => x++ else => 0)\nif y == 4 and x == 3 => writeLine(\"ok\")" },
        { "ResultCheckedEdge", "var x = 3\nlet y = if true => x + 1 else => x - 1\nif y == 4 => writeLine(\"ok\")" },
        { "ResultDead", "while true\n    exit\n    let x = if true => 1 else => 2\nwriteLine(\"ok\")" },
        { "ResultNever", "while true\n    let x: i32 = if true => exit else => continue\nwriteLine(\"ok\")" },
        { "ResultDeadLoop", "while true\n    exit\n    let x: i32 = loop => continue\nwriteLine(\"ok\")" },
        { "ResultShortTransfer", "let x = outer: do\n    let b = true and (inner: do => exit to outer: 1)\n    exit to outer: 2\nif x == 1 => writeLine(\"ok\")" },
        { "ResultShortSkipTransfer", "let x = outer: do\n    let b = false and (inner: do => exit to outer: 1)\n    exit to outer: 2\nif x == 2 => writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsResultFixtures(string name, string source)
        => ScalarEmissionTest.EmitFixture(name, source, "ok\n");

    [Fact]
    public void ResultPhisAreTypedAndUseVariableInputCounts()
    {
        var source = "var c = 1\nlet x = if c == 0 => 10 else if c == 1 => 20 else => 30\nwhile true\n    let y = if c == 1 => x else => exit\n    exit";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var function = module.GetFunction(0);
        var phis = function.Instructions.Where(x => x.Opcode == EmissionOpcode.Phi).ToArray();
        Assert.Equal(new[] { 6, 2 }, phis.Select(x => function.GetOperands(x).Length));
        using var writer = new StringWriter();
        module.WriteIr(writer);
        Assert.Contains(" = phi i32 ", writer.ToString());
        Assert.All(function.Slots, x => Assert.Equal(OwnershipPlaceKind.Local, c.Ownership.Bodies[0].Places[x.Place].Kind));
    }

    [Fact]
    public void UnreachableResultsHaveNoSyntheticMissingValuesOrPhysicalBlocks()
    {
        var c = MinimalEmissionTest.Analyze("while true\n    exit\n    let x = if true\n        yield 1\n    else\n        yield 2\nwriteLine(\"ok\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        var phi = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Phi);
        Assert.False(body.IsReachable(phi));
        Assert.Equal(0, body.Values[phi].Count);
        Assert.DoesNotContain(module.GetFunction(0).Instructions, x => x.Opcode == EmissionOpcode.Phi || x.Operation == phi);
        for (var i = 0; i < body.Operations.Count; i++)
        {
            var op = body.Operations[i];
            if (op.Place >= 0 && ReferenceEquals(body.Places[op.Place].Type, BoundType.I32) && op.Kind is OwnershipOperationKind.Write or OwnershipOperationKind.Produce)
            {
                Assert.NotEqual(OwnershipValueKind.None, body.Values[i].Kind);
            }
        }
    }

    [Fact]
    public void WarmNestedResultAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Nested);
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

    [Theory]
    [InlineData("var x = 1\nif true\n    defer => x = 2\n    ()")]
    [InlineData("var x = 1\nlet y = work: do\n    defer => x = 2\n    exit to work: x")]
    public void DeferredCleanupRemainsExplicitlyUnsupported(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("duplicate-edge")]
    [InlineData("wrong-edge")]
    [InlineData("unavailable-value")]
    [InlineData("wrong-type")]
    [InlineData("replacement")]
    [InlineData("missing-value")]
    [InlineData("missing-write")]
    [InlineData("missing-terminator")]
    public void MalformedResultPlansNeverWriteIr(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("var c = true\nlet x = if c => 1 + 2 else => 4 + 5\nif x == 3 => writeLine(\"ok\")");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var phi = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Phi);
        var value = body.Values[phi];
        var input = body.PhiInputs[value.Start];
        switch (mutation)
        {
            case "zero":
                body.Values[phi] = value with { Count = 0 };
                break;
            case "duplicate-edge":
                body.PhiInputs[value.Start] = body.PhiInputs[value.Start + 1];
                break;
            case "wrong-edge":
                body.PhiInputs[value.Start] = input with { Edge = 0 };
                break;
            case "unavailable-value":
                body.PhiInputs[value.Start] = input with { Value = body.PhiInputs[value.Start + 1].Value };
                break;
            case "wrong-type":
                var place = body.Operations[phi].Place;
                body.PlaceStorage[place] = body.Places[place] with { Type = BoundType.Boolean };
                break;
            case "replacement":
                body.OperationStorage[input.Write] = body.Operations[input.Write] with { Placement = PlacementKind.Replacement };
                break;
            case "missing-value":
                body.Values[input.Write] = default;
                break;
            case "missing-write":
                body.PhiInputs[value.Start] = input with { Write = -1 };
                break;
            case "missing-terminator":
                body.EdgeHeads[phi] = -1;
                break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out error));
        Assert.NotNull(error);
        Assert.Equal(string.Empty, writer.ToString());
    }
}
