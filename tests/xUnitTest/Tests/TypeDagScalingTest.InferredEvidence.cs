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

    // R7-C2e2 e2-5b 3a (design-e25b-p0.md R3.1.1's cyclic row; DECISIONS BU-64): a diamond of depth N over c1-g's cycle, `C.Item` the
    // diamond's top and its bottom `S.(C).Y`, whose image reaches `S.(C).Item` again (the guard's hit, ContractTypes' normalizingAssociated):
    // every level's formation contains the hit, so each keeps its call's query and is formed once per call: dev's order, which BU-64's
    // open limit N2 bounds (the guard path runs one nested query per normalized level, O(sum of subtree sizes)), a doubling ratio
    // w(2n) <= 4.5 w(n), where a memo that kept none would walk the diamond as a tree (2^N; CC-50 C1's rejected form). The batch twin
    // reads the same parts through a hole Z the result binds. Both keep dev's records (cyc2's shape); the row reads only the work.
    [Fact]
    public void AContractTypeOverACycleFormsEachLevelOncePerCall()
    {
        foreach (var hole in new[] { false, true })
        {
            var (w30, w60, w120) = (Work(30, hole), Work(60, hole), Work(120, hole));
            Assert.True(w30 < w60 && w60 <= 4.5 * w30 && w120 <= 4.5 * w60, $"hole {hole}: {w30} at 30, {w60} at 60, {w120} at 120");
        }

        static int Work(int n, bool hole)
            => MinimalEmissionTest.Analyze(Cycle(n, hole) + "public func main() -> ()\n    return\n").Binding.ContractPartWork;

        static string Cycle(int n, bool hole)
            => "contract M\ncontract D\n    associate Y\ncontract C\n    associate Item is M\n    associate Y is M\n" + (hole ? "    associate Z\n" : string.Empty) +
                $"    func get(self: ref/Self) -> Pair<Self.Item, Self.{(hole ? "Z" : "Y")}>\nstruct Pair<A, B>\n    Self is M\n" +
                "struct L0\n    Self is D\n    associate D.Y is S.(C).Y\n" +
                string.Concat(Enumerable.Range(1, n).Select(static k => $"struct L{k}\n    Self is D\n    associate D.Y is Pair<L{k - 1}.(D).Y, L{k - 1}.(D).Y>\n")) +
                $"struct S\n    Self is C\n    associate C.Item is Pair<L{n}.(D).Y, i32>\n    associate C.Y is Pair<S.(C).Item, i32>\n" +
                "    public func get(self: ref/Self) -> Pair<S.(C).Item, S.(C).Y> => $abort(\"x\")\n";
    }

    // R7-C2e2 e2-5b 3b3a-2 (CC-77 P1, CC-78; PLAN G1497; SPEC 8.5.5, 8.6.3): a ladder whose requirement reads each rung's inferred X (valid:
    // every X is R, and R is M). Each binding's evidence answer reads no obligation, so it is kept for every evidence and formed once
    // (linear), where none kept walks the ladder as a tree (2^n).
    [Fact]
    public void ARequirementLadderFormsEachInferredEvidenceOnce()
    {
        var (w4, w6, w8) = (Work(4), Work(6), Work(8));
        Assert.True(w4 < w6 && w8 - w6 <= 2 * (w6 - w4), $"{w4} at 4, {w6} at 6, {w8} at 8");

        static int Work(int n)
        {
            var c = MinimalEmissionTest.Analyze(Verification.AssociatedInferenceWorkloads.RequirementLadder(n, "R"));
            Assert.Empty(CompilationTestHelper.BindingErrors(c));
            return c.Binding.InferredEvidenceWork;
        }
    }

    // The same ladder over `Box<G>.(K).X` (valid: Box's `T is C` holds at G): each answer opens (G, C) fresh, so it is kept for its
    // evidence alone and each binding is formed once per evidence, 3n + 1 evidences (n^2), where none kept is 2^n.
    [Fact]
    public void AGenericConstrainedLadderFormsEachInferredEvidenceOncePerEvidence()
    {
        var (w4, w6, w8) = (Work(4), Work(6), Work(8));
        Assert.True(w4 < w6 && w8 - w6 <= 2 * (w6 - w4), $"{w4} at 4, {w6} at 6, {w8} at 8");

        static int Work(int n)
        {
            var c = MinimalEmissionTest.Analyze("contract C\nstruct G\n    Self is C\ncontract K\n    associate X\nstruct Box<T>\n    T is C\n    Self is K\n" +
                "    associate K.X is R\n" + Verification.AssociatedInferenceWorkloads.RequirementLadder(n, "Box<G>.(K).X"));
            Assert.Empty(CompilationTestHelper.BindingErrors(c));
            return c.Binding.InferredEvidenceWork;
        }
    }
}
