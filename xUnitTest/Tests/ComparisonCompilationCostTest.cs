// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ComparisonCompilationCostTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WarmOperatorBindingReusesItsWitnessPlans(bool snapshot, bool tuple)
    {
        const string Source = """
            struct Key
                Self is Equatable
                public let value: i32 = 1
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.value == other.value
            let first = Key.init()
            let last = Key.init()
            require first == last and not (first != last) else => $abort("comparison")
            """;
        var source = snapshot ? Source.Replace("Self is Equatable", "Self is Copy\n    Self is Equatable", StringComparison.Ordinal)
            .Replace("first == last", "first@ref == last@ref", StringComparison.Ordinal)
            .Replace("first != last", "first@ref != last@ref", StringComparison.Ordinal) : Source;
        if (tuple)
        {
            source = source.Replace("require first == last", "let a = (first@ref, 1)\nlet b = (last@ref, 1)\nrequire a == b", StringComparison.Ordinal)
                .Replace("first != last", "a != b", StringComparison.Ordinal);
        }

        var c = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Repeated comparison Binding failed.");
            }
        }));
    }
}
