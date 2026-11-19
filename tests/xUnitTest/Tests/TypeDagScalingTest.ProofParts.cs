// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public partial class TypeDagScalingTest
{
    // R7-C2e2 e2-3b-cached (the cut's delta review X1, e2 ruling 84; DECISIONS BU-19, BU-64): a requirement's evidence keeps a projection
    // its route holds pending in the image it reads (SPEC 8.3.3 Finality), so the proof normalization walks that image; it normalizes each
    // distinct part once per query, linear in a diamond chain of N cached bindings over such a bottom (`Box<S>.(K).X`, Box's `T is C`
    // resting on `S is C`), whose paths are 2^N as a tree. Valid by SPEC 8.6.3: P is M unconditionally, so `Item is M` reads no part of P.
    [Fact]
    public void AProofNormalizesEachDistinctPartOnce()
    {
        var (w10, w20, w30) = (Work(10), Work(20), Work(30));
        Assert.True(w30 - w20 <= 2 * (w20 - w10), $"{w10} at 10, {w20} at 20, {w30} at 30");

        static int Work(int n)
        {
            var c = MinimalEmissionTest.Analyze(HeldDiamond(n) + "public func main() -> ()\n    return\n");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.ProofPartWork;
        }

        static string HeldDiamond(int n)
            => "contract M\ncontract K\n    associate X\ncontract D\n    associate Y\ncontract C\n    associate Item is M\nstruct R\n    Self is M\n" +
                "struct P<A, B>\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\n" +
                "struct L0\n    Self is D\n    associate D.Y is P<Box<S>.(K).X, Box<S>.(K).X>\n" +
                string.Concat(Enumerable.Range(1, n).Select(static k => $"struct L{k}\n    Self is D\n    associate D.Y is P<L{k - 1}.(D).Y, L{k - 1}.(D).Y>\n")) +
                $"struct S\n    Self is C\n    associate C.Item is L{n}.(D).Y\n";
    }
}
