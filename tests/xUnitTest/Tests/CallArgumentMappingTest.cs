// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallArgumentMappingTest
{
    [Theory]
    [InlineData("1", true, 2, new[] { 0 })]
    [InlineData("c: 3, a: 1", true, 1, new[] { 2, 0 })]
    [InlineData("missing: 4, c: 3, a: 1", false, 1, new[] { -1, 2, 0 })]
    [InlineData("a: 1, a: 2, c: 3", false, 1, new[] { 0, -1, 2 })]
    [InlineData("a: 1, 2, c: 3", false, 1, new[] { 0, -1, 2 })]
    [InlineData("1, 2, 3, 4", false, 0, new[] { 0, 1, 2, -1 })]
    public void MappingPreservesIndependentPositionsAfterAFailure(string arguments, bool valid, int defaults, int[] expected)
    {
        var (call, function) = Parse(arguments);
        var map = new int[expected.Length];
        var used = new bool[3];
        var result = Binding.MapCallArguments(call, function, false, map, used);
        Assert.Equal(valid, result.Valid);
        Assert.Equal(defaults, result.DefaultsUsed);
        Assert.Equal(expected, map);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void ReusedMappingsAllocateNothingAndClearPreviousUse()
    {
        var (call, function) = Parse("c: 3, a: 1");
        var map = new int[2];
        var used = new[] { true, true, true };
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            var result = Binding.MapCallArguments(call, function, false, map, used);
            if (!result.Valid || result.DefaultsUsed != 1 || map[0] != 2 || map[1] != 0 || used[1])
            {
                throw new InvalidOperationException("Stale mapping.");
            }
        }));
    }

    private static (InvocationKoto Call, FunctionKoto Function) Parse(string arguments)
    {
        var c = MinimalEmissionTest.Analyze("func f(a: i32, b: i32 = 2, c: i32 = 3) -> i32 => a + b + c\nfunc use() -> i32 => f(" + arguments + ")");
        var members = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>();
        return (Assert.IsType<InvocationKoto>(members.Single(x => x.Name == "use").ExpressionBody), members.Single(x => x.Name == "f"));
    }
}
