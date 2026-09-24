// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8ReserveEffectTest
{
    [Theory]
    [InlineData("==", false, false)]
    [InlineData("!=", false, false)]
    [InlineData("<", false, false)]
    [InlineData(">=", false, false)]
    [InlineData("==", true, false)]
    [InlineData("<", true, false)]
    [InlineData("==", false, true)]
    [InlineData("<", false, true)]
    [InlineData("==", true, true)]
    [InlineData("<", true, true)]
    public void ComparisonWitnessEffectsAreCheckedThroughOperators(string operation, bool generic, bool tuple)
    {
        var equality = operation is "==" or "!=";
        var prefix = State + $$"""
            struct Compared
                Self is Comparable
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    {{(equality ? "_ = State.value" : "_ = true")}}
                    return true
                public func compare(self: ref/Self, other: ref/Self) -> i32
                    {{(equality ? "_ = true" : "_ = State.value")}}
                    return 0
            group Helpers
                public func compareValues<T>(left: ref/T, right: ref/T) -> bool
                    T is Comparable
                    return left {{operation}} right

            """;
        var expression = generic ? "Helpers.compareValues(left@ref, right@ref)" : "left " + operation + " right";
        var values = tuple ? "let first = Compared.init()\n        let last = Compared.init()\n        let left = (first@ref, 1)\n        let right = (last@ref, 2)" : "let left = Compared.init()\n        let right = Compared.init()";
        var c = Analyze(prefix, values + "\n        _ = " + expression);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Theory]
    [InlineData("first.equals(last@ref)")]
    [InlineData("first.compare(last@ref)")]
    [InlineData("Helpers.equal(first@ref, last@ref)")]
    public void IntrinsicComparisonWitnessesHaveOnlyInputEffects(string expression)
    {
        const string Prefix = "group Helpers\n    public func equal<T>(left: ref/T, right: ref/T) -> bool\n        T is Equatable\n        return left == right\n";
        var c = Analyze(Prefix, "let first: i32 = 1\n        let last: i32 = 2\n        _ = " + expression);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
    }

    private const string State = "group State\n    public var value: i32 = 0\n";
    private const string Writer = "struct Writer\n    Self is BufferWriter\n    public var local: i32 = 1\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n";
    private const string Formatter = "struct Value\n    Self is Utf8Format\n    public init() => ()\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>\n        ";

    [Theory]
    [InlineData("let value = Noisy.init()\n        _ = value.item")]
    [InlineData("var value = Noisy.init()\n        value.item = 1")]
    public void CustomAccessorEffectsAreChecked(string operation)
    {
        const string Accessors = "struct Noisy\n    public var item: i32 = 0\n        get(self: ref/Self) -> i32 => State.value\n        set(self: uniq/Self, value: i32) -> () => State.value = value\n";
        var c = Analyze(State + Accessors, operation);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void NestedGenericDestructionEffectsAreChecked()
    {
        const string Nested = "struct Noisy\n    public init() => ()\n    deinit => State.value += 1\nstruct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n";
        var c = Analyze(State + Nested, "_ = Box<Noisy>.init(Noisy.init())");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

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

    [Theory]
    [InlineData("_ = values.tryGet(Key.init())", false)]
    [InlineData("_ = values.remove(Key.init())", false)]
    [InlineData("_ = values.tryInsert(Key.init(), 1)", false)]
    [InlineData("_ = values.insertOrReplace(Key.init(), 1)", false)]
    [InlineData("values[Key.init()] = 1", false)]
    [InlineData("_ = values[Key.init()]", false)]
    [InlineData("values.clear()", true)]
    [InlineData("_ = values.insertOrReplace(Key.init(), 1)", true)]
    public void DictionaryImplicitEqualityAndDestructionEffectsAreChecked(string operation, bool destruction)
    {
        var key = "struct Key\n    Self is Equatable\n    public func equals(self: ref/Self, other: ref/Self) -> bool\n        " +
            (destruction ? "return true\n    deinit => State.value += 1\n" : "_ = State.value\n        return true\n");
        var c = Analyze(State + key, "var values: Dictionary<Key, i32> = [:]\n        " + operation);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void PureDictionaryCallbacksAndCapacityOperationsAreAllowed()
    {
        var c = Analyze(string.Empty, "var values: Dictionary<i32, i32> = [:]\n        values.reserve(3)\n        _ = values.tryInsert(1, 2)\n        _ = values.tryGet(1)\n        _ = values.insertOrReplace(1, 3)\n        _ = values.remove(1)\n        values.clear()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
