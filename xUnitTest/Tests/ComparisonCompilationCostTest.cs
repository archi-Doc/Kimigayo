// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ComparisonCompilationCostTest
{
    [Fact]
    public void WarmOperatorBindingReusesItsWitnessPlans()
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
        var c = MinimalEmissionTest.Analyze(Source);
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
