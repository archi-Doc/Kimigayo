// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.8: reslicing, splitting, Index formation and resolution, element Places and Slice iteration need no
/// heap allocation, element storage or reference-count update; only the backing Array allocates.</summary>
public class SliceCostTest
{
    private const string Operations =
        "var i: isize = 0\nvar total: i32 = 0\nwhile i < 1024\n" +
        "    let s = values[..]\n    let inner = s[1..3]\n" +
        "    let parts = s.splitAt(1)\n    require parts.0.length == 1 and parts.1.length == 3 else => $abort(\"split\")\n" +
        "    let ends = s.splitAt(^1)\n    require ends.1[0] == 40 else => $abort(\"split end\")\n" +
        "    match s.trySplitAt(2)\n        .Some(let pair) => require pair.1[0] == 30 else => $abort(\"try split\")\n        .None => $abort(\"try split none\")\n" +
        "    match s.trySplitAt(5)\n        .Some(_) => $abort(\"beyond split\")\n        .None => ()\n" +
        "    match s.trySlice(1..)\n        .Some(let tail) => require tail.length == 3 else => $abort(\"tail\")\n        .None => $abort(\"tail none\")\n" +
        "    match s.trySlice((1..3).resolve(s.length))\n        .Some(let middle) => require middle[1] == 30 else => $abort(\"resolved\")\n        .None => $abort(\"resolved none\")\n" +
        "    match s.tryGet(^1)\n        .Some(let last) => require last == 40 else => $abort(\"last\")\n        .None => $abort(\"last none\")\n" +
        "    let nested = inner[1..]\n    require nested[0] == 30 else => $abort(\"nested\")\n" +
        "    for value in inner => total += value\n" +
        "    i += 1\n" +
        "require total == 51200 else => $abort(\"total\")";

    [Fact]
    public void FixedArraySliceOperationsAllocateNothing()
        => NativeAllocationAudit.WriteFixture("SliceCostFixed", "let values: [4 of i32] = [10, 20, 30, 40]\n" + Operations, 0, 0, 0);

    [Fact]
    public void ArraySliceOperationsAllocateOnlyTheBackingArray()
        => NativeAllocationAudit.WriteFixture("SliceCostArray", "let values: Array<i32> = [10, 20, 30, 40]\n" + Operations, 1, 1, 16);

    [Theory]
    [InlineData("Binding")]
    [InlineData("Ownership")]
    [InlineData("Emission")]
    public void WarmSliceAnalysisAndEmissionAllocateNothing(string stage)
    {
        var c = MinimalEmissionTest.Analyze("let values: [4 of i32] = [10, 20, 30, 40]\n" + Operations);
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            var valid = stage switch
            {
                "Binding" => c.Bind().IsComplete,
                "Ownership" => c.Ownership.Analyze().IsVerified,
                _ => c.Emission.WriteIr(TextWriter.Null, out _),
            };
            if (!valid)
            {
                throw new InvalidOperationException(stage + " failed.");
            }
        }));
    }
}
