// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryIndexableTest
{
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
