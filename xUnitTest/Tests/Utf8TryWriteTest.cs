// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8TryWriteTest
{
    private const string Setup = "var bytes = [64 of 0@u8]\nvar buffer = Text.fixed(bytes@uniq)\nvar writer = Text.writer(buffer@uniq)\n";
    private const string Print = "match buffer.text()\n    .Ok(let text) => Console.writeLine(text)\n    .Err(_) => $abort(\"utf8\")\n";

    [Theory]
    [InlineData("Plain", "\"abc\"", "abc\n")]
    [InlineData("Empty", "\"\"", "\n")]
    [InlineData("Raw", "\"\"\"\\(42)\"\"\"", "\\(42)\n")]
    [InlineData("Embedded", "\"君=\\(42)!\\(true)\"", "君=42!true\n")]
    public void RootWritesIntoExistingStorageWithoutAllocation(string name, string literal, string expected)
    {
        var source = Setup + "_ = $tryWrite(writer@uniq, " + literal + ")\n" + Print;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join("\n", c.Ownership.Issues));
        NativeAllocationAudit.WriteFixture("Utf8TryWrite" + name, source, 0, 0, 0, expected);
    }

    [Theory]
    [InlineData("writer", DiagnosticCode.ExclusiveBorrowRequired_Kd)]
    [InlineData("42", DiagnosticCode.TypeMismatch_Kd)]
    public void FirstOperandRequiresAnExclusiveWriter(string first, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Setup + "_ = $tryWrite(" + first + ", \"x\")");
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Theory]
    [InlineData("buffer.length")]
    [InlineData("bytes[0]")]
    [InlineData("read: do\n    _ = writer.status()\n    exit to read: 1@i32")]
    public void RootBorrowIsActiveBeforeTheFirstEmbeddedExpression(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Setup + "_ = $tryWrite(writer@uniq, \"\\(" + expression + ")\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Fact]
    public void FailureSkipsLaterEvaluationAndRetainsTheWrittenPrefix()
    {
        const string Source = """
            func next() -> i32
                $abort("must not evaluate")
            var bytes = [3 of 0@u8]
            var buffer = Text.fixed(bytes@uniq)
            var writer = Text.writer(buffer@uniq)
            match $tryWrite(writer@uniq, "a\(1234)\(next())")
                .Ok(_) => $abort("expected full")
                .Err(_) => ()
            _ = $tryWrite(writer@uniq, "\(next())")
            """;
        NativeAllocationAudit.WriteFixture("Utf8TryWriteFailure", Source + "\n" + Print, 0, 0, 0, "a\n");
    }

    [Fact]
    public void BorrowedWriterAndUserFormatterReuseTheSameRoot()
    {
        const string Prefix = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    return $tryWrite(writer, "v=\(42)")
            func append<T>(writer: uniq/Utf8Writer, value: ref/T) -> Result<(), BufferFull>
                T is Utf8Format
                return $tryWrite(writer, "[\(value)]")
            """;
        NativeAllocationAudit.WriteFixture("Utf8TryWriteNested", Prefix + "\n" + Setup + "_ = append(writer@uniq, Value.init())\n" + Print, 0, 0, 0, "[v=42]\n");
    }

    [Fact]
    public void EmbeddedReturnTargetsTheOriginalFunction()
    {
        const string Source = """
            func append(writer: uniq/Utf8Writer) -> i32
                _ = $tryWrite(writer, "prefix\(do => return 7)suffix")
                return 9
            var bytes = [64 of 0@u8]
            var buffer = Text.fixed(bytes@uniq)
            var writer = Text.writer(buffer@uniq)
            require append(writer@uniq) == 7 else => $abort("return target")
            """;
        NativeAllocationAudit.WriteFixture("Utf8TryWriteReturn", Source + "\n" + Print, 0, 0, 0, "prefix\n");
    }

    [Fact]
    public void FirstOperandCanTransferControlWithoutAcquiringAWriter()
    {
        const string Source = """
            func run() -> i32
                _ = $tryWrite(do => return 7, "\(42)")
            require run() == 7 else => $abort("first operand")
            """;
        ScalarEmissionTest.EmitFixture("Utf8TryWriteFirstNever", Source, string.Empty);
    }

    [Fact]
    public void DirectWriteWarningIsIndependentOfDiscardWarning()
    {
        var c = MinimalEmissionTest.Analyze(Setup + "(writer@uniq).write(\"\\(42)\")");
        var warnings = c.AnalyzeControlFlow(c.Binding.TypeSystem).Warnings;
        Assert.Contains(warnings, x => x.Message.Contains("$tryWrite", StringComparison.Ordinal));
        Assert.Equal(2, warnings.Count);
    }

    [Theory]
    [InlineData("Exit", "work: do", "exit to work: 7")]
    [InlineData("Yield", "work: if true", "yield to work: 7")]
    public void EmbeddedTransferKeepsItsEnclosingTarget(string name, string boundary, string transfer)
    {
        var source = Setup + "let value: i32 = " + boundary + "\n    _ = $tryWrite(writer@uniq, \"a\\(do => " + transfer + ")b\")\n    " + transfer +
            (name == "Yield" ? "\nelse => 9" : string.Empty) + "\nrequire value == 7 else => $abort(\"target\")\n" + Print;
        NativeAllocationAudit.WriteFixture("Utf8TryWrite" + name, source, 0, 0, 0, "a\n");
    }

    [Theory]
    [InlineData("let literal = \"x\"\n_ = $tryWrite(writer@uniq, literal)")]
    [InlineData("_ = $tryWrite(writer@uniq, (\"x\"))")]
    [InlineData("_ = $tryWrite(writer@uniq, \"x\",)")]
    public void SecondOperandMustBeLiteralSyntax(string use)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, Setup + use);
        Assert.Contains(c.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.UnexpectedToken_Kd));
    }

    [Fact]
    public void DeadEmbeddedValuesStillRequireFormattingConformance()
    {
        var c = MinimalEmissionTest.Analyze("struct Missing\n    public init() => ()\n" + Setup + "_ = $tryWrite(writer@uniq, \"large\\(Missing.init())\")");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void BorrowedWriterExpressionEvaluatesOnce()
    {
        const string Prefix = """
            func acquire(writer: uniq/Utf8Writer) -> uniq/Utf8Writer{r} during writer
                origin r.target == writer.target
                Console.writeLine("acquire")
                return writer
            """;
        var source = Prefix + "\n" + Setup + "_ = $tryWrite(acquire(writer@uniq), \"\\(42)\\(true)\")\n" + Print;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        NativeAllocationAudit.WriteFixture("Utf8TryWriteAcquire", source, 0, 0, 0, "acquire\n42true\n");
    }

    [Fact]
    public void RootPreservesOuterPendingLiteralAndCapacityHint()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    return $tryWrite(writer, "x\(1@i8)")
            let value = Value.init()
            let text = "prefix\(value)tail"
            Console.writeLine(text)
            """;
        NativeAllocationAudit.WriteFixture("Utf8TryWriteHint", Source, 2, 2, 33, "prefixx1tail\n");
    }
}
