// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class Utf8UserFormatTest
{
    [Theory]
    [InlineData("return .Ok((window@move).limit(0))")]
    [InlineData("return .Ok((window@move).limit(minimum - 1))")]
    [InlineData("let limited = (window@move).limit(minimum - 1)\n    return .Ok(limited@move)")]
    public void LimitedWindowKeepsItsSource(string expression)
    {
        var source = "func limit(buffer: uniq/Text.HeapBuffer, minimum: isize) -> Result<WriteWindow, BufferFull>\n    let window = try buffer.reserve(minimum)\n    " + expression;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)));
    }

    [Theory]
    [InlineData("Concrete", "struct Value\n    Self is Utf8Format\n    public init() => ()\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(\"word\")\n", "Value.init()")]
    [InlineData("Generic", "struct Value<T>\n    Self is Utf8Format\n    let value: T\n    public init(value: T) => self.value = value@move\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(\"word\")\n", "Value<i32>.init(42)")]
    public void UserFormatterCallsBuiltinWrites(string name, string declaration, string value)
        => NativeAllocationAudit.WriteFixture("Utf8UserFormat" + name, declaration + Program("try (writer@uniq).write(" + value + ")"), 0, 0, 0, "word\n");

    [Fact]
    public void GenericWriterWriteSelectsConcreteUserFormatter()
    {
        const string Prefix = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write("word")
            group Helpers
                public func append<T>(value: ref/T, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    T is Utf8Format
                    return writer.write(value)
            """;
        NativeAllocationAudit.WriteFixture("Utf8UserFormatForwarded", Prefix + "\n" + Program("try Helpers.append(Value.init(), writer@uniq)"), 0, 0, 0, "word\n");
    }

    [Theory]
    [InlineData("Returned", "return .Err(BufferFull.init())")]
    [InlineData("Swallowed", "_ = writer.write(\"too large for destination\")\n        return .Ok(())")]
    public void AUserFormatterCannotClearFailure(string name, string body)
    {
        var source = "struct Value\n    Self is Utf8Format\n    public init() => ()\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>\n        " + body + "\n" + """
            var bytes = [4 of 0@u8]
            var buffer = Text.fixed(bytes@uniq)
            var writer = Text.writer(buffer@uniq)
            match (writer@uniq).write(Value.init())
                .Ok(_) => $abort("user failure")
                .Err(_) => ()
            match writer.status()
                .Ok(_) => $abort("sticky status")
                .Err(_) => ()
            match (writer@uniq).write("")
                .Ok(_) => $abort("sticky empty")
                .Err(_) => ()
            """;
        NativeAllocationAudit.WriteFixture("Utf8UserFormat" + name, source, 0, 0, 0);
    }

    [Fact]
    public void AGenericWriterReservesOnceAndStopsAfterFailure()
    {
        const string Source = """
            struct Destination<T>
                Self is BufferWriter
                let value: T
                public var count: i32 = 0
                public init(value: T) => self.value = value@move
                public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
                    self.count += 1
                    return .Err(BufferFull.init())
            func make<W>(value: uniq/W) -> Utf8Writer
                W is BufferWriter
                return Text.writer(value)
            var destination = Destination<i32>.init(42)
            var writer = make(destination@uniq)
            match (writer@uniq).write("")
                .Ok(_) => ()
                .Err(_) => $abort("empty")
            match (writer@uniq).write(123)
                .Ok(_) => $abort("expected full")
                .Err(_) => ()
            match (writer@uniq).write(456)
                .Ok(_) => $abort("sticky")
                .Err(_) => ()
            require destination.count == 1 else => $abort("reservation count")
            """;
        NativeAllocationAudit.WriteFixture("Utf8UserFormatGenericWriter", Source, 0, 0, 0);
    }

    [Theory]
    [InlineData("Valid", "return self.buffer.reserve(minimum)", true, 3)]
    [InlineData("Short", "let window = try self.buffer.reserve(minimum)\n        return .Ok((window@move).limit(minimum - 1))", false, 3)]
    [InlineData("Written", "var window = try self.buffer.reserve(minimum + 1)\n        try (window@uniq).push(120@u8)\n        return .Ok(window@move)", false, 4)]
    public void ErasedReservationsValidateUserWindows(string name, string reserve, bool valid, int bytes)
    {
        var source = """
            struct Destination
                Self is BufferWriter
                var buffer: Text.HeapBuffer
                public var count: i32 = 0
                public init() => self.buffer = Text.heap(0)
                public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
                    self.count += 1
            """ + "\n        " + reserve + "\n" + """
                public func output(self: ref/Self)
                    match self.buffer.text()
                        .Ok(let view) => Console.writeLine(view)
                        .Err(_) => $abort("utf8")
            var destination = Destination.init()
            var writer = Text.writer(destination@uniq)
            match (writer@uniq).write("abc")
            """ + (valid ? "\n    .Ok(_) => ()\n    .Err(_) => $abort(\"valid window\")" : "\n    .Ok(_) => $abort(\"malformed window\")\n    .Err(_) => ()") + "\nrequire destination.count == 1 else => $abort(\"reserve once\")\ndestination.output()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)));
        NativeAllocationAudit.WriteFixture("Utf8UserFormatWindow" + name, source, 1, 1, bytes, valid ? "abc\n" : "\n");
    }

    private static string Program(string operation)
        => """
            func run() -> Result<(), BufferFull>
                var bytes = [16 of 0@u8]
                var buffer = Text.fixed(bytes@uniq)
                var writer = Text.writer(buffer@uniq)
            """ + "\n    " + operation + "\n" + """
                match (buffer@move).intoText()
                    .Ok(let view) => Console.writeLine(view)
                    .Err(_) => $abort("utf8")
                return .Ok(())
            match run()
                .Ok(_) => ()
                .Err(_) => $abort("full")
            """;
}
