// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
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

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        // The dead tail after two returning branches is reached only by the checks (its MIR: tests/mir/control.kimi `deadTail`), also after
        // a reload.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeadTailAfterTerminalBranchesIsRecomputedAfterReload(bool reload)
        {
            var source = Source("var x: i32", "x = 1\n        return", "x = 2\n        return", "Console.writeLine(\"dead\")") + "\nf(true)";
            var c = MinimalEmissionTest.Analyze(source);
            if (reload)
            {
                c = CompilationTestHelper.Reload(c);
                Assert.True(c.Bind().IsComplete);
                Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
                c.Analyze();
            }

            Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
            if (reload)
            {
                Assert.Equal(MirDumpTest.Dump(MinimalEmissionTest.Analyze(source)), MirDumpTest.Dump(c));
            }

            c.Bind();
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(c.Analyze().IsVerified);
            Assert.True(c.Generate(TextWriter.Null));
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                c.Analyze();
                c.Generate(TextWriter.Null);
            }));
        }
    }
}
