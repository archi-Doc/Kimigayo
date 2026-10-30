// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AggregateEmissionTest
{
    private const string Echo = "func echo(x: string) -> string => x@move\n";

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
        { "AggregateCallPayload", Echo + "let value = (echo(\"a\"), \"b\")", "a=1;b=1" },

        // The component a transfer abandons is a call's result, which has a destruction to audit; a constant has none (PLAN G157 (45)).
        { "AggregatePartialExit", Echo + "loop\n    let value: (string, i32) = (echo(\"a\"), (exit))", "a=1" },
        { "AggregatePartialReturn", Echo + "func f() -> ()\n    let value: (string, i32) = (echo(\"a\"), (return))\nf()", "a=1" },
        { "AggregateArrayPartialExit", Echo + "loop\n    let value: [2 of string] = [echo(\"a\"), (exit)]", "a=1" },
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
            StringEmissionTest.WriteAuditedFixture(name, ir, string.Empty, destruction);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmAnalysisAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("let value = (\"a\", (2, \"b\"))\nlet moved = value@move\nlet array: [2 of i32] = [1, 2]\nlet copied = array");
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.WriteIr(TextWriter.Null, out _);
        });
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TupleLayoutKeepsLogicalOrderAndStableAlignmentSorting()
    {
        var c = MinimalEmissionTest.Analyze("let value: (u8, string, u64, string, ()) = (200, \"a\", 255, \"b\", ())");
        var type = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<TupleLiteralKoto>()).TypeOf()!;
        var layout = c.Layout.Of(type).Value!.Layout;
        Assert.Equal(new[] { 56, 0, 24, 32, 57 }, layout.FieldOffsets.ToArray());
        Assert.Equal(64, layout.Size);
        Assert.Equal(8, layout.Alignment);
        const string Source = "let value = (\"first\", \"second\", \"third\")";
        var ir = ScalarEmissionTest.EmitFixture("AggregateOrder", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateOrder", ir, string.Empty, "first=1;second=1;third=1", order: [2, 1, 0]);
    }

    [Theory]
    [InlineData("var value: [2147483647 of string]")]
    [InlineData("func echo(value: [2147483647 of string]) -> [2147483647 of string] => value\n()")]
    public void UnsupportedAggregateShapesRemainExplicit(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void AbortDuringConstructionDoesNotUnwindPayloads()
    {
        const string Source = "var x = 2147483647\nlet value = (\"held\", x + 1)";
        const string Error = "Hello.kimi:2:22: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("AggregateAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("AggregateAbort", ir, string.Empty, "held=0", 1, Error);
    }
}
