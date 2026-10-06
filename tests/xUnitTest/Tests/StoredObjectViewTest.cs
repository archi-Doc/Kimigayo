// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class StoredObjectViewTest
{
    private const string Item = VerificationWorkloads.ObjectItem;

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Tuple", "let stored = (owner@move, 1)\nrequire stored.0.id == 7 else => $abort(\"tuple\")")]
    [InlineData("Array", "let stored: [1 of obj/Item] = [owner@move]\nrequire stored[0].id == 7 else => $abort(\"array\")")]
    [InlineData("Nested", "let stored: [1 of (obj/Item, i32)] = [(owner@move, 1)]\nrequire stored[0].0.id == 7 else => $abort(\"nested\")")]
    [InlineData("Capture", "let stored = (owner@move, 1)\nlet f = func [stored@move] () => stored.0.id\nrequire f() == 7 and f() == 7 else => $abort(\"capture\")")]
    [InlineData("Explicit", "let stored = (owner@move, 1)\nlet view = stored.0@objref\nrequire view.id == 7 else => $abort(\"view\")")]
    [InlineData("Argument", "let stored = (owner@move, 1)\nfunc inspect(value: objref/Item) -> i32 => value.id\nrequire inspect(stored.0) == 7 else => $abort(\"argument\")")]
    [InlineData("Method", "let stored = (owner@move, 1)\nrequire stored.0.read() == 7 else => $abort(\"method\")")]
    [InlineData("Dynamic", "let stored: [1 of obj/Item] = [owner@move]\nfunc index() -> usize => 0\nrequire stored[index()].id == 7 else => $abort(\"index\")")]
    [InlineData("SavedPosition", "let stored: [1 of obj/Item] = [owner@move]\nlet last = ^1\nrequire stored[last].id == 7 else => $abort(\"position\")")]
    [InlineData("FromEnd", "let stored: [1 of obj/Item] = [owner@move]\nrequire stored[^1].id == 7 else => $abort(\"position\")")]
    [InlineData("Struct", "struct Box\n    public let item: obj/Item\n    public init(item: obj/Item) => self.item = item@move\nlet stored = Box.init(owner@move)\nrequire stored.item.id == 7 else => $abort(\"field\")")]
    [InlineData("BorrowedStruct", "struct Box\n    public let item: obj/Item\n    public init(item: obj/Item) => self.item = item@move\nfunc inspect(box: ref/Box) -> i32 => box.item.id\nlet stored = Box.init(owner@move)\nrequire inspect(stored) == 7 else => $abort(\"borrowed field\")")]
    [InlineData("BorrowedArray", "let stored: [1 of obj/Item] = [owner@move]\nfunc inspect(items: ref/[1 of obj/Item], index: isize) -> i32 => items[index].id\nrequire inspect(stored, 0) == 7 else => $abort(\"borrowed array\")")]
    public void StoredHandlesLendWithoutCopyingOrAllocating(string name, string source)
        => NativeAllocationAudit.WriteFixture("StoredObjectView" + name, Item + "let owner = Kimi.Intrinsics.makeObj(Item.init())\n" + source, 1, 1, 20, "drop\n");

    [Theory]
    [InlineData("let moved = stored@move")]
    [InlineData("stored.0 = Kimi.Intrinsics.makeObj(Item.init())")]
    public void StoredViewsKeepTheirHandleSlotProtected(string conflict)
    {
        var c = MinimalEmissionTest.Analyze(Item + "let owner = Kimi.Intrinsics.makeObj(Item.init())\nvar stored = (owner@move, 1)\nlet view = stored.0@objref\n" + conflict + "\nrequire view.id == 7 else => $abort(\"live\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void DynamicViewsKeepTheirArrayProtected()
    {
        var c = MinimalEmissionTest.Analyze(Item + "var stored: [1 of obj/Item] = [Kimi.Intrinsics.makeObj(Item.init())]\nfunc index() -> usize => 0\nlet view = stored[index()]@objref\nlet moved = stored@move\nrequire view.id == 7 else => $abort(\"live\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmStoredViewAnalysisAndEmissionAllocateNothing(bool stored)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.ObjectView(stored));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Fact]
    public void PhysicalReuseRechecksTheCurrentRuntimeIdentity()
    {
        var c = MinimalEmissionTest.Analyze(Item + "let owner = Kimi.Intrinsics.makeObj(Item.init())\nlet stored = (owner@move, 1)\nrequire stored.0.id == 7 else => $abort(\"read\")");
        var initial = CompilationTestHelper.WriteIr(c);
        c.Project.ProjectFile.PackageId = "changed.identity";
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.Emission.Validate(out _));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified);
        var changed = CompilationTestHelper.WriteIr(c);
        Assert.NotEqual(initial, changed);
        Assert.True(c.Emission.TryPrepare(out var module, out _));
        Assert.Contains("changed.identity", module.Constants[Assert.Single(module.Objects).TypeKey].Value, StringComparison.Ordinal);
        Assert.Equal(changed, CompilationTestHelper.WriteIr(c));
    }
}
