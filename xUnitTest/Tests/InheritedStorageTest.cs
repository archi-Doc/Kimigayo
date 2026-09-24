// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class InheritedStorageTest
{
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

    [Fact]
    public void BaseConstructionPrecedesOwnInitializersAndDestructionFollowsOwnFields()
    {
        const string Source = """
            struct Resource
                deinit => Console.writeLine("resource")
            group Helpers
                public func initialValue() -> i32
                    Console.writeLine("field")
                    return 42
            open struct Base
                let id: i32
                protected init(value: i32)
                    self.id = value
                    Console.writeLine("base init")
                deinit
                    require self.id == 12 else => $abort("base storage")
                    Console.writeLine("base")
            struct Leaf: Base
                public let extra: i32 = Helpers.initialValue()
                let resource: Resource = Resource.init()
                public init(value: i32): base(value) => Console.writeLine("leaf init")
                deinit => Console.writeLine("leaf")
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
                deinit
                    require self.small == 7 and self.large == 123456 else => $abort("prefix")
                    Console.writeLine("base")
            open struct Middle: Base
                let extra: i8
                protected init(): base() => self.extra = 11
                deinit
                    require self.extra == 11 else => $abort("middle")
                    Console.writeLine("middle")
            struct Leaf: Middle
                public let extraLeaf: i32
                public init(): base() => self.extraLeaf = 23
                deinit => Console.writeLine("leaf")
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
                deinit => Console.writeLine("base")
            struct Leaf: Base
                public let extra: i64
                public init(): base() => self.extra = 42
            let item = Leaf.init()
            require item.extra == 42 else => $abort("own storage")
            """;
        ScalarEmissionTest.EmitFixture("InheritedStorageZero", Source, "base\n");
    }

    [Fact]
    public void MovingEveryOwnFieldStillDestroysTheCompleteBaseRemainder()
    {
        const string Source = """
            open struct Base
                let name: string
                protected init() => self.name = "base"
                deinit => Console.writeLine(self.name)
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

    [Fact]
    public void DynamicTypeTestsUseTheVerifiedBaseMapAndDestroyTheCompletePayload()
    {
        const string Source = """
            open struct Base
                let value: i32
                protected init() => self.value = 10
                deinit
                    require self.value == 10 else => $abort("base contents")
                    Console.writeLine("base")
            struct Leaf: Base
                let extra: i32
                public init(): base() => self.extra = 20
                deinit
                    require self.extra == 20 else => $abort("leaf contents")
                    Console.writeLine("leaf")
            struct Other
            let owner = Kimi.Intrinsics.makeObj(Leaf.init())
            require owner is Leaf and owner is Base and owner is not Other else => $abort("dynamic identity")
            """;
        NativeAllocationAudit.WriteFixture("InheritedStorageObject", Source, 1, 1, 24, "leaf\nbase\n");
    }
}
