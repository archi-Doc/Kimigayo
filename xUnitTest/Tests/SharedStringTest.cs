// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedStringTest
{
    [Fact]
    public void LocalsAndResultsRetainTheOriginalOwner()
    {
        const string Source = """
            func identity(value: ref/string) -> ref/string during value => value
            let text = "hello"
            let saved = text@ref
            let copied = identity(saved)
            require copied == "hello" and "hello" == saved and saved < "world" else => $abort("borrow")
            Console.writeLine(copied)
            Console.writeLine(text)
            """;
        NativeAllocationAudit.WriteFixture("SharedStringLocals", Source, 0, 0, 0, "hello\nhello\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedIterationBorrowsStringElements(bool dynamic)
    {
        const string Source = """
            let values: [2 of string] = ["first", "last"]
            for value in values => Console.writeLine(value)
            Console.writeLine(values[0])
            """;
        var source = dynamic ? Source.Replace("[2 of string]", "Array<string>", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedStringIteration" + dynamic, source, dynamic ? 1 : 0, dynamic ? 1 : 0, dynamic ? 96 : 0, "first\nlast\nfirst\n");
    }

    [Theory]
    [InlineData("text = \"replacement\"")]
    [InlineData("let moved = text@move")]
    public void RetainedBorrowRejectsOwnerInvalidation(string invalidation)
    {
        var c = MinimalEmissionTest.Analyze("var text = \"hello\"\nlet saved = text@ref\n" + invalidation + "\nConsole.writeLine(saved)");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void SharedSliceIterationRetainsTheArray()
    {
        const string Source = "let values: Array<string> = [\"first\", \"last\"]\nfor value in values[0..1] => Console.writeLine(value)\nConsole.writeLine(values[1])";
        NativeAllocationAudit.WriteFixture("SharedStringSlice", Source, 1, 1, 96, "first\nlast\n");
    }

    [Fact]
    public void ExclusiveParameterCanBeReadWithoutTransferringItsOwner()
    {
        const string Source = "func inspect(text: uniq/string) => Console.writeLine(text)\nvar text = \"hello\"\ninspect(text@uniq)\nConsole.writeLine(text)";
        NativeAllocationAudit.WriteFixture("SharedStringExclusive", Source, 0, 0, 0, "hello\nhello\n");
    }
}
