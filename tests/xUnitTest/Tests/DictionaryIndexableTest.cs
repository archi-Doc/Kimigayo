// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryIndexableTest
{
    [Fact]
    public void SubscriptsSelectTheSameSourceEntries()
    {
        var c = MinimalEmissionTest.Analyze("var values = [1: 42]\nvalues[1] = 43\nrequire values[1] == 43 else => $abort(\"value\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.GetSymbol(KimiDeclarationId.DictionaryIndex)!.Declaration));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.GetSymbol(KimiDeclarationId.DictionaryIndexUniq)!.Declaration));
        ScalarEmissionTest.WriteFixture("DictionaryIndexableSubscript", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    // SPEC 4.6, 4.6.9: a Dictionary takes a key of its key Type, so a range literal is a Range key, not a slice; only the
    // concrete sequences read a range as a slice.
    [Fact]
    public void ARangeLiteralIsAKeyOfARangeKeyedDictionary()
    {
        const string Source = """
            var spans: Dictionary<Range<i32, i32>, string> = [1..2: "a"]
            let key: Range<i32, i32> = 1..2
            Console.writeLine(spans[key])
            spans[1..2] = "b"
            Console.writeLine(spans[1..2])
            """;
        ScalarEmissionTest.EmitFixture("DictionaryIndexableRangeKey", Source, "a\nb\n");
    }

    [Fact]
    public void ARangeLiteralSelectsAUserRangeIndexable()
    {
        const string Source = """
            struct Spans
                Self is Indexable<Range<i32, i32>>
                associate Element is i32
                var value: i32
                public init(value: i32) => self.value = value
                public func index(self, key: ref/Range<i32, i32>) -> place ref/i32 during self => self.value
            let spans = Spans.init(7)
            let key: Range<i32, i32> = 1..2
            require spans[key] == 7 and spans[1..2] == 7 else => $abort("range key")
            Console.writeLine("ok")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryIndexableUserRangeKey", Source, "ok\n");
    }

    [Fact]
    public void AReplacedKeyRebuildsTheSynthesizedIndexCall()
    {
        // The synthesized index call is cached per index expression; after an edit replaces the key it must name the new key,
        // which is bound once, instead of the detached old one.
        const string Prefix = "var values = [1: 42, 2: 43]\nlet k: i32 = 1\nlet j: i32 = 2\n";
        var c = MinimalEmissionTest.Analyze(Prefix + "let r = values[k]");
        var index = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.IndexKoto>().Single();
        Assert.NotNull(c.Binding.IndexerCall(index, false));
        var replacement = KotoTree.Walk(MinimalEmissionTest.Analyze(Prefix + "let r = values[j]").Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.IndexKoto>().Single().Right;
        Assert.True(KotoHelper.Replace(index, index.Right, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Same(replacement, c.Binding.IndexerCall(index, false)!.ArgumentNodes[0]);
        Assert.Equal(BindingState.Resolved, replacement.BindingState);
    }

    [Fact]
    public void DirectEntriesPreserveStoredKeysAndSourceLoans()
    {
        const string Source = """
            var values = [1: "before", 2: "second"]
            Console.writeLine(values.index(1))
            values.indexUniq(1) = "after"
            let found = values.index(1)@ref
            Console.writeLine(found)
            for (key, value) in values => Console.writeLine(value)
            """;
        ScalarEmissionTest.EmitFixture("DictionaryIndexableDirect", Source, "before\nafter\nafter\nsecond\n");
    }

    [Theory]
    [InlineData("Shared", "let result = values.index(9)", 14)]
    [InlineData("Exclusive", "values.indexUniq(9) = 3", 1)]
    public void MissingKeysReportTheActualCallAfterEarlierSuccessfulCalls(string name, string access, int column)
    {
        const string Prefix = "var values = [1: 42]\nrequire values.index(1) == 42 else => $abort(\"existing\")\nvalues.indexUniq(1) = 43\n";
        ScalarEmissionTest.EmitFixture("DictionaryIndexableMissing" + name, Prefix + access, string.Empty, 1, $"Hello.kimi:4:{column}: abort KIMI_E_MISSING_KEY: Dictionary key was not found\n");
    }

    [Fact]
    public void GenericMissingKeysReportTheIndexInsideTheGenericBody()
    {
        const string Source = """
            func read<D, K>(values: ref/D, key: ref/K) -> place ref/D.Element during values
                D is Indexable<K>
                return values[key]
            var values = [1: 42]
            require read(values, 1) == 42 else => $abort("existing")
            require read(values, 9) == 0 else => $abort("unreachable")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryIndexableGenericMissing", Source, string.Empty, 1, "Hello.kimi:3:12: abort KIMI_E_MISSING_KEY: Dictionary key was not found\n");
    }

    [Theory]
    [InlineData("let view = values.index(1)@ref\nvalues.clear()\nrequire view == 42 else => $abort(\"view\")")]
    [InlineData("let view = values.indexUniq(1)@uniq\nvalues.indexUniq(1) = 3\nrequire view == 42 else => $abort(\"view\")")]
    public void PublishedPlacesRetainTheReceiverLoan(string use)
    {
        var c = MinimalEmissionTest.Analyze("var values = [1: 42]\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Fact]
    public void DictionaryPublishesPlacesThroughGenericIndexableConstraints()
    {
        const string Source = """
            func read<D, K>(values: ref/D, key: ref/K) -> place ref/D.Element during values
                D is Indexable<K>
                return values[key]
            func slot<D, K>(values: uniq/D, key: ref/K) -> place uniq/D.Element during values
                D is UniqIndexable<K>
                return values[key]
            var values = [1: "before"]
            Console.writeLine(read(values, 1))
            slot(values@uniq, 1) = "after"
            Console.writeLine(read(values, 1))
            """;
        ScalarEmissionTest.EmitFixture("DictionaryIndexableGeneric", Source, "before\nafter\n");
    }
}
