// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public partial class TypeDagScalingTest
{
    // R7-C2e2 G1001 (C-407 N1; SPEC 8.5.5): the walks of an inferred binding take one step per distinct part past their memos, linear in
    // diamond chains of N whose paths are 2^N as a tree. The requirement writes `P<GN<Item>.(D).Y, GN<Item>.(D).Y>`, 2^N hole positions
    // in each half; S writes each half as a projection over a diamond with a held bottom (LN over `Box<S>.(K).X`, NN over
    // `Box2<S>.(K).X`): the extraction follows the written `P` and pairs each half's diamond below its written projection, the requirement's
    // part its anchor (ExtractAssociatedEvidence); each source's evidence pairs the anchor's diamond with its formed part
    // (InferredEvidence); the two positions' images, distinct diamonds, join (JoinEvidence). Invalid by SPEC 8.6.3's example (the result
    // compatibility normalizes `Box<S>.(K).X` through `T is C`, which rests on `S is C`) and accepted as dev, G629's: the row reads only the
    // work, never the verdict.
    [Fact]
    public void AnInferredBindingsWalksTakeEachDistinctPartOnce()
    {
        var (w30, w60, w120) = (Work(30), Work(60), Work(120));
        Assert.True(w30 < w60 && w120 - w60 <= 2 * (w60 - w30), $"{w30} at 30, {w60} at 60, {w120} at 120");

        static int Work(int n)
            => MinimalEmissionTest.Analyze(HeldHalves(n) + "public func main() -> ()\n    return\n").Binding.InferredEvidenceWork;

        static string HeldHalves(int n)
            => "contract M\ncontract K\n    associate X\ncontract D\n    associate Y\ncontract C\n    associate Item is M\n" +
                $"    func get(self: ref/Self) -> P<G{n}<Item>.(D).Y, G{n}<Item>.(D).Y>\nstruct R\n    Self is M\nstruct P<A, B>\n    Self is M\n" +
                "struct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is C\n    Self is K\n    associate K.X is R\n" +
                "struct G0<T>\n    Self is D\n    associate D.Y is T\n" + Chain("L", n, "Box") + Chain("N", n, "Box2") +
                string.Concat(Enumerable.Range(1, n).Select(static k => $"struct G{k}<T>\n    Self is D\n    associate D.Y is P<G{k - 1}<T>.(D).Y, G{k - 1}<T>.(D).Y>\n")) +
                $"struct S\n    Self is C\n    public func get(self: ref/Self) -> P<G{n}<L{n}.(D).Y>.(D).Y, G{n}<N{n}.(D).Y>.(D).Y> => $abort(\"x\")\n";

        static string Chain(string name, int n, string box)
            => $"struct {name}0\n    Self is D\n    associate D.Y is P<{box}<S>.(K).X, {box}<S>.(K).X>\n" +
                string.Concat(Enumerable.Range(1, n).Select(k => $"struct {name}{k}\n    Self is D\n    associate D.Y is P<{name}{k - 1}.(D).Y, {name}{k - 1}.(D).Y>\n"));
    }
}
