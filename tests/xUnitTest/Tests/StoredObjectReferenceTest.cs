// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StoredObjectReferenceTest
{
    private const string Item = "struct Item\n    public var id: i32 = 7\n    public func read(self: ref/Self) -> i32 => self.id\n    drop => Console.writeLine(\"drop\")\n";
    private const string Owner = "let owner = Kimi.Intrinsics.makeObj(Item.init())\n";

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Shared", "let view = owner@objref\nlet slot = view@ref\nrequire slot.id == 7 else => $abort(\"field\")")]
    [InlineData("Nested", "let view = owner@objref\nlet slot = view@ref\nlet outer = slot@ref\nrequire outer.id == 7 else => $abort(\"field\")")]
    [InlineData("ExclusiveSlot", "var view = owner@objref\nlet slot = view@uniq\nrequire slot.id == 7 else => $abort(\"field\")")]
    [InlineData("Method", "let view = owner@objref\nlet slot = view@ref\nrequire slot.read() == 7 else => $abort(\"method\")")]
    [InlineData("Enum", "let value: Option<objref/Item> = .Some(owner)\nmatch value\n    .Some(let view)\n        require view.id == 7 else => $abort(\"field\")\n    .None => $abort(\"missing\")")]
    public void ObjectReceiversReadTheSharedViewThroughReferenceLayers(string name, string use)
        => NativeAllocationAudit.WriteFixture("StoredObjectReference" + name, Item + Owner + use, 1, 1, 20, "drop\n");

    [Fact]
    public void TheLoadedViewKeepsTheExternalOwnerLoan()
    {
        var c = MinimalEmissionTest.Analyze(Item + Owner + "let view = owner@objref\nlet slot = view@ref\nlet field = slot.id@ref\nlet moved = owner@move\nrequire field == 7 else => $abort(\"field\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void TheLoadedViewDoesNotRetainTheOuterSlot()
        => NativeAllocationAudit.WriteFixture("StoredObjectReferenceIndependent", Item + Owner + "var view = owner@objref\nlet slot = view@ref\nlet field = slot.id@ref\nview = owner@objref\nrequire field == 7 else => $abort(\"field\")", 1, 1, 20, "drop\n");

    [Theory]
    [InlineData("let field = slot.id@uniq")]
    [InlineData("slot.id = 9")]
    public void ASharedObjectViewDoesNotGainExclusiveAuthority(string use)
    {
        var c = MinimalEmissionTest.Analyze(Item + Owner + "var view = owner@objref\nlet slot = view@uniq\n" + use);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmReferenceLayerPlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Item + Owner + "let view = owner@objref\nlet slot = view@ref\nrequire slot.id == 7 else => $abort(\"field\")");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }
}
