// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ComparisonCompositionTest
{
    [Fact]
    public void RecursiveGenericWitnessCompositionReusesThePreparedHelper()
    {
        const string Source = """
            struct Key<T>
                Self is Equatable
                T is Copy
                public let value: T
                public let remaining: i32
                public init(value: T, remaining: i32)
                    self.value = value
                    self.remaining = remaining
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    if self.remaining == 0 => return other.remaining == 0
                    if other.remaining == 0 => return false
                    let next = Key<T>.init(self.value, self.remaining - 1)
                    let peer = Key<T>.init(other.value, other.remaining - 1)
                    return (next@ref, 0) == (peer@ref, 0)
            let first = Key<i32>.init(1, 3)
            let last = Key<i32>.init(2, 3)
            require (first@ref, 0) == (last@ref, 0) else => $abort("recursive witness")
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        NativeAllocationAudit.WriteFixture("ComparisonCompositionRecursive", Source, 0, 0, 0);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceValuesComposeThroughByValueGenericParameters(bool match)
    {
        const string Source = """
            struct Key
                Self is Equatable
                public let number: i32
                public init(number: i32) => self.number = number
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.number == other.number
            func equal<T>(left: T, right: T) -> bool
                T is Copy and Equatable
                return left == right
            let first = Key.init(1)
            let last = Key.init(1)
            require equal(first@ref, last@ref) else => $abort("reference witness")
            """;
        var source = match ? Source.Replace("return left == right", "return match true\n        true => left == right\n        false => false", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("ComparisonCompositionReferenceValues" + match, source, 0, 0, 0);
    }
}
