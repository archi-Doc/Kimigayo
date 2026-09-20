// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ScalarTemporaryBorrowTest
{
    private const string Read = "func read(n?: ref/i32) -> bool\n    Console.writeLine(\"called\")\n    return true\nfunc one() -> i32 => 41\n";

    private const string Inspect = "func inspect<T>(value?: ref/T) -> () => ()\nfunc makeValue() -> i64 => 1\n";

    // SPEC 10.2: an owner scalar temporary is materialized once and shared-borrowed for a ref/T argument.
    [Theory]
    [InlineData("Call", Read + "require read(one()) else => $abort(\"value\")", "called\n")]
    [InlineData("Twice", Read + "require read(one()) and read(one()) else => $abort(\"value\")", "called\ncalled\n")]
    [InlineData("Binary", Read + "require read(one() + 1) else => $abort(\"value\")", "called\n")]
    [InlineData("Bool", "func check(b?: ref/bool) -> bool\n    Console.writeLine(\"checked\")\n    return true\nfunc yes() -> bool => true\nrequire check(yes()) else => $abort(\"value\")", "checked\n")]
    [InlineData("Literal", Read + "require read(1) else => $abort(\"value\")", "called\n")]
    [InlineData("Float", "func check(x?: ref/f64) -> bool\n    Console.writeLine(\"checked\")\n    return true\nrequire check(1.5) else => $abort(\"value\")", "checked\n")]
    [InlineData("Rank", "func pick(value?: i64) -> i32 => 1\nfunc pick(value?: ref/i32) -> i32 => 2\nrequire pick(1) == 1 else => $abort(\"rank\")", "")]
    [InlineData("Returned", Read + "func keep(n?: ref/i32) -> ref{n}/i32 => n\nrequire read(keep(1)) and read(keep(one())) else => $abort(\"value\")", "called\ncalled\n")]
    [InlineData("Generic", Inspect + "inspect(1)\ninspect(1.5)\ninspect(makeValue())\nConsole.writeLine(\"done\")", "done\n")]
    public void ExecutesScalarTemporaryBorrows(string name, string source, string output)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ScalarTemporaryBorrow" + name, source, output);
    }

    [Theory]
    [InlineData("func keep(n?: ref/i32) -> ref{n}/i32 => n\nfunc one() -> i32 => 1\nlet r = keep(one())\nlet s = r")]
    [InlineData("func keep(n?: ref/i32) -> ref{n}/i32 => n\nlet r = keep(1)\nlet s = r")]
    public void RejectsReturnedBorrowOfTemporaryAfterStatement(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("func bump(n?: uniq/i32)\n    ()\nbump(1)")]
    [InlineData("func chooseBorrow(value?: ref/i32) -> () => ()\nfunc chooseBorrow(value?: ref/i64) -> () => ()\nchooseBorrow(1)")]
    [InlineData("func read(n?: ref/u8) -> i32 => 0\nlet r = read(-1)")]
    [InlineData("func modify<T>(value?: uniq/T) -> () => ()\nmodify(1)")]
    public void RejectsInvalidLiteralBorrows(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
