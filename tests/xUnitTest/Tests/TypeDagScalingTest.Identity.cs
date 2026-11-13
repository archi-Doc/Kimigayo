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
