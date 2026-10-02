// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DisjointArrayPairTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Array<i32>")]
    [InlineData("[2 of i32]")]
    public void WarmPairPipelineAllocatesNothing(string type)
    {
        var c = MinimalEmissionTest.Analyze(RepeatedUpdates(type));
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.Validate(out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Pair Binding failed.");
            }

            c.Binding.CheckStartup(OutputKind.Application);
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.Validate(out _))
            {
                throw new InvalidOperationException("Pair pipeline failed.");
            }
        }));
    }

    [Fact]
    public void LibraryDeclarationsRemainWellFormed()
    {
        var c = MinimalEmissionTest.Analyze("()");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Diagnostics.Finalize().Diagnostics.Select(static x => $"{x.Message}: {x.Display}")));
    }

    [Theory]
    [InlineData("Array<i32>", "Array")]
    [InlineData("[4 of i32]", "Fixed")]
    public void PairOrderBoundsAndEqualityFollowResolvedPositions(string type, string name)
    {
        var source = $$"""
            var values: {{type}} = [10, 20, 30, 40]
            var first: isize = -1
            while first <= 4
                var second: isize = -1
                while second <= 4
                    match values.tryGetPairUniq(first: first, second: second)
                        .Some((let a, let b))
                            require first >= 0 and first < 4 and second >= 0 and second < 4 and first != second else => $abort("invalid pair")
                            require a == (first + 1)@i32 * 10 and b == (second + 1)@i32 * 10 else => $abort("order")
                            a@follow += 1
                            b@follow += 2
                            a@follow -= 1
                            b@follow -= 2
                        .None
                            require first < 0 or first >= 4 or second < 0 or second >= 4 or first == second else => $abort("missing pair")
                    second += 1
                first += 1
            match values.tryGetPairUniq(first: ^1, second: 0)
                .Some((let last, let head))
                    last@follow = 42
                    head@follow = 7
                .None => $abort("from end")
            match values.tryGetPairUniq(first: ^1, second: 3)
                .Some(_) => $abort("same logical position")
                .None => ()
            require values[0] == 7 and values[3] == 42 else => $abort("writeback")
            """;
        ScalarEmissionTest.EmitFixture("DisjointArrayPair" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Array<()>", "Array")]
    [InlineData("[2 of ()]", "Fixed")]
    public void ZeroSizedElementsUseLogicalPositions(string type, string name)
    {
        var source = $$"""
            var values: {{type}} = [(), ()]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    a@follow = ()
                    b@follow = ()
                .None => $abort("distinct zero-sized elements")
            match values.tryGetPairUniq(first: 1, second: ^1)
                .Some(_) => $abort("same zero-sized element")
                .None => ()
            """;
        ScalarEmissionTest.EmitFixture("DisjointArrayPairZero" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Array<Item>", "Array")]
    [InlineData("[2 of Item]", "Fixed")]
    public void ReplacingBothNonCopyElementsDestroysOnlyTheirOldValues(string type, string name)
    {
        var source = $$"""
            struct Item
                let name: string
                public init(name: string) => self.name = name@move
                drop => Console.writeLine(self.name)
            var values: {{type}} = [Item.init("old first"), Item.init("old last")]
            match values.tryGetPairUniq(first: ^1, second: 0)
                .Some((let a, let b))
                    a@follow = Item.init("new last")
                    b@follow = Item.init("new first")
                .None => $abort("pair")
            Console.writeLine("after")
            """;
        ScalarEmissionTest.EmitFixture("DisjointArrayPairDrop" + name, source, "old last\nold first\nafter\nnew last\nnew first\n");
    }

    [Theory]
    [InlineData("Array<i32>", "Array")]
    [InlineData("[0 of i32]", "Fixed")]
    public void EmptyStorageHasNoPair(string type, string name)
    {
        var source = $$"""
            var values: {{type}} = []
            match values.tryGetPairUniq(first: 0, second: ^1)
                .Some(_) => $abort("empty")
                .None => ()
            """;
        ScalarEmissionTest.EmitFixture("DisjointArrayPairEmpty" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Array<i32>", "values[0] = 3")]
    [InlineData("Array<i32>", "let copy = values[0]")]
    [InlineData("Array<i32>", "values.append(3)")]
    [InlineData("[2 of i32]", "values[0] = 3")]
    [InlineData("[2 of i32]", "let copy = values[0]")]
    public void PairKeepsTheWholeCollectionBorrowed(string type, string operation)
    {
        var c = MinimalEmissionTest.Analyze($$"""
            var values: {{type}} = [1, 2]
            match values.tryGetPairUniq(first: 0, second: 1)
                .Some((let a, let b))
                    {{operation}}
                    a@follow += 1
                    b@follow += 1
                .None => ()
            """);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Array<i32>")]
    [InlineData("[2 of i32]")]
    public void OrdinaryIndexingStillRequiresStaticNonOverlap(string type)
    {
        var c = MinimalEmissionTest.Analyze($"var values: {type} = [1, 2]\nlet receiver = values@uniq\nlet first: isize = 0\nlet second: isize = 1\nlet a = receiver[first]@uniq\nlet b = receiver[second]@uniq\na@follow = 3\nb@follow = 4");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    internal static string RepeatedUpdates(string type) => $$"""
        var values: {{type}} = [0, 0]
        var count = 0
        while count < 1024
            match values.tryGetPairUniq(first: 1, second: 0)
                .Some((let a, let b))
                    a@follow += 1
                    b@follow += 2
                .None => $abort("pair")
            count += 1
        require values[0] == 2048 and values[1] == 1024 else => $abort("updates")
        """;
}
