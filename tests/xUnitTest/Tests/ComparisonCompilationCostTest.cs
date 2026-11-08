// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ComparisonCompilationCostTest
{
    [Trait("Purpose", "Allocation")]
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
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Repeated comparison Binding failed.");
            }
        }));
    }

    // SPEC 13.4, 14.8.1: a string comparison is one witness call, an operator's and each of a Literal Pattern's eight arms, with no plan per
    // site: warm Binding and warm MIR allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("    return text == \"a\" or text < \"b\" or text != other")]
    [InlineData("    match text\n        \"a\" => return true\n        \"b\" => return true\n        \"c\" => return true\n        \"d\" => return true\n" +
        "        \"e\" => return true\n        \"f\" => return true\n        \"g\" => return true\n        \"h\" => return true\n        _ => return false")]
    public void WarmStringComparisonBindingAndMirAllocateNothing(string body)
    {
        const string Head = "func test(text: ref/string, other: ref/string) -> bool\n";
        var c = MinimalEmissionTest.Analyze(Head + body + "\nlet text = \"a\"\nif test(text, text) => Console.writeLine(text)");
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Repeated comparison Binding failed.");
            }
        }));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            _ = c.BuildMir();
        }));
    }
}
