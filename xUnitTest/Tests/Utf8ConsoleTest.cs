// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class Utf8ConsoleTest
{
    [Fact]
    public void BoundedConsoleInterpolationUsesExactlyThirtyThreeStackBytes()
    {
        const string Source = "let n = 42@i64\nConsole.writeLine(\"My number is \\(n)\")";
        NativeAllocationAudit.WriteFixture("Utf8ConsoleStack", Source, 0, 0, 0, "My number is 42\n");
        var c = MinimalEmissionTest.Analyze(Source);
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), error);
        Assert.Contains("%formatBytes0 = alloca [33 x i8], align 1", output.ToString());
    }

    [Theory]
    [InlineData(1023, 0)]
    [InlineData(1024, 0)]
    [InlineData(1025, 1)]
    public void StackLimitSelectsOnePathBeforeEvaluation(int bound, int allocations)
    {
        var text = new string('x', bound - 4);
        var source = "func next() -> i8\n    Console.writeLine(\"once\")\n    return 7\nConsole.writeLine(\"" + text + "\\(next())\")";
        NativeAllocationAudit.WriteFixture("Utf8ConsoleLimit" + bound, source, allocations, allocations, allocations * bound, "once\n" + text + "7\n");
    }

    [Fact]
    public void ReturningDuringStackFormattingDoesNotFreeTheStackOrPrintThePrefix()
    {
        const string Source = """
            func run() -> i32
                Console.writeLine("prefix\(do => return 7)")
            require run() == 7 else => $abort("return target")
            Console.writeLine("done")
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleReturn", Source, 0, 0, 0, "done\n");
    }

    [Fact]
    public void StackFormattingPreservesUserTemporaryDestruction()
    {
        const string Source = """
            struct Value
                public init() => Console.writeLine("create")
                public func get(self: ref/Self) -> i32 => 42
                deinit => Console.writeLine("destroy")
            Console.writeLine("n=\(Value.init().get())")
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleLifetime", Source, 0, 0, 0, "create\nn=42\ndestroy\n");
    }

    [Fact]
    public void GenericConcreteBoundedValuesUseTheStack()
    {
        const string Source = """
            func print<T>(value: ref/T)
                T is Utf8Format
                Console.writeLine("value=\(value)")
            print(42)
            print("abc")
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleGeneric", Source, 1, 1, 9, "value=42\nvalue=abc\n");
    }

    [Fact]
    public void RepeatedAndConditionalStackFormattingDoesNotAllocate()
    {
        const string Source = """
            var i = 0
            while i < 3
                if i != 1 => Console.writeLine("i=\(i)")
                i += 1
            """;
        NativeAllocationAudit.WriteFixture("Utf8ConsoleLoop", Source, 0, 0, 0, "i=0\ni=2\n");
    }

    [Fact]
    public void InterpolatedTemporaryCannotLendAViewPastTheExpression()
    {
        var c = MinimalEmissionTest.Analyze("let text = Text.utf8(\"\\(42)\")\nConsole.writeLine(text)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
    }
}
