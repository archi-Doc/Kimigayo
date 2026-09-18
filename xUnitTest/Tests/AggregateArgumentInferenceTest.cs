// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AggregateArgumentInferenceTest
{
    [Theory]
    [InlineData("func use(value: (u8, bool)) => ()\nuse((255, true))")]
    [InlineData("func use(value: ([1 of u8], bool)) => ()\nuse(([255], true))")]
    [InlineData("func use<T>(value: (T, bool), evidence: T) => ()\nlet n: u8 = 2\nuse((255, true), n)")]
    [InlineData("func use<T>(value: [1 of (T, bool)]) => ()\nlet n: u8 = 2\nuse([(n, true)])")]
    [InlineData("func use<length N, T>(value: ([N of T], bool)) => ()\nlet n: u8 = 2\nuse(([255, n], true))")]
    [InlineData("func use<T>(value: (T, T)) => ()\nlet n: u8 = 2\nuse((255, n))")]
    [InlineData("func use<T>(value: (T, T)) => ()\nlet n: u8 = 2\nuse((n, 255))")]
    [InlineData("func use<T>(first: T, second: T) => ()\nlet n: u8 = 2\nuse((255, true), (n, true))")]
    [InlineData("func use<T>(first: T, second: T) => ()\nlet n: u8 = 2\nuse((n, true), (255, true))")]
    [InlineData("func use<length N, T>(value: ([N of T], bool)) => ()\nuse<0, u8>(([], true))")]
    [InlineData("func use origin a(value: (ref/i32 from a, bool)) => ()\nfunc forward origin b(x: ref/i32 from b) => use((x, true))")]
    [InlineData("func use origin a(value: [1 of (ref/i32 from a, bool)]) => ()\nfunc forward origin b(x: ref/i32 from b) => use([(x, true)])")]
    public void CollectsStructuralEvidenceBeforeDefaulting(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Theory]
    [InlineData("func use(value: (i32, bool)) => ()\nfunc use(value: (i64, bool)) => ()\nuse((1, true))")]
    [InlineData("func use(value: (i64, bool)) => ()\nfunc use(value: (i32, bool)) => ()\nuse((1, true))")]
    public void LiteralDefaultsCannotSelectAnAggregateOverload(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var call = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingFailure.Ambiguous, call.BindingFailure);
        Assert.Null(call.BoundCall);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("func use<T>(value: T) => ()\nuse((1, true))")]
    [InlineData("func use<T>(value: T) => ()\nuse(((1, true), (2.0,)))")]
    [InlineData("func use(value: ref/(i32, bool)) => ()\nuse((1, true))")]
    [InlineData("func use<T>(value: ref/T) => ()\nuse((1, true))")]
    [InlineData("func use(value: ()) => ()\nuse(())")]
    public void PreservesIndependentTupleInferenceAndTemporaryBorrows(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("func use(value: (u8, bool)) => ()\nuse((256, true))")]
    [InlineData("func use(value: (u8, bool)) => ()\nuse((-1, true))")]
    [InlineData("func use(value: (u8, bool)) => ()\nuse((1 + 2, true))")]
    [InlineData("func use(value: (u8, bool)) => ()\nlet n: i32 = 1\nuse((n, true))")]
    [InlineData("func use(value: ([1 of u8], bool)) => ()\nuse(([1, 2], true))")]
    [InlineData("func use<length N>(value: ([N of i32], [N of i32])) => ()\nuse(([1], [1, 2]))")]
    [InlineData("func use(value: (i32, bool)) => ()\nuse((1, true, 2))")]
    [InlineData("func use(value: uniq/(i32, bool)) => ()\nuse((1, true))")]
    public void RejectsInvalidAggregateCandidates(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var call = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingFailure.NoApplicableCandidate, call.BindingFailure);
        Assert.Null(call.BoundCall);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("Byte", "func use(value: (u8, bool)) -> u8 => value.0\nif use((255, true)) == 255 => Console.writeLine(\"ok\")")]
    [InlineData("Nested", "func use(value: ([1 of u8], (bool, string))) => Console.writeLine(value.1.1)\nuse(([255], (true, \"ok\")))")]
    [InlineData("Range", "func use(value: (i8, bool)) => Console.writeLine(\"wrong\")\nfunc use(value: (u8, bool)) => Console.writeLine(\"ok\")\nuse((255, true))")]
    [InlineData("Borrow", "func use(value: ref/(u8, bool)) -> u8 => value.0\nif use((255, true)) == 255 => Console.writeLine(\"ok\")")]
    [InlineData("Float", "func use(value: (f32, bool)) -> f32 => value.0\nif use((1.5, true)) == 1.5 => Console.writeLine(\"ok\")")]
    public void EmitsCandidateFittedTuples(string name, string source)
        => ScalarEmissionTest.EmitFixture("AggregateArgument" + name, source, "ok\n");

    [Fact]
    public void AcquiresNamedArgumentsAndTupleElementsInSourceOrder()
        => ScalarEmissionTest.EmitFixture("AggregateArgumentOrder", "func mark(label: string) -> u8\n    Console.writeLine(label)\n    return 1\nfunc use(first: (u8, u8), second: (u8, u8)) => Console.writeLine(\"body\")\nuse(second: (mark(\"one\"), mark(\"two\")), first: (mark(\"three\"), 255))", "one\ntwo\nthree\nbody\n");

    [Fact]
    public void SelectedOwnedTupleHasOneCleanupResponsibility()
    {
        const string Source = "func use(value: (u8, string)) => ()\nfunc use(value: (i8, string)) => ()\nuse((255, \"owned\"))";
        var ir = ScalarEmissionTest.EmitFixture("AggregateArgumentCleanup", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateArgumentCleanup", Source, ir, string.Empty, "owned=1", order: [0]);
    }

    [Fact]
    public void WarmAggregateCandidateInferenceAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func use<length N, T>(value: ([N of T], bool)) => ()\nlet n: u8 = 2\nuse(([255, n], true))");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Aggregate argument inference failed.");
            }
        }));
    }

    private static IEnumerable<Koto> Nodes(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Nodes(child))
            {
                yield return descendant;
            }
        }
    }
}
