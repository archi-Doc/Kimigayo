// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class InheritedStorageTest
{
    [Fact]
    public void InheritedStandardStorageKeepsItsDeclaringLayer()
    {
        const string Source = """
            open struct Base
                public var count: i32
                protected init() => self.count = 3
            struct Leaf: Base
                public let extra: i32 = 7
                public init(): base() => ()
            var item = Leaf.init()
            require item.count == 3 and item.extra == 7 else => $abort("read")
            item.count = 5
            require item.count + item.extra == 12 else => $abort("write")
            let moved = item@move
            require moved.count == 5 else => $abort("move")
            """;
        ScalarEmissionTest.EmitFixture("InheritedStorageProjection", Source, string.Empty);
    }

    [Fact]
    public void BaseArgumentsCannotAccessTheConstructionReceiver()
    {
        const string Source = """
            open struct Base
                protected init(value: i32) => ()
            struct Leaf: Base
                let value: i32 = 42
                public init(): base(self.value) => ()
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InvalidAssignment_Kd);
    }

    // SPEC 6.2.3.4, 11.3.1: the construction receiver also forbids inherited Field and computed access, custom accessor calls and
    // a Non-Copy Move, each one Language record at the access.
    [Theory]
    [InlineData("open struct Base\n    public var x: i32 = 1\n    public init() => ()\nstruct Leaf: Base\n    var y: i32\n    public init()\n        self.y = self.x\n", "self.x")]
    [InlineData("open struct Base\n    public var x: i32 = 1\n    public init() => ()\nstruct Leaf: Base\n    var y: i32 = 0\n    public init()\n        self.x = 3\n", "self.x")]
    [InlineData("struct S\n    var a: i32\n    public computed b: i32\n        get() -> i32 => 5\n    public init()\n        self.a = self.b\n", "self.b")]
    [InlineData("struct S\n    public computed b: i32\n        get() -> i32 => 5\n        set(value: i32) -> () => ()\n    public init()\n        self.b = 1\n", "self.b")]
    [InlineData("struct S\n    var a: i32\n    public var b: i32 = 0\n        get() -> i32 => storage\n    public init()\n        self.a = self.b\n", "self.b")]
    [InlineData("struct S\n    public var b: i32 = 0\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value\n    public init()\n        self.b += 1\n", "self.b")]
    [InlineData("struct R\n    public var n: i32 = 0\n    drop => ()\nstruct S\n    var r: R\n    public init(r: R)\n        self.r = r@move\n        let moved = self.r@move\n", "self.r@move")]
    public void TheConstructionReceiverForbidsInheritedComputedAndMovingAccess(string declarations, string at)
    {
        var source = declarations + "public func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), source.LastIndexOf(at, StringComparison.Ordinal), at.Length), (error.Code, error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BaseConstructionPrecedesOwnInitializersAndDestructionFollowsOwnFields()
    {
        const string Source = """
            struct Resource
                drop => Console.writeLine("resource")
            group Helpers
                public func initialValue() -> i32
                    Console.writeLine("field")
                    return 42
            open struct Base
                let id: i32
                protected init(value: i32)
                    self.id = value
                    Console.writeLine("base init")
                drop
                    require self.id == 12 else => $abort("base storage")
                    Console.writeLine("base")
            struct Leaf: Base
                public let extra: i32 = Helpers.initialValue()
                let resource: Resource = Resource.init()
                public init(value: i32): base(value) => Console.writeLine("leaf init")
                drop => Console.writeLine("leaf")
            let item = Leaf.init(12)
            require item.extra == 42 else => $abort("own storage")
            """;
        NativeAllocationAudit.WriteFixture("InheritedStorageOrder", Source, 0, 0, 0, "base init\nfield\nleaf init\nleaf\nresource\nbase\n");
    }

    [Fact]
    public void NestedBasePrefixesKeepTheirPaddingAndDestructors()
    {
        const string Source = """
            open struct Base
                let small: i8
                let large: i64
                protected init()
                    self.small = 7
                    self.large = 123456
                drop
                    require self.small == 7 and self.large == 123456 else => $abort("prefix")
                    Console.writeLine("base")
            open struct Middle: Base
                let extra: i8
                protected init(): base() => self.extra = 11
                drop
                    require self.extra == 11 else => $abort("middle")
                    Console.writeLine("middle")
            struct Leaf: Middle
                public let extraLeaf: i32
                public init(): base() => self.extraLeaf = 23
                drop => Console.writeLine("leaf")
            let item = Leaf.init()
            require item.extraLeaf == 23 else => $abort("leaf")
            """;
        ScalarEmissionTest.EmitFixture("InheritedStoragePadding", Source, "leaf\nmiddle\nbase\n");
    }

    [Fact]
    public void ZeroSizedBaseStillRunsItsDestructor()
    {
        const string Source = """
            open struct Base
                protected init() => ()
                drop => Console.writeLine("base")
            struct Leaf: Base
                public let extra: i64
                public init(): base() => self.extra = 42
            let item = Leaf.init()
            require item.extra == 42 else => $abort("own storage")
            """;
        ScalarEmissionTest.EmitFixture("InheritedStorageZero", Source, "base\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MovingEveryOwnFieldStillDestroysTheCompleteBaseRemainder()
    {
        const string Source = """
            open struct Base
                let name: string
                protected init() => self.name = "base"
                drop => Console.writeLine(self.name)
            struct Leaf: Base
                public let text: string
                public init(): base() => self.text = "own"
            func run(move: bool)
                let item = Leaf.init()
                if move
                    let taken = item.text@move
                    Console.writeLine(taken)
            run(true)
            run(false)
            """;
        NativeAllocationAudit.WriteFixture("InheritedStoragePartial", Source, 0, 0, 0, "own\nbase\nbase\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DynamicTypeTestsUseTheVerifiedBaseMapAndDestroyTheCompletePayload()
    {
        const string Source = """
            open struct Base
                let value: i32
                protected init() => self.value = 10
                drop
                    require self.value == 10 else => $abort("base contents")
                    Console.writeLine("base")
            struct Leaf: Base
                let extra: i32
                public init(): base() => self.extra = 20
                drop
                    require self.extra == 20 else => $abort("leaf contents")
                    Console.writeLine("leaf")
            struct Other
            let owner = Kimi.Intrinsics.makeObj(Leaf.init())
            require owner is Leaf and owner is Base and owner is not Other else => $abort("dynamic identity")
            """;
        NativeAllocationAudit.WriteFixture("InheritedStorageObject", Source, 1, 1, 24, "leaf\nbase\n");
    }
}
