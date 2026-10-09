// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedObjectAdaptationTest
{
    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void SharedViewsAdaptAtFixedValuePositions(string mode)
    {
        var c = CompilationTestHelper.Parse($"struct Item\n    public let id: i32 = 7\n    public func read(self: ref/Self) -> i32 => self.id\nfunc inspect(value: objref/Item) -> i32 => value.read()\nfunc run(owner: {mode}/Item)\n    let first: objref/Item = owner\n    var second: objref/Item = first\n    second = owner\n    let pair: (objref/Item, i32) = (owner, 9)\n    _ = inspect(owner)\n    _ = owner.read()");
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Fact]
    public void ObjectReborrowsKeepResultDependencies()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nfunc view(value: objuniq/Item) -> objref/Item during value => value\nvar owner = Kimi.Intrinsics.makeObj(Item.init())\nlet result = view(owner@objuniq)\nrequire result.id == 7 else => $abort(\"view\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var issue), MinimalEmissionTest.Describe(c, issue));
    }

    [Theory]
    [InlineData("rc")]
    [InlineData("arc")]
    public void SharedHandlesNeverAdaptToExclusiveViews(string mode)
    {
        var c = CompilationTestHelper.Parse($"struct Item\n    public var id: i32 = 7\nfunc edit(value: objuniq/Item) => ()\nfunc run(owner: {mode}/Item) => edit(owner)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void SharedViewAdaptationDoesNotUpcast(string mode)
    {
        const string Types = "open struct Base\n    public let id: i32 = 7\n    protected init() => ()\nstruct Child: Base\n    public init() => ()\nfunc inspect(value: objref/Base) => ()\n";
        var argument = CompilationTestHelper.Parse(Types + $"func run(owner: {mode}/Child) => inspect(owner)");
        Assert.False(argument.Bind().IsComplete);
        Assert.Equal(DiagnosticCode.NoApplicableOverload_Kd, Assert.Single(argument.Binding.Issues).Code);
        var annotation = CompilationTestHelper.Parse(Types + $"func run(owner: {mode}/Child)\n    let view: objref/Base = owner");
        Assert.False(annotation.Bind().IsComplete);
        Assert.Equal(DiagnosticCode.TypeMismatch_Kd, Assert.Single(annotation.Binding.Issues).Code);
        Assert.True(CompilationTestHelper.Parse(Types + $"func run(owner: {mode}/Child) => inspect(owner@objref/Base)").Bind().IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ImplicitObjectViewsExecuteWithoutExtraAllocations()
    {
        const string Source = """
            struct Item
                public let id: i32 = 7
                drop => Console.writeLine("drop")
            func inspect(value: objref/Item) -> i32 => value.id
            func view(value: objuniq/Item) -> objref/Item during value => value
            var owner = Kimi.Intrinsics.makeObj(Item.init())
            let first: objref/Item = owner
            var second: objref/Item = first
            second = owner
            let pair: (objref/Item, i32) = (owner, 9)
            require inspect(owner) == 7 and first.id == 7 and second.id == 7 and pair.0.id == 7 else => $abort("views")
            require view(owner@objuniq).id == 7 else => $abort("result")
            """;
        // Allocation size is the 16-byte header plus the 4-byte payload; no trailing alignment padding is needed.
        NativeAllocationAudit.WriteFixture("ObjectImplicitViews", Source, 1, 1, 20, "drop\n");
    }

    [Fact]
    public void ImplicitSharedViewsKeepTheirOwnersLive()
    {
        var c = MinimalEmissionTest.Analyze("struct Item\n    public let id: i32 = 7\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet view: objref/Item = owner\nlet moved = owner@move\nrequire view.id == 7 else => $abort(\"live\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AnAnnotationCannotBorrowAnOwnerExclusively()
    {
        var c = CompilationTestHelper.Parse("struct Item\nvar owner = Kimi.Intrinsics.makeObj(Item.init())\nlet view: objuniq/Item = owner");
        Assert.False(c.Bind().IsComplete);
    }
}
