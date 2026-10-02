// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryLiteralEmissionTest
{
    [Theory]
    [InlineData("Inferred", "let entries = [1: 10, 2: 20]\nif entries.length == 2 and entries[1] == 10 and entries[2] == 20 => Console.writeLine(\"ok\")")]
    [InlineData("Strings", "let entries: Dictionary<i32, string> = [1: \"first\", 2: \"second\"]\nConsole.writeLine(entries[1])\nConsole.writeLine(entries[2])", "first\nsecond\n")]
    [InlineData("Order", "func key(value: i32) -> i32\n    Console.writeLine(\"key\")\n    return value\nfunc value() -> i32\n    Console.writeLine(\"value\")\n    return 1\nlet entries = [key(1): value(), key(2): value()]", "key\nvalue\nkey\nvalue\n")]
    public void NonemptyLiteralsConstructEntriesInOrder(string name, string source, string expected = "ok\n")
        => ScalarEmissionTest.EmitFixture("DictionaryLiteral" + name, source, expected);

    [Theory]
    [InlineData("Computed", "i32", "1", "1 + 0")]
    [InlineData("SignedZero", "f64", "0.0", "-0.0")]
    [InlineData("NaN", "f64", "0.0 / 0.0", "0.0 / 0.0")]
    [InlineData("Tuple", "(i32, i32)", "(1, 2)", "(1, 2)")]
    public void RuntimeDuplicatesAbortBeforeTheValueAndLaterEntries(string name, string type, string first, string second)
    {
        var source = "func value() -> i32\n    Console.writeLine(\"value\")\n    return 1\n" +
            "let entries: Dictionary<" + type + ", i32> = [" + first + ": value(), " + second + ": value()]\nConsole.writeLine(\"bad\")";
        var column = source.LastIndexOf(second + ": value()", StringComparison.Ordinal) - source.LastIndexOf('\n', source.IndexOf("let entries", StringComparison.Ordinal));
        ScalarEmissionTest.EmitFixture(
            "DictionaryLiteralDuplicate" + name,
            source,
            "value\n",
            1,
            "Hello.kimi:4:" + column + ": abort KIMI_E_DUPLICATE_KEY: Dictionary literal contains an equivalent key\n");
    }

    [Fact]
    public void StoredKeyIsTheEqualityReceiverBeforeValueAcquisition()
    {
        const string source = """
            struct Key
                Self is Equatable
                public let code: i32
                public let tag: i32
                public init(code: i32, tag: i32)
                    self.code = code
                    self.tag = tag
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    require self.tag == 1 else => $abort("receiver")
                    Console.writeLine("equals")
                    return self.code == other.code
            func value() -> i32
                Console.writeLine("value")
                return 1
            let entries = [Key.init(1, 1): value(), Key.init(2, 2): value()]
            """;
        ScalarEmissionTest.EmitFixture("DictionaryLiteralEqualityOrder", source, "value\nequals\nvalue\n");
    }

    [Fact]
    public void ValuesAndKeysAreDestroyedOnceInReverseEntryOrder()
    {
        const string source = """
            struct Item
                Self is Equatable
                public let name: string
                public init(name: string) => self.name = name@move
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.name == other.name
                drop => Console.writeLine(self.name)
            let entries = [Item.init("key1"): Item.init("value1"), Item.init("key2"): Item.init("value2")]
            """;
        ScalarEmissionTest.EmitFixture("DictionaryLiteralCleanup", source, "value2\nkey2\nvalue1\nkey1\n");
    }

    [Theory]
    [InlineData("Key", "(label key: do\n        return\n        exit to key 2\n    ): Item.init(\"bad\")", "first\nafter\n")]
    [InlineData("Value", "2: (label value: do\n        return\n        exit to value Item.init(\"bad\")\n    )", "first\nafter\n")]
    public void AbruptAcquisitionDestroysThePartialDictionary(string name, string entry, string expected)
    {
        var source = "struct Item\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n" +
            "func run()\n    let entries: Dictionary<i32, Item> = [1: Item.init(\"first\"), " + entry + "]\nrun()\nConsole.writeLine(\"after\")";
        ScalarEmissionTest.EmitFixture("DictionaryLiteralReturn" + name, source, expected);
    }

    [Fact]
    public void GenericLiteralUsesConcreteWitnessAndStoredResult()
    {
        const string source = """
            func make<K, V>(key: K, value: V) -> Dictionary<K, V>
                K is Equatable
                K is Owned
                V is Owned
                return [key@move: value@move]
            let entries = make((1, 2), "ok")
            Console.writeLine(entries[(1, 2)])
            """;
        ScalarEmissionTest.EmitFixture("DictionaryLiteralGeneric", source, "ok\n");
    }

    [Fact]
    public void AReturnFromTheValueDestroysTheAcquiredKeyBeforeEarlierEntries()
    {
        const string source = """
            struct Item
                Self is Equatable
                public let name: string
                public init(name: string) => self.name = name@move
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.name == other.name
                drop => Console.writeLine(self.name)
            func run()
                let entries: Dictionary<Item, Item> = [Item.init("old key"): Item.init("old value"), Item.init("new key"): (label value: do
                    return
                    exit to value Item.init("bad")
                )]
            run()
            Console.writeLine("after")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryLiteralReturnOwnedKey", source, "new key\nold value\nold key\nafter\n");
    }

    [Fact]
    public void AcquiredInputsCannotBeMovedAgainAfterConstruction()
    {
        var c = MinimalEmissionTest.Analyze("let value = \"owned\"\nlet entries = [1: value@move]\nlet again = value@move");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void DependentLiteralStorageUsesTheOrdinaryConstructionPath()
    {
        var c = MinimalEmissionTest.Analyze("let value = 1\nlet entries = [1: value@ref]");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var error), error);
    }

    [Fact]
    public void RebindingAndReloadKeepLiteralGenerationStable()
    {
        const string source = "let entries = [(1, 2): \"ok\"]\nConsole.writeLine(entries[(1, 2)])";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryRequireAbsent));
        var original = CompilationTestHelper.WriteIr(c);
        var loaded = CompilationTestHelper.Reload(c);
        Assert.True(loaded.Bind().IsComplete);
        loaded.Binding.CheckStartup(OutputKind.Application);
        Assert.True(loaded.Ownership.Analyze().IsVerified);
        Assert.Equal(original, CompilationTestHelper.WriteIr(loaded));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.Equal(original, CompilationTestHelper.WriteIr(c));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Unit", "let entries = [(): ()]", 64)]
    [InlineData("Bool", "let entries = [true: false, false: true]", 96)]
    [InlineData("Integers", "let entries = [1: 10, 2: 20, 3: 30, 4: 40]", 96)]
    public void LiteralConstructionAllocatesOnlyTheEntryBuffer(string name, string source, long bytes)
        => NativeAllocationAudit.WriteFixture("DictionaryLiteralCost" + name, source, 1, 1, bytes);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLiteralCompilationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let entries = [(1, 2): \"one\", (2, 3): \"two\"]");
        for (var i = 0; i < 4; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete || !c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Dictionary literal compilation failed.");
            }
        }));
    }
}
