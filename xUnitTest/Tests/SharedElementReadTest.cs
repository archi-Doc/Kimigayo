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
            let saved = values[0]@ref
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
        // A stored uniq/i32 is Non-Copy: the shared Reborrow of its referent is explicit (SPEC 13.5.5.2).
        var source = exclusive ? Source.Replace("ref/i32", "uniq/i32", StringComparison.Ordinal).Replace("number@ref", "number@uniq", StringComparison.Ordinal).Replace("values[..][0]", "values[..][0]@follow@ref", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedElementReference" + exclusive, source, 1, 1, 32);
    }

    [Fact]
    public void ImplicitBorrowsAtExpectedReferences()
    {
        // SPEC 10.2: a readable owned Place at a fixed expected ref/U is shared-borrowed at an annotated
        // initializer, an argument and a by-value result; a Copy element is read bare.
        const string Source = """
            func first(values: ref/Array<string>) -> ref/string during values => values[0]
            var values: Array<string> = ["hello"]
            let saved: ref/string = values[0]
            Console.writeLine(saved)
            Console.writeLine(values[0])
            Console.writeLine(first(values@ref))
            var number: i32 = 5
            let view: ref/i32 = number
            let numbers: [1 of i32] = [7]
            let copied = numbers[..][0]
            require view == 5 and copied == 7 else => $abort("scalar")
            """;
        NativeAllocationAudit.WriteFixture("SharedElementExpectedReference", Source, 1, 1, 96, "hello\nhello\nhello\n");
    }

    [Theory]
    [InlineData("var values: Array<string> = [\"hello\"]\nlet saved = values[0]")]
    [InlineData("var values: Array<string> = [\"hello\"]\nlet saved = values[..][0]")]
    [InlineData("var number = 42\nlet values: Array<uniq/i32> = [number@uniq]\nlet saved = values[..][0]")]
    public void BareNonCopyElementReadsAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.TransferRequired);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void BorrowedStringPreventsReplacementBeforeLastUse()
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<string> = [\"hello\"]\nlet saved = values[0]@ref\nvalues[0] = \"new\"\nConsole.writeLine(saved)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
