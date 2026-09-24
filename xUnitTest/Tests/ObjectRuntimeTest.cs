// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ObjectRuntimeTest
{
    [Fact]
    public void PayloadExchangePreservesRuntimeIdentityAndReleasesOneOriginalAllocation()
    {
        const string Source = """
            struct Counter
                public let value: i32
                public init(value: i32) => self.value = value
                deinit
                    if self.value == 1 => Console.writeLine("old")
                    else => Console.writeLine("new")
            struct Other
                public let value: i32 = 0
            func inspect(view: objref/Counter) -> bool => view is Counter and view is not Other
            func update(view: objuniq/Counter)
                require view is Counter else => $abort("exclusive Type")
                do
                    let old = Kimi.Intrinsics.exchange(view@uniq/Counter, with: Counter.init(2))
                    require old.value == 1 else => $abort("old payload")
                    let payload = view@ref/Counter
                    require payload.value == 2 else => $abort("new payload")
                require view is Counter else => $abort("changed identity")
            var owner = Kimi.Intrinsics.makeObj(Counter.init(1))
            require owner is Counter and owner is not Other else => $abort("owner Type")
            require inspect(owner@objref/Counter) else => $abort("shared Type")
            update(owner@objuniq/Counter)
            require owner is Counter else => $abort("final Type")
            """;
        NativeAllocationAudit.WriteFixture("ObjectRuntimeExchange", Source, 1, 1, 20, "old\nnew\n");
    }

    [Fact]
    public void EqualLayoutsAndDistinctInstantiationsRetainDifferentTypeIdentities()
    {
        const string Source = """
            struct Box<T>
            struct Other
            let first = Kimi.Intrinsics.makeObj(Box<i32>.init())
            let second = Kimi.Intrinsics.makeObj(Box<u32>.init())
            require first is Box<i32> and first is not Box<u32> else => $abort("first identity")
            require second is Box<u32> and second is not Box<i32> else => $abort("second identity")
            require first is not Other else => $abort("uncreated identity")
            """;
        NativeAllocationAudit.WriteFixture("ObjectRuntimeIdentity", Source, 2, 2, 32);
    }

    [Fact]
    public void CallResultsAreEvaluatedOnceAndTemporaryOwnersAreDestroyed()
    {
        const string Source = """
            struct Item
                deinit => Console.writeLine("destroyed")
            func make() -> obj/Item
                Console.writeLine("created")
                return Kimi.Intrinsics.makeObj(Item.init())
            func observe(view: objref/Item) -> objref/Item during view
                Console.writeLine("observed")
                return view
            require make() is Item else => $abort("temporary")
            let owner = make()
            require observe(owner@objref/Item) is Item else => $abort("result")
            """;
        NativeAllocationAudit.WriteFixture("ObjectRuntimeEvaluation", Source, 2, 2, 32, "created\ndestroyed\ncreated\nobserved\ndestroyed\n");
    }

    [Fact]
    public void RuntimeTestsCannotBypassAnOutstandingExclusiveBorrow()
    {
        const string Source = """
            struct Item
            var owner = Kimi.Intrinsics.makeObj(Item.init())
            let view = owner@objuniq/Item
            require owner is Item else => $abort("Type")
            _ = view@move
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
