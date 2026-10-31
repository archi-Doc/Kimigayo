// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class Utf8InterpolationTest
{
    [Fact]
    public void InterpolationBindingUsesTheOrdinaryCallContracts()
    {
        var c = MinimalEmissionTest.Analyze("let n = 42@i64\nlet text = \"My number is \\(n)\"\nConsole.writeLine(text)");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node.ToString())));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BoundedOwningInterpolationUsesOneAllocation()
        => NativeAllocationAudit.WriteFixture("Utf8InterpolationBounded", "let n = 42@i64\nlet text = \"My number is \\(n)\"\nConsole.writeLine(text)", 1, 1, 33, "My number is 42\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InterpolationUsesTheUserFormatter()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write("word")
            let value = Value.init()
            let text = "\(value)"
            Console.writeLine(text)
            """;
        NativeAllocationAudit.WriteFixture("Utf8InterpolationUser", Source, 1, 1, 4, "word\n");
    }

    [Fact]
    public void EachValueFormatsBeforeTheNextExpressionAndTemporariesKeepTheirLifetime()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => Console.writeLine("create")
                drop => Console.writeLine("destroy")
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    Console.writeLine("format")
                    return writer.write("v")
            func next() -> i32
                Console.writeLine("next")
                return 7
            let text = "\(Value.init())\(next())"
            Console.writeLine(text)
            """;
        ScalarEmissionTest.EmitFixture("Utf8InterpolationLifetime", Source, "create\nformat\nnext\ndestroy\nv7\n");
    }

    [Fact]
    public void GenericInterpolationKeepsTheSelectedContract()
    {
        const string Source = """
            func describe<T>(value: ref/T) -> string
                T is Utf8Format
                return "value=\(value)"
            Console.writeLine(describe(123))
            Console.writeLine(describe("君"))
            """;
        ScalarEmissionTest.EmitFixture("Utf8InterpolationGeneric", Source, "value=123\nvalue=君\n");
    }

    [Fact]
    public void ValueBorrowEndsBeforeTheNextExpression()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                var n: i32
                public init() => self.n = 1
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(self.n)
                public func change(self: uniq/Self) -> i32
                    self.n = 2
                    return self.n
            var value = Value.init()
            let text = "\(value)\(value@uniq.change())"
            Console.writeLine(text)
            """;
        ScalarEmissionTest.EmitFixture("Utf8InterpolationBorrowEnd", Source, "12\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("String", "\"abc\"", "prefixabc:42\n", 21)]
    [InlineData("Empty", "\"\"", "prefix:42\n", 18)]
    [InlineData("Utf8", "Text.utf8(source)", "prefix君:42\n", 21)]
    public void OneUnboundedValueUsesOneAllocationIncludingThePendingPrefix(string name, string value, string expected, int capacity)
    {
        var source = "let source = \"君\"\nlet value = " + value + "\nlet text = \"prefix\\(value):\\(42)\"\nConsole.writeLine(text)";
        NativeAllocationAudit.WriteFixture("Utf8InterpolationHint" + name, source, 1, 1, capacity, expected);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void EmptyUnboundedOutputRemainsUnallocated()
        => NativeAllocationAudit.WriteFixture("Utf8InterpolationEmpty", "let value = \"\"\nlet text = \"\\(value)\"\nConsole.writeLine(text)", 0, 0, 0, "\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AFormatterThatNeverReservesDoesNotLosePendingLiterals()
    {
        const string Source = """
            struct Empty
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => .Ok(())
            let text = "a\(Empty.init())b"
            Console.writeLine(text)
            """;
        NativeAllocationAudit.WriteFixture("Utf8InterpolationPendingEmpty", Source, 1, 1, 2, "ab\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void NestedReservationsRetainTheOuterHint()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    try writer.write("a")
                    return writer.write("0123456789")
            let text = "p\(Value.init())tail"
            Console.writeLine(text)
            """;
        NativeAllocationAudit.WriteFixture("Utf8InterpolationNestedHint", Source, 2, 2, 22, "pa0123456789tail\n");
    }

    // SPEC 22.10.5 (DECISIONS R5-M133): MIR code generation defers a segment's literal text exactly when a value follows, so these forms keep
    // the old generator's allocations and bytes: the O2 corpus kernel's line (literals before bounded values and a user
    // formatter reserving twice), a literal before a bounded value after a value that writes nothing, a bounded value before an unbounded
    // string, and an owning interpolation inside another (fail-before under MIR code generation: C9c's interim record).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(
        "Kernel",
        "let i = 21@i64\nlet reading = Reading.init(i)\nlet text = \"item \\(i) of \\(reading) is \\(i % 7 == 0)\"\nConsole.writeLine(text)",
        2,
        60,
        "item 21 of Reading(21) is true\n")]
    [InlineData("EmptyThenBounded", "let text = \"\\(Empty.init())L\\(7@i8)\"\nConsole.writeLine(text)", 1, 2, "L7\n")]
    [InlineData(
        "BoundedThenString",
        "let n = 5@i64\nlet s = \"abcdefghijklmnopqrstuvwxyz0123\"\nlet text = \"x\\(n)\\(s)\"\nConsole.writeLine(text)",
        2,
        34,
        "x5abcdefghijklmnopqrstuvwxyz0123\n")]
    [InlineData("Nested", "let s = \"abc\"\nlet text = \"<\\(\"[\\(s)]\")>\"\nConsole.writeLine(text)", 2, 12, "<[abc]>\n")]
    public void DeferredLiteralTextKeepsTheOldGeneratorsAllocations(string name, string statements, int allocations, int bytes, string expected)
    {
        const string Formatters = """
            struct Reading
                Self is Utf8Format
                public let number: i64
                public init(number: i64) => self.number = number
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    return $tryWrite(writer, "Reading(\(self.number))")
            struct Empty
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => .Ok(())
            """;
        NativeAllocationAudit.WriteFixture("Utf8InterpolationLiteral" + name, Formatters + "\n" + statements, allocations, allocations, bytes, expected);
    }

    [Fact]
    public void EmbeddedReturnKeepsTheEnclosingFunctionAsItsTarget()
    {
        const string Source = """
            func make() -> string
                _ = "prefix\(do => return "early")suffix"
            Console.writeLine(make())
            """;
        ScalarEmissionTest.EmitFixture("Utf8InterpolationReturn", Source, "early\n");
    }
}
