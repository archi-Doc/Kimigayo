// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class NestedCollectionTest
{
    [Theory]
    [InlineData("ArrayDictionary", "let values = [[1: 2]]", 2, 320)]
    [InlineData("DictionaryArray", "let values = [1: [2]]", 2, 208)]
    [InlineData("DictionaryDictionary", "let values = [1: [2: 3]]", 2, 416)]
    [InlineData("ArrayArray", "let values = [[1]]", 2, 112)]
    [InlineData("Tuple", "let values = ([1: 2], [3: 4])", 2, 192)]
    [InlineData("FixedArray", "let values: [2 of Dictionary<i32, i32>] = [[1: 2], [3: 4]]", 2, 192)]
    [InlineData("Option", "let values: Option<Dictionary<i32, i32>> = .Some([1: 2])", 1, 96)]
    [InlineData("Struct", "struct Box\n    public let entries: Dictionary<i32, i32>\n    public init(entries: Dictionary<i32, i32>) => self.entries = entries@move\nlet value = Box.init([1: 2])", 1, 96)]
    [InlineData("Generic", "func forward<T>(value: T) -> T => value@move\nlet value = forward([1: [2: 3]])", 2, 416)]
    [Trait("Purpose", "Allocation")]
    public void NestedHandlesReleaseEveryOwnedBuffer(string name, string source, int allocations, long bytes)
        => NativeAllocationAudit.WriteFixture("NestedCollection" + name, source, allocations, allocations, bytes);

    [Fact]
    public void NestedEntriesKeepReverseDestructionOrder()
    {
        const string source = """
            struct Item
                public let name: string
                public init(name: string) => self.name = name@move
                drop => Console.writeLine(self.name)
            let entries = [1: [1: Item.init("first"), 2: Item.init("second")], 2: [1: Item.init("third")]]
            """;
        ScalarEmissionTest.EmitFixture("NestedCollectionOrder", source, "third\nsecond\nfirst\n");
    }

    [Theory]
    [InlineData("Replace", "var values = [1: [2: 3]]\nvalues[1] = [4: 5]", 3, 512)]
    [InlineData("ArrayReplace", "var values = [[1: 2]]\nvalues[0] = [3: 4]", 3, 416)]
    [InlineData("Remove", "var values = [1: [2: 3]]\nlet removed = values.remove(1)", 2, 416)]
    [InlineData("ReplaceResult", "var values = [1: [2: 3]]\nlet previous = values.insertOrReplace(1, [4: 5])", 3, 512)]
    [InlineData("Duplicate", "var values = [1: [2: 3]]\nlet duplicate = values.tryInsert(1, [4: 5])", 3, 512)]
    [InlineData("Clear", "var values = [1: [2: 3]]\nvalues.clear()\nvalues.clear()", 2, 416)]
    [InlineData("OwningIteration", "let values = [1: [2: 3], 2: [4: 5]]\nfor (key, value) in values@move => require value.length == 1 else => $abort(\"value\")", 3, 512)]
    [InlineData("IteratorRemainder", "let values = [1: [2: 3], 2: [4: 5]]\nfor (key, value) in values@move => exit", 3, 512)]
    [InlineData("ArrayPop", "var values = [[1: 2]]\nlet entry = values.pop()", 2, 320)]
    [InlineData("TupleMove", "let values = ([1: 2], [3: 4])\nlet first = values.0@move", 2, 192)]
    [InlineData("TuplePartialReplace", "var values = ([1: 2], [3: 4])\nlet first = values.0@move\nvalues = ([5: 6], [7: 8])", 4, 384)]
    [InlineData("BorrowedReplace", "var values = [[1: 2]]\nfunc change(value: uniq/Dictionary<i32, i32>) => Kimi.Intrinsics.replace(value, with: [3: 4])\nchange(values[0]@uniq)", 3, 416)]
    [InlineData("BorrowedAssignment", "var values = [[1: 2]]\nfunc change(value: uniq/Dictionary<i32, i32>) => value@follow = [3: 4]\nchange(values[0]@uniq)", 3, 416)]
    [InlineData("ArrayOwnIteration", "let values = [[1: 2], [3: 4]]\nfor value in values@move => require value.length == 1 else => $abort(\"value\")", 3, 416)]
    [Trait("Purpose", "Allocation")]
    public void TransfersAndMutationPreserveNestedOwnership(string name, string source, int allocations, long bytes)
        => NativeAllocationAudit.WriteFixture("NestedCollection" + name, source, allocations, allocations, bytes);

    [Fact]
    public void DifferentPayloadTypesWithEqualHandleLayoutsKeepTheirOwnDestructors()
    {
        const string source = """
            struct First
                public init() => ()
                drop => Console.writeLine("first")
            struct Second
                public init() => ()
                drop => Console.writeLine("second")
            let first = ([1: First.init()], 1)
            let second = ([1: Second.init()], 2)
            """;
        ScalarEmissionTest.EmitFixture("NestedCollectionDropIdentity", source, "second\nfirst\n");
    }

    [Fact]
    public void ReturnDuringNestedConstructionDestroysOnlyCompletedEntries()
    {
        const string source = """
            struct Item
                public init() => ()
                drop => Console.writeLine("item")
            func run()
                let values: Dictionary<i32, Dictionary<i32, Item>> = [1: [1: Item.init()], 2: (label nested: do
                    return
                    exit to nested [:]
                )]
            run()
            Console.writeLine("after")
            """;
        ScalarEmissionTest.EmitFixture("NestedCollectionPartial", source, "item\nafter\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCompilationReusesNestedStoragePlans()
    {
        var c = MinimalEmissionTest.Analyze("var values = [1: [2: 3]]\nlet removed = values.remove(1)\nlet arrays = [[1: 2]]");
        void Compile()
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        for (var i = 0; i < 32; i++)
        {
            Compile();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Compile));
    }

    [Fact]
    public void AStoredHandleCannotBeMovedTwice()
    {
        var c = MinimalEmissionTest.Analyze("let inner = [1: 2]\nlet values = [1: inner@move]\nlet again = inner@move");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("var values = [[1: 2]]\nlet view = values[0]@ref\nvalues.clear()\nlet count = view.length")]
    [InlineData("var values = [1: [2: 3]]\nlet view = values[1]@ref\nvalues.clear()\nlet count = view.length")]
    public void RetainedInnerHandleBorrowsProtectTheOuterStorage(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void SeparateExclusiveArgumentsStillCannotOverlapTheOuterStorage()
    {
        const string source = "func use(first: uniq/Dictionary<i32, i32>, second: uniq/Dictionary<i32, i32>) => ()\nvar values = [[1: 2], [3: 4]]\nuse(values[0]@uniq, values[1]@uniq)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }
}
