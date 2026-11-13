// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public partial class TypeDagScalingTest
{
    // R7-C3a (design review 1, B2): the identity classes are linear in the premises' distinct parts. A Tuple tree of depth d (d premises,
    // 2^d paths) and a chain of n identities cost nodes proportional to d and n; a cycle of n steps is found with no depth bound and
    // recorded once at the declaration (SPEC 8.2.3 Cycles).
    [Theory]
    [InlineData("tree")]
    [InlineData("chain")]
    public void IdentityClassesAreLinearInThePremises(string shape)
    {
        var (small, large) = (Work(shape, 16), Work(shape, 32));
        Assert.True(large <= 2 * small, $"{shape}: {small} at 16, {large} at 32");

        static int Work(string shape, int n)
        {
            var c = MinimalEmissionTest.Analyze(IdentitySource(shape, n));
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityClassWork;
        }
    }

    [Fact]
    public void AnIdentityCycleIsFoundWithoutADepthBound()
        => Assert.Equal("1*InvalidConstraint_Kd", DiagnosticCorpus.RecordProfile(IdentitySource("cycle", 70)));

    // R7-C3a (the provisional path's bound, Binding.TypeIdentity's contract): a table read under a Provisional availability (a Contract's
    // supplied identity) is rebuilt at each read, so its work is the reads times the view's premise nodes: linear in the uses, and in
    // the premises their own clauses' reads times their nodes.
    [Theory]
    [InlineData("uses", 2)]
    [InlineData("premises", 4)]
    public void ProvisionalIdentityWorkIsReadsTimesPremises(string grown, int factor)
    {
        var (small, large) = (Work(8, 8), grown == "uses" ? Work(16, 8) : Work(8, 16));
        Assert.True(large <= factor * small, $"{grown}: {small} at 8, {large} at 16");

        static int Work(int uses, int premises)
        {
            var parameters = string.Concat(Enumerable.Range(0, premises).Select(static i => $", U{i}"));
            var source = "contract K1\n    associate A\n    associate C\n    Self.(K1).A is Option<Self.(K1).C>\n" +
                $"func f<T{parameters}>(c: T.(K1).C) -> i32\n    T is K1\n    T.(K1).C is i32\n" +
                string.Concat(Enumerable.Range(0, premises).Select(static i => $"    U{i} is i32\n")) +
                string.Concat(Enumerable.Range(0, uses).Select(static i => $"    let x{i}: T.(K1).C = c\n")) + "    return 0\n";
            var c = MinimalEmissionTest.Analyze(source + "public func main() -> ()\n    return\n");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityClassWork;
        }
    }

    // R7-C2c2 c2-1 (design review 2, D): the premise supply's class keys cost one key per distinct Type a table compares, n = 8, 32, 128:
    // linear in a chain of n parameter identities (one class of n + 1 members, which each key's Representative scans); for a demand n
    // projections deep under `U is T`, whose view gains a Contract fact per prefix (another serial, another table) and whose requests each
    // index every fact (a1's preparation), at most one key per indexed fact, quadratic in n as the index is (cubic while ClassKey kept no
    // key: 11,285 at 8, 11,849,545 at 128). Both programs are valid (SPEC 8.2.3, 8.3.2): CopyTree supplies Copy.
    [Theory]
    [InlineData("parameters", 4)]
    [InlineData("depth", 16)]
    public void ClassKeyWorkIsAKeyPerComparedType(string shape, int factor)
    {
        var (w8, w32, w128) = (Work(shape, 8), Work(shape, 32), Work(shape, 128));
        Assert.True(w32 <= factor * w8 && w128 <= factor * w32, $"{shape}: {w8} at 8, {w32} at 32, {w128} at 128");

        static long Work(string shape, int n)
        {
            var c = MinimalEmissionTest.Analyze(KeySource(shape, n));
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityKeyWork;
        }
    }

    private static string KeySource(string shape, int n)
    {
        const string Copies = "contract Tree\n    associate Child is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n";
        if (shape == "parameters")
        {
            var parameters = string.Join(", ", Enumerable.Range(0, n + 1).Select(static i => $"T{i}"));
            var chain = string.Concat(Enumerable.Range(1, n).Select(static i => $"    T{i} is T{i - 1}\n"));
            return Copies + $"func twice<{parameters}>(value: T{n}.(Tree).Child) -> (T{n}.(Tree).Child, T{n}.(Tree).Child)\n" + chain +
                "    T0 is CopyTree\n    return (value, value)\npublic func main() -> ()\n    return\n";
        }

        var demand = "U" + string.Concat(Enumerable.Repeat(".(Tree).Child", n));
        return Copies + $"func twice<T, U>(value: {demand}) -> ({demand}, {demand})\n    U is T\n    T is CopyTree\n    return (value, value)\n" +
            "public func main() -> ()\n    return\n";
    }

    private static string IdentitySource(string shape, int n)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, n + 1).Select(static i => $"T{i}"));
        var premises = shape switch
        {
            "tree" => Enumerable.Range(0, n).Select(static i => $"    T{i} is (T{i + 1}, T{i + 1})\n"),
            "chain" => Enumerable.Range(0, n).Select(static i => $"    T{i} is T{i + 1}\n").Append($"    T{n} is i32\n"),
            _ => Enumerable.Range(0, n).Select(static i => $"    T{i} is Option<T{i + 1}>\n").Append($"    T{n} is Option<T0>\n"),
        };

        return $"func f<{parameters}>(v: T0) -> i32\n" + string.Concat(premises) + "    return 0\npublic func main() -> ()\n    return\n";
    }
}
