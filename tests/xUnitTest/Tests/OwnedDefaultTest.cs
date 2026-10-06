// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 7.2.3: a default may produce an independent owned value and temporarily inspect a prepared argument.
public class OwnedDefaultTest
{
    private const string CopyText = "func copyText(x: ref/string) -> string => Text.toString(x)\n";

    [Theory]
    [InlineData("Literal", "func f(x: string = \"default\") => Console.writeLine(x)\nf()\nf(\"explicit\")\nf()", "default\nexplicit\ndefault\n")]
    [InlineData("Call", "func make() -> string\n    Console.writeLine(\"make\")\n    return \"default\"\nfunc f(x: string = make()) => Console.writeLine(x)\nf()\nf(\"explicit\")", "make\ndefault\nexplicit\n")]
    [InlineData("Inspection", CopyText + "func f(x: string, y: string = copyText(x))\n    Console.writeLine(x)\n    Console.writeLine(y)\nf(\"prepared\")", "prepared\nprepared\n")]
    [InlineData("Intrinsic", "func f(x: string, y: string = Text.toString(x))\n    Console.writeLine(x)\n    Console.writeLine(y)\nf(\"prepared\")", "prepared\nprepared\n")]
    [InlineData("ScalarBorrow", "func inspect(x: ref/i32) -> i32 => x@follow + 1\nfunc f(x: i32, y: i32 = inspect(x@ref)) -> i32 => y\nrequire f(4) == 5 else => $abort(\"default\")", "")]
    [InlineData("Order", "func note(x: string) -> string\n    Console.writeLine(x)\n    return x@move\nfunc f(a: string, b: string = note(\"default\"), c: string = note(\"last\"))\n    Console.writeLine(a)\n    Console.writeLine(b)\n    Console.writeLine(c)\nf(note(\"explicit\"))", "explicit\ndefault\nlast\nexplicit\ndefault\nlast\n")]
    [InlineData("LaterExplicitAbandons", "func f(a: string = \"bad\", b: i32) => Console.writeLine(a)\nlabel done: do\n    f(b: (exit to done))\nConsole.writeLine(\"done\")", "done\n")]
    [InlineData("DefaultNever", "func f(a: string = \"owned\", b: i32 = (loop => continue)) => Console.writeLine(a)\nConsole.writeLine(\"begin\")\nf()", "begin\n")]
    [InlineData("LocalCall", "func helper(s: string) -> i32 => 1\nfunc f(x: i32 = (label scope: do\n    let n = helper(\"a\")\n    exit to scope n\n)) -> i32 => x\nrequire f() == 1 and f(3) == 3 else => $abort(\"local\")", "")]
    [InlineData("UnexecutedArm", "func helper(s: string) -> i32 => 2\nfunc f(x: i32 = (if true => 1 else => helper(\"a\"))) -> i32 => x\nrequire f() == 1 and f(3) == 3 else => $abort(\"arm\")", "")]
    [InlineData("SuppliedBorrow", "func helper(r: ref/i32) -> i32 => r@follow + 1\nfunc f(x: i32, y: i32 = helper(x@ref)) -> i32 => y\nrequire f(2) == 3 and f(2, 5) == 5 else => $abort(\"borrow\")", "")]
    public void OwnedResultsAndTemporaryInspectionExecute(string name, string source, string output)
        => ScalarEmissionTest.EmitFixture("OwnedDefault" + name, source, output, timeoutMilliseconds: name == "DefaultNever" ? 200 : 0);

    [Theory]
    [InlineData("Delivered", "func f(a: string, b: string, c: string = \"default\") => ()\nf(b: \"second\", a: \"first\")", "first=1;second=1;default=1", new[] { 2, 1, 0 })]
    [InlineData("Abandoned", "func f(a: string, b: string, c: i32, d: string = \"default\") => ()\nlabel done: do => f(b: \"second\", a: \"first\", c: (exit to done))", "first=1;second=1", new[] { 0, 1 })]
    [InlineData("Unreachable", "func f(a: string = \"first\", b: string = \"second\") => ()\nif false => f()", "first=0;second=0", new int[] { })]
    public void OwnedPreparationHasExactCleanup(string name, string source, string destructions, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("OwnedDefault" + name, source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("OwnedDefault" + name, source, ir, string.Empty, destructions, order: order);
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void WarmOwnedPreparationAllocatesNothing()
        {
            var c = MinimalEmissionTest.Analyze(CopyText + "func f(x: string, y: string = copyText(x)) => Console.writeLine(y)\nf(\"prepared\")");
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
