// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class AggregateEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "AggregateTuple", "let value = (\"a\", \"b\")", "a=1;b=1" },
        { "AggregateArray", "let value: [2 of string] = [\"a\", \"b\"]", "a=1;b=1" },
        { "AggregateNested", "let value = (\"a\", (\"b\", \"c\"))", "a=1;b=1;c=1" },
        { "AggregateMove", "let value = (\"a\", \"b\")\nlet other = value@move", "a=1;b=1" },
        { "AggregateSelf", "var value = (\"a\", \"b\")\nvalue = value@move", "a=1;b=1" },
        { "AggregateReplace", "var value = (\"a\", \"b\")\nvalue = (\"c\", \"d\")", "a=1;b=1;c=1;d=1" },
        { "AggregateLoop", "var i = 0\nwhile i < 3\n    let value = (\"a\", \"b\")\n    i += 1", "a=3;b=3" },
        { "AggregateCopy", "let value: (u8, u64) = (2, 7)\nlet other = value\nlet again = value", string.Empty },
        { "AggregateArrayCopy", "let value: [3 of i32] = [1, 2, 3]\nlet other = value\nlet again = value", string.Empty },
        { "AggregateUnit", "let value: [2 of ()] = [(), ()]", string.Empty },
        { "AggregateTuplePattern", "let value = (1, 2)\nmatch value\n    (let a, _) => ()", string.Empty },
        { "AggregateEmpty", "let value: [0 of string] = []", string.Empty },
        { "AggregateConditional", "var flag = true\nvar value = (\"a\", \"b\")\nif flag => value@move\nvalue = (\"c\", \"d\")", "a=1;b=1;c=1;d=1" },
        { "AggregateConditionalSkip", "var flag = false\nvar value = (\"a\", \"b\")\nif flag => value@move\nvalue = (\"c\", \"d\")", "a=1;b=1;c=1;d=1" },
        { "AggregateConditionalLoop", "var value = (\"a\", \"b\")\nvar i = 0\nwhile i < 3\n    if i == 1 => value@move\n    value = (\"c\", \"d\")\n    i += 1", "a=1;b=1;c=3;d=3" },
        { "AggregateMixed", "let value: (u8, string, u64, string, ()) = (200, \"a\", 255, \"b\", ())", "a=1;b=1" },
        { "AggregateResultPayload", "let value = (if true => \"a\" else => \"b\", \"c\")", "a=1;b=0;c=1" },
        { "AggregateCallPayload", "func echo(x: string) -> string => x@move\nlet value = (echo(\"a\"), \"b\")", "a=1;b=1" },
        { "AggregatePartialExit", "loop\n    let value: (string, i32) = (\"a\", (exit))", "a=1" },
        { "AggregatePartialReturn", "func f() -> ()\n    let value: (string, i32) = (\"a\", (return))\nf()", "a=1" },
        { "AggregateArrayPartialExit", "loop\n    let value: [2 of string] = [\"a\", (exit)]", "a=1" },
        { "AggregateMatch", "match true\n    true\n        let value = (\"a\", \"b\")\n    false\n        let value = (\"c\", \"d\")", "a=1;b=1;c=0;d=0" },
        { "AggregateCovered", "match true\n    _ => ()\n    true\n        let value = (\"a\", \"b\")", "a=0;b=0" },
        { "AggregateDefer", "var i = 0\nloop\n    defer\n        let value = (\"a\", \"b\")\n    i += 1\n    if i == 3 => exit\n    continue", "a=3;b=3" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConstructionTransferAndDestruction(string name, string source, string destruction)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty);
        if (destruction.Length != 0)
        {
            StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, destruction);
        }
    }

    [Fact]
    public void WarmAnalysisAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("let value = (\"a\", (2, \"b\"))\nlet moved = value@move\nlet array: [2 of i32] = [1, 2]\nlet copied = array");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var valid = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("declaration")]
    [InlineData("placement")]
    [InlineData("completion")]
    [InlineData("flag")]
    public void InvalidAggregatePlansDoNotWriteIr(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("var value = (\"a\", 1)\nif true => value@move\nvalue = (\"b\", 2)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var construction = body.ConstructionStorage[0];
        if (mutation == "count")
        {
            body.ConstructionStorage[0] = construction with { PayloadCount = 0 };
        }
        else if (mutation == "flag")
        {
            var index = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Consume && x.Place == 1);
            Assert.True(index >= 0);
            body.OperationStorage[index] = body.OperationStorage[index] with { Acquisition = AcquisitionKind.Copy };
        }
        else
        {
            var kind = mutation == "declaration" ? OwnershipOperationKind.Declare : mutation == "placement" ? OwnershipOperationKind.PayloadPlacement : OwnershipOperationKind.CompleteConstruction;
            var index = body.OperationStorage.FindIndex(x => x.Kind == kind && (kind == OwnershipOperationKind.CompleteConstruction ? x.Place == construction.Place : x.Place == construction.PayloadStart));
            Assert.True(index >= 0);
            body.OperationStorage[index] = body.OperationStorage[index] with { Kind = OwnershipOperationKind.Produce };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void TupleLayoutKeepsLogicalOrderAndStableAlignmentSorting()
    {
        var c = MinimalEmissionTest.Analyze("let value: (u8, string, u64, string, ()) = (200, \"a\", 255, \"b\", ())");
        var type = c.Ownership.Bodies[0].Constructions[0].Place;
        var pool = new AggregateLayoutPool();
        var layout = Assert.IsType<AggregateLayout>(pool.Get(c.Ownership.Bodies[0].Places[type].Type));
        Assert.Equal(new[] { 56, 0, 24, 32, 57 }, layout.Value.Layout.FieldOffsets.ToArray());
        Assert.Equal(64, layout.Value.Layout.Size);
        Assert.Equal(8, layout.Value.Layout.Alignment);
        const string Source = "let value = (\"first\", \"second\", \"third\")";
        var ir = ScalarEmissionTest.EmitFixture("AggregateOrder", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateOrder", Source, ir, string.Empty, "first=1;second=1;third=1", order: [2, 1, 0]);
    }

    [Theory]
    [InlineData("var value: [2147483647 of string]")]
    [InlineData("func echo(value: [2147483647 of string]) -> [2147483647 of string] => value\n()")]
    public void UnsupportedAggregateShapesRemainExplicit(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void AbortDuringConstructionDoesNotUnwindPayloads()
    {
        const string Source = "var x = 2147483647\nlet value = (\"held\", x + 1)";
        const string Error = "Hello.kimi:2:22: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("AggregateAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("AggregateAbort", Source, ir, string.Empty, "held=0", 1, Error);
    }
}
