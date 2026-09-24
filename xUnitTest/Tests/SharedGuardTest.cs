// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedGuardTest
{
    [Fact]
    public void SelectedCopyBindingHasItsOwnStorageOrigin()
    {
        const string Source = """
            struct Pair
                Self is Copy
                public let number: i32
                public init(number: i32) => self.number = number
            var original = ("hello", Pair.init(42))
            match original
                (let text, let pair) if text == "hello" and pair.number == 42
                    let saved = pair@ref
                    original = ("new", Pair.init(7))
                    require saved.number == 42 else => $abort("snapshot")
                (_, _) => $abort("missing")
            """;
        NativeAllocationAudit.WriteFixture("SharedGuardBodyStorageOrigin", Source, 0, 0, 0);
    }

    [Fact]
    public void WarmSharedGuardAnalysisAndEmissionAllocateNothing()
    {
        const string Source = "let value = (\"hello\", 42)\nmatch value\n    (let text, let number) if text == \"hello\" and number == 42 => Console.writeLine(text)\n    (_, _) => ()";
        var c = MinimalEmissionTest.Analyze(Source);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Shared guard verification failed.");
            }
        }));
    }

    [Fact]
    public void CompleteCopyCandidatesSupportFieldsAndBorrowedCalls()
    {
        const string Source = """
            struct Pair
                Self is Copy
                public let number: i32
                public init(number: i32) => self.number = number
            func accepts(value: ref/Pair) -> bool => value.number == 42
            let value = (Pair.init(42), "hello")
            match value
                (let pair, let text) if pair.number == 42 and accepts(pair) and text == "hello"
                    Console.writeLine(text)
                (_, _) => $abort("missing")
            """;
        NativeAllocationAudit.WriteFixture("SharedGuardCopyAggregate", Source, 0, 0, 0, "hello\n");
    }

    [Fact]
    public void AStoredReferenceCopyRetainsItsExternalOriginWhenReturnedFromAGuard()
    {
        const string Source = """
            func get(value: ref/(ref/string during source, i32)) -> ref/string during source
                match value
                    (let text, _) if (return text) => ()
                    (_, _) => ()
                $abort("none")
            let text = "hello"
            let value = (text@ref, 42)
            Console.writeLine(get(value@ref))
            """;
        NativeAllocationAudit.WriteFixture("SharedGuardStoredReference", Source, 0, 0, 0, "hello\n");
    }

    [Fact]
    public void CheckingReadsAfterATerminalGuardHaveFreshProtection()
    {
        const string Source = """
            func get(value: ref/(string, i32)) -> i32
                match value
                    (let text, _) if (check: do
                        return 42
                        Console.writeLine(text)
                        Console.writeLine(text)
                        exit to check: true
                    ) => ()
                    (_, _) => ()
                return 0
            let value = ("hello", 1)
            require get(value@ref) == 42 else => $abort("return")
            """;
        NativeAllocationAudit.WriteFixture("SharedGuardCheckingRead", Source, 0, 0, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompositeCandidatesShareReadAndKeepTheirBodyAcquisition(bool shared)
    {
        const string Source = """
            let value = (Text.toString("hello"), 42)
            match value@move
                (let text, let number) if (check: do
                    defer => Console.writeLine("cleanup")
                    exit to check: text == "other" and number == 42
                ) => $abort("false")
                (let text, let number) if text == "hello" and number == 42
                    Console.writeLine(text)
                (_, _) => $abort("missing")
            Console.writeLine("done")
            """;
        var source = shared ? Source.Replace("match value@move", "match value", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("SharedGuardComposite" + shared, source, 1, 1, 5, "cleanup\nhello\ndone\n");
    }

    [Fact]
    public void WholeReferenceAndScalarLiteralCandidatesCanGuard()
    {
        const string Source = """
            func inspect(value: ref/i32) -> i32 => match value
                0 if false => 0
                let number if number == 42 => 42
                _ => 1
            let number = 42
            require inspect(number@ref) == 42 else => $abort("number")
            let text = "hello"
            match text
                let saved if saved == "other" => $abort("other")
                let saved if saved == "hello" => Console.writeLine(saved)
                _ => $abort("missing")
            """;
        NativeAllocationAudit.WriteFixture("SharedGuardWholeReference", Source, 0, 0, 0, "hello\n");
    }

    [Theory]
    [InlineData("_", "text = \"new\"")]
    [InlineData("let saved", "Console.writeLine(saved)\n        text = \"new\"")]
    public void ProtectionLastsThroughGuardCleanupEvenAfterCandidateLastUse(string pattern, string mutation)
    {
        var source = "var text = \"hello\"\nmatch text\n    " + pattern + " if (check: do\n        " + mutation + "\n        exit to check: false\n    ) => ()\n    _ => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void ANewCandidateBorrowCannotEscapeItsGuard()
    {
        var c = MinimalEmissionTest.Analyze("func get(value: ref/(string, i32)) -> ref/string during value\n    match value\n        (let text, _) if (return text) => ()\n        (_, _) => ()\n    $abort(\"none\")");
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.False(c.Binding.Result.IsComplete);
    }
}
