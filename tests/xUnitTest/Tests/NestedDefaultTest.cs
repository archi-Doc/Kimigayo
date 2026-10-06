// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 7.2.3: each pending call owns its prepared slots. A nested omission must restore the outer declaration and slots.
public class NestedDefaultTest
{
    private const string Scalar = "func leaf(a: i32, b: i32 = a + 1) -> i32 => a + b\nfunc outer(x: i32, y: i32 = leaf(x), z: i32 = x + y) -> i32 => x + y + z\nrequire outer(4) == 26 and outer(2) == 14 else => $abort(\"frames\")";

    [Theory]
    [InlineData("Scalar", Scalar, "")]
    [InlineData("Sibling", "func leaf(a: i32, b: i32 = a + 1) -> i32 => a + b\nfunc f(x: i32, y: i32 = leaf(x) + leaf(x + 2)) -> i32 => x + y\nrequire f(3) == 21 else => $abort(\"siblings\")", "")]
    [InlineData("DifferentSizes", "func leaf(a: i32, b: i32 = a + 1, c: i32 = a + b, d: i32 = c + b) -> i32 => d\nfunc f(x: i32, y: i32 = leaf(x) + x) -> i32 => y\nrequire f(3) == 14 else => $abort(\"sizes\")", "")]
    [InlineData("ThreeLevels", "func inner(a: i32, b: i32 = a + 1) -> i32 => b\nfunc middle(a: i32, b: i32 = inner(a) + a) -> i32 => b\nfunc outer(a: i32, b: i32 = middle(a) + a) -> i32 => b\nrequire outer(3) == 10 else => $abort(\"depth\")", "")]
    [InlineData("Owned", "func leaf(x: string, y: string = Text.toString(x)) -> string => y@move\nfunc outer(x: string, y: string = leaf(Text.toString(x)), z: string = Text.toString(x))\n    Console.writeLine(x)\n    Console.writeLine(y)\n    Console.writeLine(z)\nouter(\"prepared\")", "prepared\nprepared\nprepared\n")]
    [InlineData("Order", "func note(n: i32) -> i32\n    Console.writeLine(Text.toString(n))\n    return n\nfunc inner(x: i32, y: i32 = note(2)) -> i32 => x + y\nfunc outer(x: i32, y: i32 = inner(note(1)), z: i32 = note(3)) => ()\nouter(note(0))", "0\n1\n2\n3\n")]
    [InlineData("Supplied", "func inner(x: i32 = (loop => continue)) -> i32 => x\nfunc outer(x: i32 = inner()) -> i32 => x\nrequire outer(7) == 7 else => $abort(\"supplied\")", "")]
    [InlineData("Never", "func inner(x: i32 = (loop => continue)) -> i32 => x\nfunc outer(x: string = \"owned\", y: i32 = inner()) => Console.writeLine(x)\nConsole.writeLine(\"begin\")\nouter()", "begin\n")]
    public void NestedOmissionsExecute(string name, string source, string output)
        => ScalarEmissionTest.EmitFixture("NestedDefault" + name, source, output, timeoutMilliseconds: name == "Never" ? 200 : 0);

    [Fact]
    public void EachCallDestroysOnlyItsDeliveredValues()
    {
        const string Source = "func inner(a: string = \"inner\") -> string => \"result\"\nfunc outer(a: string = \"outer\", b: string = inner()) => ()\nouter()";
        var ir = ScalarEmissionTest.EmitFixture("NestedDefaultCleanup", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("NestedDefaultCleanup", Source, ir, string.Empty, "inner=1;result=1;outer=1", order: [0, 1, 2]);
    }

    [Fact]
    public void SuppliedOuterDefaultStillChecksTheInnerDeclaration()
    {
        const string Source = "func bad(x: string, y: string = x@move) -> string => y@move\nfunc outer(x: string, y: string = bad(Text.toString(x))) => ()\nouter(\"a\", \"supplied\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal(OwnershipFailure.DefaultArgumentMove, issue.Failure);
        Assert.Equal("x", issue.Source.ToString());
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void RebindingAndReloadRestoreFrames()
    {
        var c = MinimalEmissionTest.Analyze(Scalar);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(c, error));
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        Assert.True(restored.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(restored, error));
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void NestedFramesReuseStorage()
        {
            var c = MinimalEmissionTest.Analyze(Scalar);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
