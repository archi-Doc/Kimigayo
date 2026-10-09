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

    // SPEC 3.6.1, 7.3, 10.2, 13.5.5.1, 13.5.5.2: a temporary handle is materialized like an owned temporary and lends its object or
    // payload for the operation: explicit and implicit object borrows, payload follows, shared receivers and a moved handle.
    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void TemporaryHandlesLendLikeOwnedPlaces(string mode)
    {
        var factory = "make" + char.ToUpperInvariant(mode[0]) + mode[1..];
        var source = "struct Item\n    public var id: i32 = 7\n    public func read(self: ref/Self) -> i32 => self.id\n    public func look(self: objref/Self) -> i32 => self.id\n" +
            $"func inspect(v: objref/Item) -> i32 => v.id\nfunc make() -> {mode}/Item => Kimi.Intrinsics.{factory}(Item.init())\n" +
            "require inspect(make()@objref) == 7 and inspect(make()) == 7 and make().read() == 7 and make().look() == 7 and make()@follow@ref.id == 7 else => $abort(\"lend\")\n" +
            $"let x = make()@follow@ref.id\nlet h = make()\nrequire x == 7 and inspect(h@move@objref) == 7 and inspect(Item.init()@{mode}@objref) == 7 else => $abort(\"more\")";
        var c = MinimalEmissionTest.Analyze(source);
        var valid = c.Emission.Validate(out var failure);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified && valid, MinimalEmissionTest.Describe(c, failure));
    }

    // SPEC 7.3, 10.2, 15.1.5: a temporary obj lends exclusively when spelled or as a receiver, never implicitly at an argument; rc and arc
    // lend no exclusive access even for a temporary at count one.
    [Theory]
    [InlineData("edit(Kimi.Intrinsics.makeObj(Item.init())@objuniq)\n_ = Kimi.Intrinsics.makeObj(Item.init()).bump()\nKimi.Intrinsics.makeObj(Item.init())@follow@uniq.id = 5\nbumpField(Kimi.Intrinsics.makeObj(Item.init()).id@uniq)", null, null)]
    [InlineData("edit(Kimi.Intrinsics.makeObj(Item.init()))", "NoApplicableOverload_Kd", "edit(Kimi.Intrinsics.makeObj(Item.init()))")]
    [InlineData("readMut(Kimi.Intrinsics.makeObj(Item.init())@follow)", "ExclusiveBorrowRequired_Kd", "Kimi.Intrinsics.makeObj(Item.init())@follow")]
    [InlineData("edit(Kimi.Intrinsics.makeRc(Item.init())@objuniq)", "SharedPathAccess_Kd", "Kimi.Intrinsics.makeRc(Item.init())")]
    [InlineData("Kimi.Intrinsics.makeArc(Item.init())@follow@uniq.id = 1", "SharedPathAccess_Kd", "Kimi.Intrinsics.makeArc(Item.init())@follow@uniq")]
    [InlineData("_ = Kimi.Intrinsics.makeRc(Item.init()).bump()", "NoApplicableOverload_Kd", "Kimi.Intrinsics.makeRc(Item.init()).bump()")]
    public void TemporaryHandlesLendExclusivelyOnlyWithAuthorityAndSpelling(string use, string? code, string? at)
    {
        var source = "struct Item\n    public var id: i32 = 7\n    public func bump(self: uniq/Self) -> i32\n        self.id += 1\n        return self.id\n" +
            "func edit(v: objuniq/Item) => v.id += 10\nfunc readMut(v: uniq/Item) -> i32 => v.id\nfunc bumpField(v: uniq/i32) => v@follow += 1\n" + use;
        if (code is null)
        {
            var c = MinimalEmissionTest.Analyze(source);
            var valid = c.Emission.Validate(out var failure);
            Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified && valid, MinimalEmissionTest.Describe(c, failure));
            return;
        }

        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // SPEC 11.2.3: a getter result is a Temporary Place lent only shared, also when it is a handle whose slot an explicit @uniq borrows.
    [Fact]
    public void GetterResultHandlesAreLentSharedOnly()
    {
        const string Source = "struct Item\n    public var id: i32 = 7\nstruct Holder\n    public computed made: rc/Item\n        get(self: ref/Self) -> rc/Item => Kimi.Intrinsics.makeRc(Item.init())\nfunc take(v: uniq/rc/Item) -> i32 => v.id\nlet h = Holder.init()\nlet a = take(h.made@uniq)";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), "h.made@uniq"), (error.Code, Source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // Temporary handles release at their full-expression boundary, after the call that borrows them, in reverse creation order.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void TemporaryHandleBorrowsReleaseAtTheirBoundary()
    {
        const string Source = "struct Item\n    public var id: i32\n    public init(id: i32) => self.id = id\n    public func read(self: ref/Self) -> i32 => self.id\n    public func look(self: objref/Self) -> i32 => self.id\n    drop => Console.writeLine(\"drop\")\n" +
            "func inspect(v: objref/Item) -> i32 => v.id\nfunc pair(a: objref/Item, b: objref/Item) -> i32\n    Console.writeLine(\"call\")\n    return a.id + b.id\n" +
            "require inspect(Kimi.Intrinsics.makeRc(Item.init(1))@objref) == 1 else => $abort(\"a\")\nConsole.writeLine(\"a\")\nrequire inspect(Kimi.Intrinsics.makeRc(Item.init(2))) == 2 else => $abort(\"b\")\nConsole.writeLine(\"b\")\n" +
            "require Kimi.Intrinsics.makeRc(Item.init(3)).read() == 3 else => $abort(\"c\")\nConsole.writeLine(\"c\")\nrequire Kimi.Intrinsics.makeRc(Item.init(4)).look() == 4 else => $abort(\"d\")\nConsole.writeLine(\"d\")\n" +
            "let n = Kimi.Intrinsics.makeRc(Item.init(5))@follow@ref.id\nConsole.writeLine(\"e\")\nrequire n == 5 and pair(Kimi.Intrinsics.makeRc(Item.init(6))@objref, Kimi.Intrinsics.makeRc(Item.init(7))) == 13 else => $abort(\"f\")";
        SharedObjectRuntimeTest.WriteModes("TemporaryBorrows", Source, 7, 7, 140, "drop\na\ndrop\nb\ndrop\nc\ndrop\nd\ndrop\ne\ncall\ndrop\ndrop\n");
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
