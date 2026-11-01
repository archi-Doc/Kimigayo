// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void DynamicViewsKeepTheirArrayProtected()
    {
        var c = MinimalEmissionTest.Analyze(Item + "var stored: [1 of obj/Item] = [Kimi.Intrinsics.makeObj(Item.init())]\nfunc index() -> usize => 0\nlet view = stored[index()]@objref\nlet moved = stored@move\nrequire view.id == 7 else => $abort(\"live\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmStoredViewAnalysisAndEmissionAllocateNothing(bool stored)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.ObjectView(stored));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }

    // PD17 (DECISIONS R5-M110): runtime identity is the Origin-erased Type numbered in the module, and no package-qualified key text is
    // written, so a changed package identity rebinds to the same module text; an edit that takes Root off the chain numbered before Item's
    // shifts Item's number (Other 1, Root 2, Item 3, then Item 2) at the same descriptor item, and the reused module writes the current one.
    [Fact]
    public void PhysicalReuseRechecksTheCurrentRuntimeIdentity()
    {
        const string Root = "open struct Root\n    public let code: i64 = 9\n    protected init() => ()\n" +
            "struct Keep : Root\n    public init() : base() => ()\n";
        const string Based = "struct Other : Root\n    public init() : base() => ()\n";
        const string Unbased = "struct Other\n    public let code: i64 = 9\n    public init() => ()\n";
        const string Program = "let k = Keep.init()\nlet first = Kimi.Intrinsics.makeObj(Other.init())\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\n" +
            "let stored = (owner@move, 1)\nrequire stored.0.id == 7 else => $abort(\"read\")";
        var c = MinimalEmissionTest.Analyze(Root + Based + Item + Program);
        var initial = CompilationTestHelper.WriteIr(c);
        c.Project.ProjectFile.PackageId = "changed.identity";
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified);
        var changed = CompilationTestHelper.WriteIr(c);
        Assert.Equal(initial, changed);
        Assert.DoesNotContain("changed.identity", changed, StringComparison.Ordinal);
        Assert.Equal(changed, CompilationTestHelper.WriteIr(c));

        c.Kotonoha.RootKoto.Clear();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, Root + Unbased + Item + Program);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified);
        var (descriptor, number) = Identity(changed);
        Assert.Equal("3", number);
        Assert.Equal((descriptor, "2"), Identity(CompilationTestHelper.WriteIr(c)));
    }

    // Item's descriptor and runtime identity number in a module's text: its payload metadata's (an i32 layout, not Copy, a destroy entry).
    private static (string Descriptor, string Number) Identity(string ir)
    {
        var metadata = Regex.Match(ir, @"(?m)^@__kimi_object_metadata(\d+) = private constant \{[^}]*\} \{ i64 (\d+), i64 4, i64 4, i64 0, ptr @");
        Assert.True(metadata.Success);
        return (metadata.Groups[1].Value, metadata.Groups[2].Value);
    }
}
