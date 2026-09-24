// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ComparisonCompositionTest
{
    [Fact]
    public void MilestoneComposesBorrowedAndTupleWitnesses()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone30.kimi"));
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        const string expected = "User comparisons keep their operands.\nTuple comparisons compose witnesses.\nFloating Contract equality is NaN-reflexive.\nComparison contracts finished.\n";
        ScalarEmissionTest.EmitFixture("ComparisonCompositionMilestone30", source, expected);
    }

    [Fact]
    public void FloatingTupleOperatorsPreservePartialOrderingAndShortCircuit()
    {
        const string Source = """
            let nan: f64 = 0.0 / 0.0
            let a = (nan, 1)
            let b = (nan, 2)
            require not (a == b) and a != b else => $abort("equality")
            require not (a < b) and not (a <= b) and not (a > b) and not (a >= b) else => $abort("unordered")
            require (1, nan) < (2, nan) and (2, nan) >= (1, nan) else => $abort("short circuit")
            require (0.0, 1) == (-0.0, 1) and (0.0, 1) <= (-0.0, 1) else => $abort("zero")
            """;
        NativeAllocationAudit.WriteFixture("ComparisonCompositionFloating", Source, 0, 0, 0);
    }

    [Theory]
    [InlineData("let x = (1, true) < (1, false)")]
    [InlineData("let x = (1, 2) == (true, 2)")]
    [InlineData("let a = (0.0, 1)\nlet b = (1.0, 2)\nlet invalid = a.compare(b@ref)")]
    public void CompositionDoesNotInventMissingCapabilitiesOrCoreConversions(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
