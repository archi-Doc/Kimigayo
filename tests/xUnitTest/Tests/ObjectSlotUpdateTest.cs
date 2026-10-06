// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ObjectSlotUpdateTest
{
    private const string Item = """
        struct Item
            public let id: i32
            public init(id: i32) => self.id = id
            drop
                if self.id == 1 => Console.writeLine("one")
                else if self.id == 2 => Console.writeLine("two")
                else => Console.writeLine("three")
        """;

    [Theory]
    [InlineData("Obj", "makeObj", "@uniq")]
    [InlineData("Rc", "makeRc", "@uniq")]
    [InlineData("Arc", "makeArc", "@uniq")]
    public void CompleteSlotsShareTransferAndCleanup(string name, string factory, string borrow)
        => NativeAllocationAudit.WriteFixture("ObjectSlotUpdate" + name, Source(factory, borrow), 3, 3, 60, "three\nupdated\none\ntwo\n");

    [Theory]
    [InlineData("makeObj")]
    [InlineData("makeRc")]
    [InlineData("makeArc")]
    public void AStoredPayloadViewPreventsSlotReplacement(string factory)
    {
        var source = Item + "\nvar owner = Kimi.Intrinsics." + factory + "(Item.init(1))\nlet view = owner@objref\nKimi.Intrinsics.replace(owner@uniq, with: Kimi.Intrinsics." + factory + "(Item.init(2)))\nrequire view.id == 1 else => $abort(\"view\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Activation);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("makeRc")]
    [InlineData("makeArc")]
    public void WarmSlotUpdatesAllocateNothing(string factory)
    {
        var c = MinimalEmissionTest.Analyze(Source(factory, "@uniq"));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Source(string factory, string borrow)
        => Item + "\nvar first = Kimi.Intrinsics." + factory + "(Item.init(1))\nvar second = Kimi.Intrinsics." + factory + "(Item.init(2))\n" +
            "let old = Kimi.Intrinsics.exchange(first" + borrow + ", with: Kimi.Intrinsics." + factory + "(Item.init(3)))\n" +
            "Kimi.Intrinsics.swap(first" + borrow + ", second" + borrow + ")\nKimi.Intrinsics.replace(second" + borrow + ", with: old@move)\n" +
            "require first.id == 2 and second.id == 1 else => $abort(\"slots\")\nConsole.writeLine(\"updated\")";
}
