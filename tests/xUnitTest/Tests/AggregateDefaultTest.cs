// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 7.2.3: independent owned defaults use ordinary expression acquisition, construction and cleanup.
public class AggregateDefaultTest
{
    private const string Point = "struct Point\n    Self is Copy\n    public let x: i32\n    public let y: i32\n    public init(x: i32, y: i32)\n        self.x = x\n        self.y = y\n";
    private const string FunctionFactory = "func makeAdder(offset: i32) -> (i32) -> i32 => func [offset] (x) => x + offset\nfunc f(x: i32, action: (i32) -> i32 = makeAdder(5)) -> i32 => action(x)\nrequire f(2) == 7 else => $abort(\"factory\")";

    [Theory]
    [InlineData("StringLocal", "func f(x: string = label work: do\n    let text = \"local\"\n    exit to work text@move\n) => Console.writeLine(x)\nf()", "local\n")]
    [InlineData("Tuple", "func f(x: (string, i32) = (\"tuple\", 5))\n    Console.writeLine(x.0)\n    require x.1 == 5 else => $abort(\"tuple\")\nf()", "tuple\n")]
    [InlineData("Array", "func f(x: [2 of string] = [\"one\", \"two\"])\n    Console.writeLine(x[0])\n    Console.writeLine(x[1])\nf()", "one\ntwo\n")]
    [InlineData("Enum", "func f(x: Option<string> = .Some(\"enum\"))\n    match x@move\n        .Some(let text) => Console.writeLine(text)\n        .None => ()\nf()", "enum\n")]
    [InlineData("Struct", "struct Box\n    public let text: string\n    public init(text: string) => self.text = text@move\nfunc f(x: Box = Box.init(\"struct\")) => Console.writeLine(x.text)\nf()", "struct\n")]
    [InlineData("FunctionFactory", FunctionFactory, "")]
    [InlineData("FunctionEnum", "func inc(x: i32) -> i32 => x + 1\nfunc f(action: Option<(i32) -> i32> = .Some(inc)) -> i32\n    return match action@move\n        .Some(let f) => f(1)\n        .None => 0\nrequire f() == 2 else => $abort(\"enum function\")", "")]
    [InlineData("LocalCapture", "func f(action: (i32) -> i32 = label work: do\n    let k = 2\n    exit to work func [k] (x) => x + k\n) -> i32 => action(1)\nrequire f() == 3 and f() == 3 else => $abort(\"local capture\")", "")]
    [InlineData("MovedLocalCapture", "func f(action: () -> () = label work: do\n    let text = \"capture\"\n    exit to work func [text@move] () => Console.writeLine(text)\n) => action()\nf()\nf()", "capture\ncapture\n")]
    [InlineData("StructCapture", Point + "func f(p: Point, action: () -> i32 = func [p] () => p.x + p.y) -> i32 => action()\nrequire f(Point.init(2, 3)) == 5 else => $abort(\"struct capture\")", "")]
    [InlineData("ArrayCapture", "func f(xs: [3 of i32], action: () -> i32 = func [xs] () => xs[0] + xs[2]) -> i32 => action()\nrequire f([1, 2, 3]) == 4 else => $abort(\"array capture\")", "")]
    [InlineData("EnumCapture", "enum Direction\n    Self is Copy\n    North\n    South\nfunc f(d: Direction, action: () -> i32 = func [d] () => match d\n    .North => 1\n    .South => 2\n) -> i32 => action()\nrequire f(.North) == 1 and f(.South) == 2 else => $abort(\"enum capture\")", "")]
    public void OwnedExpressionsExecute(string name, string source, string output)
        => ScalarEmissionTest.EmitFixture("AggregateDefault" + name, source, output);

    [Theory]
    [InlineData("TupleCleanup", "func f(x: (string, string) = (\"first\", \"second\")) => ()\nf()", "first=1;second=1", new[] { 1, 0 })]
    [InlineData("LocalCleanup", "func f(x: string = label work: do\n    let unused = \"local\"\n    exit to work \"result\"\n) => ()\nf()", "local=1;result=1", new[] { 0, 1 })]
    [InlineData("Abandoned", "func f(x: (string, string) = (\"first\", \"second\")) => ()\nlabel done: do => f(x: (\"first\", (exit to done)))", "first=1", new[] { 0 })]
    public void AggregateAcquisitionHasExactCleanup(string name, string source, string destructions, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("AggregateDefault" + name, source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateDefault" + name, source, ir, string.Empty, destructions, order: order);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void LocalMovedCaptureOwnsAndReleasesItsEnvironment()
        => NativeAllocationAudit.WriteFixture("AggregateDefaultLocalEnvironment", "func f(action: () -> () = label work: do\n    let text = \"captured\"\n    exit to work func [text@move] () => Console.writeLine(text)\n) => action()\nf()\nf()", 2, 2, 48, "captured\ncaptured\n");

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void WarmFunctionFactoryDefaultsAllocateNothing()
        {
            var c = MinimalEmissionTest.Analyze(FunctionFactory);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }

        [Fact]
        public void WarmAggregateCopiesAllocateNothing()
        {
            var c = MinimalEmissionTest.Analyze(Point + "func f(p: Point, action: () -> i32 = func [p] () => p.x + p.y) -> i32 => action()\nrequire f(Point.init(2, 3)) == 5 else => $abort(\"copy\")");
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
