// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedElementReadTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyAggregateReadHasIndependentStorage(bool slice)
    {
        const string Source = """
            struct Pair
                Self is Copy
                public let number: i32
                public init(number: i32) => self.number = number
            var values: Array<Pair> = [Pair.init(42)]
            let saved = values[0]
            values[0] = Pair.init(7)
            require saved.number == 42 else => $abort("snapshot")
            """;
        var source = slice ? Source.Replace("let saved = values[0]", "let saved = values[..][0]", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedElementCopy" + slice, source, 1, 1, 16);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonCopyStringReadBorrowsItsSlot(bool slice)
    {
        const string Source = """
            var values: Array<string> = ["hello"]
            let saved = values[0]
            Console.writeLine(saved)
            values[0] = "after"
            Console.writeLine(values[0])
            """;
        var source = slice ? Source.Replace("values[0]", "values[..][0]", StringComparison.Ordinal).Replace("values[..][0] =", "values[0] =", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedElementString" + slice, source, 1, 1, 96, "hello\nafter\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredReferenceCopyKeepsItsDependency(bool exclusive)
    {
        const string Source = """
            var number = 42
            let values: Array<ref/i32> = [number@ref]
            let saved = values[..][0]
            require saved == 42 else => $abort("reference")
            number = 7
            """;
        var source = exclusive ? Source.Replace("ref/i32", "uniq/i32", StringComparison.Ordinal).Replace("number@ref", "number@uniq", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedElementReference" + exclusive, source, 1, 1, 32);
    }

    [Fact]
    public void BorrowedStringPreventsReplacementBeforeLastUse()
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<string> = [\"hello\"]\nlet saved = values[0]\nvalues[0] = \"new\"\nConsole.writeLine(saved)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
