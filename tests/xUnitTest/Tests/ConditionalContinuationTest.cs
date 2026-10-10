// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalContinuationTest
{
    [Fact]
    public void CommonOuterBorrowSurvivesTheJoinUntilCallAcquisitionEnds()
        => ScalarEmissionTest.EmitFixture(
            "NeverConditional" + Configuration + "Borrow",
            "func stop() -> Never => $abort(\"stop\")\nfunc inspect(s: ref/string, ready: bool) => ()\nfunc f(c: bool)\n    var s = \"s\"\n    inspect(s, if c => stop() else => false)\n    s = \"new\"\n    Console.writeLine(s)\nf(false)\nf(true)",
            "new\n",
            1,
            "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    private static string Source(string declaration, string yes, string no, string tail)
        => "func f(c: bool)\n    " + declaration + "\n    if c\n        " + yes + "\n    else\n        " + no + "\n    " + tail;

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeadTailAfterTerminalBranchesIsRecomputedAfterReload(bool reload)
        {
            var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1\n        return", "x = 2\n        return", "Console.writeLine(\"dead\")") + "\nf(true)");
            if (reload)
            {
                c = CompilationTestHelper.Reload(c);
                Assert.True(c.Bind().IsComplete);
                Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
                c.Ownership.Analyze();
            }

            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
            var body = Assert.Single(c.Ownership.Bodies, b => b.Function.Name == "f");
            var calls = Enumerable.Range(0, body.Operations.Count).Where(i => body.Operations[i].Source is InvocationKoto).ToArray();
            Assert.NotEmpty(calls);
            Assert.All(calls, i => Assert.False(body.IsReachable(i)));
            c.Bind();
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                c.Ownership.Analyze();
                c.Emission.WriteIr(TextWriter.Null, out _);
            }));
        }
    }
}
