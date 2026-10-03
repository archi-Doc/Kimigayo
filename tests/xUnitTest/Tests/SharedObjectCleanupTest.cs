// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedObjectCleanupTest
{
    private const string Item = "struct Item\n    public let value: i32 = 7\n    drop => Console.writeLine(\"drop\")\n";

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BaseUpcastAndCloneKeepDynamicIdentityAndFinalDestruction()
    {
        const string Source = """
            open struct Base
                public let value: i32
                protected init(value: i32) => self.value = value
                drop => Console.writeLine("base")
            struct Leaf: Base
                public let extra: i32
                public init(): base(7) => self.extra = 9
                drop => Console.writeLine("leaf")
            func inspect(view: objref/Base)
                require view.value == 7 and view is Leaf else => $abort("identity")
            let copy = label result: do
                let owner = Kimi.Intrinsics.makeRc(Leaf.init())
                let widened = owner@rc/Base
                inspect(widened@objref)
                exit to result Kimi.Intrinsics.clone(widened@ref)
            inspect(copy@objref)
            require copy is Leaf and copy is Base else => $abort("type")
            Console.writeLine("alive")
            """;
        NativeAllocationAudit.WriteFixture("SharedRcBaseIdentity", Source, 1, 1, 24, "alive\nleaf\nbase\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Clear", "values@uniq.clear()")]
    [InlineData("OwningExit", "for item in values@move\n    require item.value == 7 else => $abort(\"item\")\n    exit")]
    public void DynamicArrayReleasesEveryStoredReference(string name, string cleanup)
    {
        var source = Item + "let owner = Kimi.Intrinsics.makeRc(Item.init())\nvar values = Array<rc/Item>.init(capacity: 2)\n" +
            "values@uniq.append(Kimi.Intrinsics.clone(owner@ref))\nvalues@uniq.append(Kimi.Intrinsics.clone(owner@ref))\n" +
            "_ = owner@move\n" + cleanup + "\nConsole.writeLine(\"done\")";
        // The Windows Array reserve policy rounds the initial nonempty capacity to four pointer-sized slots.
        NativeAllocationAudit.WriteFixture("SharedRcArray" + name, source, 2, 2, 52, "drop\ndone\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AbandonedArgumentsReleaseAcquiredClones()
    {
        const string Source = Item + "func take(value: rc/Item, rest: ()) => $abort(\"unreachable\")\n" +
            "func run()\n    let owner = Kimi.Intrinsics.makeRc(Item.init())\n    take(Kimi.Intrinsics.clone(owner@ref), (return))\nrun()\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture("SharedRcAbandonedArgument", Source, 1, 1, 20, "drop\ndone\n");
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    public void RawStoredHandlesStillRequireExplicitMoves(string mode)
    {
        var c = MinimalEmissionTest.Analyze(Item + $"func bad(value: raw/({mode}/Item)) -> {mode}/Item\n    unsafe => return value[0]\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.TransferRequired);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ExclusiveHandlesUseTheSameRawStorageMove()
    {
        const string Source = Item + "var values = Array<obj/Item>.init(capacity: 1)\nvalues@uniq.append(Kimi.Intrinsics.makeObj(Item.init()))\nfor item in values@move\n    require item.value == 7 else => $abort(\"item\")\n    exit";
        NativeAllocationAudit.WriteFixture("SharedRcObjOwningIteration", Source, 2, 2, 52, "drop\n");
    }
}
