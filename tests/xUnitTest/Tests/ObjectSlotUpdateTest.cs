// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
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

    // SPEC 15.6.7: the replacement's exclusive reservation of the slot cannot activate while the payload view lives. The MIR's record
    // (H5, DECISIONS R5-M185) relates the view's Borrow, its later use and the reservation.
    [Theory]
    [InlineData("makeObj")]
    [InlineData("makeRc")]
    [InlineData("makeArc")]
    public void AStoredPayloadViewPreventsSlotReplacement(string factory)
    {
        var replace = "Kimi.Intrinsics.replace(owner@uniq, with: Kimi.Intrinsics." + factory + "(Item.init(2)))";
        var source = Item + "\nvar owner = Kimi.Intrinsics." + factory + "(Item.init(1))\nlet view = owner@objref\n" + replace + "\nrequire view.id == 1 else => $abort(\"view\")";
        var c = CallReservationTest.AssertMirRecord(source, DiagnosticCode.CallActivationConflict_Kd, replace, "borrow 9:12 owner@objref | loan 11:9 view.id == 1 | reservation 10:25 owner@uniq");
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("makeRc")]
    [InlineData("makeArc")]
    public void WarmSlotUpdatesAllocateNothing(string factory)
    {
        var c = MinimalEmissionTest.Analyze(Source(factory, "@uniq"));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }

    private static string Source(string factory, string borrow)
        => Item + "\nvar first = Kimi.Intrinsics." + factory + "(Item.init(1))\nvar second = Kimi.Intrinsics." + factory + "(Item.init(2))\n" +
            "let old = Kimi.Intrinsics.exchange(first" + borrow + ", with: Kimi.Intrinsics." + factory + "(Item.init(3)))\n" +
            "Kimi.Intrinsics.swap(first" + borrow + ", second" + borrow + ")\nKimi.Intrinsics.replace(second" + borrow + ", with: old@move)\n" +
            "require first.id == 2 and second.id == 1 else => $abort(\"slots\")\nConsole.writeLine(\"updated\")";
}
