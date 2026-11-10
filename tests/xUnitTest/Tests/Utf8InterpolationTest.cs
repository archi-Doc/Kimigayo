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
    // formatter reserving twice), a deferred literal written at the first reservation, then grown by an unbounded string (a literal before
    // a bounded value after a value that writes nothing is OneUnboundedValueUsesOneAllocationIncludingThePendingPrefix's Empty row), an
    // owning interpolation inside another (fail-before under MIR code generation: C9c's interim record), and a bool's body whose leaf write
    // takes the deferred literal and grows once by the hint of the literal after it (Utf8Writer.reserve, R8-M18).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(
        "Kernel",
        "let i = 21@i64\nlet reading = Reading.init(i)\nlet text = \"item \\(i) of \\(reading) is \\(i % 7 == 0)\"\nConsole.writeLine(text)",
        2,
        60,
        "item 21 of Reading(21) is true\n")]
    [InlineData(
        "BoundedThenString",
        "let n = 5@i64\nlet s = \"abcdefghijklmnopqrstuvwxyz0123\"\nlet text = \"x\\(n)\\(s)\"\nConsole.writeLine(text)",
        2,
        34,
        "x5abcdefghijklmnopqrstuvwxyz0123\n")]
    [InlineData("Nested", "let s = \"abc\"\nlet text = \"<\\(\"[\\(s)]\")>\"\nConsole.writeLine(text)", 2, 12, "<[abc]>\n")]
    [InlineData("BoundedGrowsByItsHint", "let text = \"\\(Empty.init())x\\(true)tail\"\nConsole.writeLine(text)", 1, 9, "xtruetail\n")]
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

    // SPEC 22.10.1, 22.10.5: an interpolated literal passed to `write`, or embedded in a $tryWrite, first builds its owning string, a
    // bounded one in at most one allocation; writing that string into a FixedBuffer, or a HeapBuffer with enough capacity, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Fixed", "var bytes = [8 of 0@u8]\nvar buffer = Text.fixed(bytes@uniq)", "(writer@uniq).write(\"n=\\(42)\")", "n=42", 1, 13)]
    [InlineData("Heap", "var buffer = Text.heap(4)", "(writer@uniq).write(\"n=\\(42)\")", "n=42", 2, 17)]
    [InlineData("TryWrite", "var bytes = [8 of 0@u8]\nvar buffer = Text.fixed(bytes@uniq)", "$tryWrite(writer@uniq, \"<\\(\"[\\(7)]\")>\")", "<[7]>", 1, 13)]
    public void AnInterpolatedWriteValueAllocatesOnlyItsString(string name, string setup, string write, string expected, int allocations, int bytes)
    {
        var source = setup + "\nvar writer = Text.writer(buffer@uniq)\nmatch " + write + "\n    .Ok(_) => ()\n    .Err(_) => $abort(\"full\")\n" +
            "match buffer.text()@move\n    .Ok(let text) => Console.writeLine(text)\n    .Err(_) => $abort(\"utf8\")";
        NativeAllocationAudit.WriteFixture("Utf8InterpolationWriteValue" + name, source, allocations, allocations, bytes, expected + "\n");
    }

    // The O2 corpus's Interpolation kernel and its checksum (benchmark-o2-corpus.ps1). Per round, its owning interpolation with a user
    // formatter allocates at most twice (DeferredLiteralTextKeepsTheOldGeneratorsAllocations' Kernel row), and its $tryWrite into the
    // cleared fixed buffer and its last line nothing (SPEC 22.10.5): a bound the adapter may lower but not raise.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void TheInterpolationKernelKeepsItsChecksumWithinItsAllocations()
    {
        const long Bound = 2 * 1048576;
        var source = File.ReadAllText(DiagnosticCorpus.RepositoryPath("src", "Benchmark", "Kimi", "Corpus", "Interpolation.kimi"));
        NativeAllocationAudit.WriteFixture("Utf8InterpolationKernel", source, new(0, Bound), new(0, Bound), NativeAllocationAudit.Bound.Any, "checksum 1676251827\n");
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
