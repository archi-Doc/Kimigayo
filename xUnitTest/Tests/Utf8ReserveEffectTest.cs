// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8ReserveEffectTest
{
    private const string State = "group State\n    public var value: i32 = 0\n";
    private const string Writer = "struct Writer\n    Self is BufferWriter\n    public var local: i32 = 1\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n";
    private const string Formatter = "struct Value\n    Self is Utf8Format\n    public init() => ()\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>\n        ";

    [Theory]
    [InlineData("", "_ = minimum")]
    [InlineData("", "self.local += 1")]
    [InlineData("group Helpers\n    public func pure(value: i32) -> i32 => value + 1\n", "_ = Helpers.pure(self.local)")]
    [InlineData("group Constants\n    public let value: i32 = 1\n", "_ = Constants.value")]
    [InlineData("", "_ = Text.toString(42)")]
    [InlineData("", "_ = \"value: \\(42)\"")]
    [InlineData(Formatter + "return writer.write(42)\n", "_ = \"value: \\(Value.init())\"")]
    [InlineData("group Helpers\n    public func text<T>(value: ref/T) -> string\n        T is Utf8Format\n        return Text.toString(value)\n", "_ = Helpers.text(42)")]
    [InlineData("group Helpers\n    public func pure<T>(value: T) -> T\n        T is Copy\n        return value\n", "_ = Helpers.pure(42)")]
    [InlineData("group Helpers\n    public func number<T>() -> i32 => 1\n    public func pure<T>(value: T, other: i32 = Helpers.number<T>()) -> T\n        T is Copy\n        return value\n", "_ = Helpers.pure(42)")]
    public void InputAuthorityAndImmutableStateAreAllowed(string prefix, string operation)
    {
        var c = Analyze(prefix, operation);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null) + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)));
    }

    [Theory]
    [InlineData(State, "_ = State.value")]
    [InlineData(State, "State.value += 1")]
    [InlineData(State + "group Helpers\n    public func read() -> i32 => State.value\n", "_ = Helpers.read()")]
    [InlineData(State + "group Helpers\n    public func read() -> i32 => State.value\ngroup Cache\n    public let value: i32 = Helpers.read()\n", "_ = Cache.value")]
    [InlineData(State + "struct Noisy\n    public init() => ()\n    deinit\n        _ = State.value\n", "_ = Noisy.init()")]
    [InlineData(State + "struct Initializer\n    let value: i32 = State.value\n", "_ = Initializer.init()")]
    [InlineData("", "Console.writeLine(\"external\")")]
    [InlineData(State + Formatter + "_ = State.value\n        return .Ok(())\n", "_ = \"value: \\(Value.init())\"")]
    [InlineData(State + Formatter + "_ = State.value\n        return .Ok(())\n", "_ = Text.toString(Value.init())")]
    public void AmbientOrUnknownEffectsInvalidateTheConformance(string prefix, string operation)
    {
        var c = Analyze(prefix, operation);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void BorrowingAFieldDoesNotExecuteItsDestructor()
    {
        const string Source = """
            group State
                public var value: i32 = 0
            struct Noisy
                public var number: i32 = 1
                deinit => State.value += 1
            struct Writer
                Self is BufferWriter
                let held: Noisy = Noisy.init()
                public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
                    _ = self.held.number
                    return .Err(BufferFull.init())
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
    }

    [Theory]
    [InlineData("", "self.items.clear()")]
    [InlineData("group Helpers\n    public func clear<T>(items: uniq/Array<T>) => items.clear()\n", "Helpers.clear(self.items)")]
    public void ClearingBorrowedArraysChecksElementDestruction(string helper, string operation)
    {
        var source = State + "struct Noisy\n    deinit => State.value += 1\n" + helper +
            "struct Writer {source}\n    Self is BufferWriter\n    let items: uniq/Array<Noisy> during source\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        " + operation + "\n        return .Err(BufferFull.init())";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void GenericFormattingChecksTheSelectedConcreteEffects()
    {
        var prefix = State + Formatter + "_ = State.value\n        return .Ok(())\n" +
            "group Helpers\n    public func text<T>(value: ref/T) -> string\n        T is Utf8Format\n        return Text.toString(value)\n";
        var c = Analyze(prefix, "_ = Helpers.text(Value.init())");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void FormattingRootsKeepTheirEffectsInsideReserveChecking()
    {
        const string Source = """
            struct Writer
                Self is BufferWriter
                public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
                    var bytes = [4 of 0@u8]
                    var buffer = Text.fixed(bytes@uniq)
                    var writer = Text.writer(buffer@uniq)
                    _ = $tryWrite(writer@uniq, "\(42)")
                    return .Err(BufferFull.init())
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
    }

    private static Compilation Analyze(string prefix, string operation)
        => MinimalEmissionTest.Analyze(prefix + Writer + "        " + operation + "\n        return .Err(BufferFull.init())");
}
