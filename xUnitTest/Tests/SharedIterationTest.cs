// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedIterationTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TupleComponentsCopyValuesAndBorrowNonCopyStorage(bool dynamic)
    {
        const string Source = """
            struct Item
                public let number: i32
                public init(number: i32) => self.number = number
                deinit => ()
            let values: [2 of (i32, Item)] = [(2, Item.init(3)), (5, Item.init(7))]
            var total = 0
            for (number, item) in values
                let copied: i32 = number
                total += copied + item.number
            require total == 17 else => $abort("components")
            for (_, item) in values => require item.number > 0 else => $abort("owner")
            """;
        var source = dynamic ? Source.Replace("[2 of (i32, Item)]", "Array<(i32, Item)>", StringComparison.Ordinal) : Source;
        // The existing Array growth policy allocates at least four 8-byte elements.
        NativeAllocationAudit.WriteFixture("SharedIterationTuple" + dynamic, source, dynamic ? 1 : 0, dynamic ? 1 : 0, dynamic ? 32 : 0);
    }

    [Fact]
    public void CopyComponentsKeepTheirOwnReferenceOrigins()
    {
        const string Source = """
            let value: i32 = 42
            let input = value@ref
            let pairs: [1 of (ref/i32 during input, i32)] = [(input, 1)]
            for (reference, number) in pairs
                let copied: ref/i32 = reference
                require copied == 42 and number == 1 else => $abort("stored reference")
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        NativeAllocationAudit.WriteFixture("SharedIterationReference", Source, 0, 0, 0);
    }

    [Fact]
    public void CopyAggregateComponentUsesItsOwnSnapshot()
    {
        const string Source = """
            struct Pair
                Self is Copy
                public var number: i32
                public init(number: i32) => self.number = number
            let pairs: [1 of (Pair, i32)] = [(Pair.init(42), 1)]
            let view = pairs[..]
            for (value, number) in view
                var copied = value
                copied.number = 7
                require value.number == 42 and copied.number == 7 and number == 1 else => $abort("snapshot")
            """;
        NativeAllocationAudit.WriteFixture("SharedIterationCopyAggregate", Source, 0, 0, 0);
    }

    [Fact]
    public void SharedIterationKeepsItsOwnerProtected()
    {
        const string Source = """
            var values: [1 of (i32, i32)] = [(1, 2)]
            for (first, second) in values
                values = [(3, 4)]
                require first + second == 3 else => $abort("values")
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
