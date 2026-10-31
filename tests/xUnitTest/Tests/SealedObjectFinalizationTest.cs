// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SealedObjectFinalizationTest
{
    private const string RecursiveNodes = """
        struct Node
            public let next: Option<rc/Node>
            public init(next: Option<rc/Node>) => self.next = next@move
            drop => Console.writeLine("node")
        let tail = Kimi.Intrinsics.makeRc(Node.init(Option<rc/Node>.None))
        let head = Kimi.Intrinsics.makeRc(Node.init(Option<rc/Node>.Some(tail@move)))
        """;

    // A Sealed payload's glue destroys its parts, whose handles release the next payload through the same glue (SPEC 16.3.2; package C
    // 3.6), so recursive data is destroyed node by node, each allocation released once: an rc, arc or obj chain, and an enum whose Case
    // holds the handle to the next. The obj nodes and the enum's items are named, so their order is fixed too: a node's destructor runs
    // before its Fields (the next node), and a Case's payload elements go last to first (the rc tail before the item).
    public static TheoryData<string, string, int, long, string> RecursivePayloads => new()
    {
        { "Rc", RecursiveNodes, 2, 64, "node\nnode\n" },
        { "Arc", SharedObjectRuntimeTest.ArcSource(RecursiveNodes), 2, 64, "node\nnode\n" },
        {
            "Obj", "struct Node\n    public let name: string\n    public let next: Option<obj/Node>\n" +
            "    public init(name: string, next: Option<obj/Node>)\n        self.name = name@move\n        self.next = next@move\n" +
            "    drop => Console.writeLine(self.name)\nlet tail = Kimi.Intrinsics.makeObj(Node.init(\"tail\", Option<obj/Node>.None))\n" +
            "let head = Kimi.Intrinsics.makeObj(Node.init(\"head\", Option<obj/Node>.Some(tail@move)))", 2, 112, "head\ntail\n"
        },
        {
            "Enum", "struct Item\n    public let name: string\n    public init(name: string) => self.name = name@move\n" +
            "    drop => Console.writeLine(self.name)\nenum List\n    Cons(Item, rc/List)\n    Nil\nlet nil = Kimi.Intrinsics.makeRc(List.Nil)\n" +
            "let inner = Kimi.Intrinsics.makeRc(List.Cons(Item.init(\"inner\"), nil@move))\n" +
            "let outer = Kimi.Intrinsics.makeRc(List.Cons(Item.init(\"outer\"), inner@move))", 3, 168, "inner\nouter\n"
        },
    };

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void SealedPayloadsHaveDirectCleanupAndRelease(string factory)
    {
        // SPEC 16.3.3 (DECISIONS R5-M112, R5-M145): the payload's destructor runs once and its one allocation is released once, natively;
        // the handle glue's direct release is FunctionCodegenTest.AHandlesGlueReleasesItsObject's (R5-M12).
        var source = "struct Item\n    public let value: i32 = 7\n    drop => Console.writeLine(\"drop\")\n" +
            $"let owner = Kimi.Intrinsics.make{factory}(Item.init())\nrequire owner.value == 7 else => $abort(\"payload\")";
        NativeAllocationAudit.WriteFixture("SealedObjectDirect" + factory, source, 1, 1, 20, "drop\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj", "7", -1, 20)]
    [InlineData("Rc", "7", -1, 20)]
    [InlineData("Obj", "\"value\"", -2, 40)]
    [InlineData("Rc", "\"value\"", -2, 40)]
    [InlineData("Arc", "7", -1, 20)]
    [InlineData("Arc", "\"value\"", -2, 40)]
    public void TrivialAndStringPayloadsKeepTheirDistinctCleanup(string factory, string value, int drop, int bytes)
    {
        var source = $"let owner = Kimi.Intrinsics.make{factory}({value})";
        NativeAllocationAudit.WriteFixture("SealedObjectPayload" + factory + (drop == -1 ? "Trivial" : "String"), source, 1, 1, bytes);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Obj")]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void ZeroSizeStillDestroysTheLogicalPayload(string factory)
        => NativeAllocationAudit.WriteFixture("SealedObjectZero" + factory, "struct Item\n    drop => Console.writeLine(\"zero\")\n" + $"let owner = Kimi.Intrinsics.make{factory}(Item.init())", 1, 1, 16, "zero\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DifferentSealedDestructorsAndModesKeepTheirPlansAcrossWarmEmission()
    {
        const string Source = """
            struct First
                drop => Console.writeLine("first")
            struct Second
                drop => Console.writeLine("second")
            let first = Kimi.Intrinsics.makeObj(First.init())
            let second = Kimi.Intrinsics.makeRc(Second.init())
            let clone = Kimi.Intrinsics.clone(second@ref)
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
        NativeAllocationAudit.WriteFixture("SealedObjectMixed", Source, 2, 2, 32, "second\nfirst\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [MemberData(nameof(RecursivePayloads))]
    public void RecursivePayloadsKeepFinitePlansAndDestroyEveryNode(string name, string source, int allocations, long bytes, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
        NativeAllocationAudit.WriteFixture("SealedObjectRecursive" + name, source, allocations, allocations, bytes, stdout);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void NestedSealedPayloadsReleaseTheirOwnAllocations()
    {
        const string Source = """
            struct Item
                drop => Console.writeLine("item")
            let child = Kimi.Intrinsics.makeRc(Item.init())
            let parent = Kimi.Intrinsics.makeObj((child@move, 7))
            """;
        NativeAllocationAudit.WriteFixture("SealedObjectNested", Source, 2, 2, 48, "item\n");
    }
}
