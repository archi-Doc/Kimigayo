// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// A5-F4's verdicts on the OriginProof families (2a0-D §10's 2a-4): the twin of OriginProofScalingTest and OriginObligationVerdictTest,
// which read the published records and Binding's judge; and X-5 N3's relations by variance between fixed Origins.
public sealed partial class MirOriginsTest
{
    // X-5 N3's callers after Holder and Callback (line 7 on): a covariant `Option` value at a call, a contravariant value call (C's slot
    // is named only by a stored Function Type Field's parameter), an invariant `uniq` target at a call, the same three at a `return` fit
    // and a callee with a clause.
    private const string CovariantCall = "func put(slot: uniq/(ref/i32 during t), value: Option<ref/i32 during t>) => ()\n" +
        "func run(slot: uniq/(ref/i32 during a), value: Option<ref/i32 during b>)\n";

    private const string ContravariantCall = "func run(value: C during b, k: (C during a) -> (), anchor: ref/i32 during a)\n";

    private const string InvariantCall = "func tie(slot: uniq/(ref/i32 during t), other: uniq/Option<ref/i32 during t>) => ()\n" +
        "func run(slot: uniq/(ref/i32 during a), other: uniq/Option<ref/i32 during b>)\n";

    private const string CovariantFit = "func run(value: Option<ref/i32 during b>) -> Option<ref/i32 during a>\n";

    private const string ContravariantFit = "func run(value: C during b) -> C during a\n";

    private const string InvariantFit = "func run(value: uniq/(ref/i32 during b) during c) -> uniq/(ref/i32 during a) during c\n";

    private const string Link = "func link(x: uniq/(ref/i32 during u), y: uniq/(ref/i32 during w))\n";

    private const string LinkReturnAndCaller = "    return\nfunc run(p: uniq/(ref/i32 during a), q: uniq/(ref/i32 during b))\n";

    private const string PutMove = "    put(slot, value@move)\n";

    private const string ReturnMove = "    return value@move\n";

    // SPEC 15.3.4-15.3.7, 15.6.1, 15.6.5: on the MIR side a relation the visible premises prove is accepted and one they do not is one
    // UnprovenOriginRelation_Kd per unproven conjunct, A5's (R7-O1 H: Binding judges no body relation).
    [Theory]
    [MemberData(nameof(OriginProofScalingTest.Families), MemberType = typeof(OriginProofScalingTest))]
    public void OriginProofFamiliesHaveTheirOutcomeOnTheMir(string family, string? code)
    {
        foreach (var size in new[] { 1, 2, 8 })
        {
            var c = MinimalEmissionTest.Analyze(Verification.OriginProofWorkloads.Create(family, size));
            Assert.Equal(code?.Split(' ') ?? [], MirTargetSide.MirSide(c).Select(static x => x.Code.ToString()));
        }
    }

    // X-5 N3 (SPEC 15.3.5, 15.6.1): each Origin position relates by its variance, at a call and at a fit between fixed Origins, and A5
    // alone judges it (R7-M66): `outlives` at a covariant position (A, D: an `Option` value, a local's annotation at its initializer), the
    // reverse at a contravariant one (B, E: a value call, a solved Type parameter, a `return` fit and under a Type argument; C's slot is
    // named only by a stored Function Type Field's parameter, hence contravariant, SPEC 15.3.5 and the owner's O-536 (a)) and `==` at an
    // invariant one (C, F: a `uniq` target, one record failing both ways, else its failing direction). A substituted clause stands at the
    // first input naming its longer Origin (G); G3 is 15.6.1's "several fixed Origins" sentence, reached through a declared `==`. A pair
    // layer's target is invariant where `uniq` is admitted (H3: the only A5 row for a generic pair layer's invariant target, the verdict's
    // layer since R7-M66; PairVarianceTest asserts the corpus record), and a case's well-formedness premise serves the case that requires
    // it (H4, SPEC 15.6.5). Each I row is one record per failing position and nothing else (SPEC 23.1.2); the Unknown part's R7Inv,
    // Phantom, h0@move, ClauseBound, r7-subst, c2, c22 and j-pick rows hold the other cells.
    [Theory]
    [InlineData(CovariantCall + "    origin b outlives a\n" + PutMove)]
    [InlineData(CovariantCall + PutMove, "value@move: b > a")]
    [InlineData(CovariantCall + "    origin a outlives b\n" + PutMove, "value@move: b > a")]
    [InlineData(ContravariantCall + "    origin a outlives b\n    k(value@move)\n")]
    [InlineData(ContravariantCall + "    k(value@move)\n", "value@move: a > b")]
    [InlineData(ContravariantCall + "    origin b outlives a\n    k(value@move)\n", "value@move: a > b")]
    [InlineData("func put<T>(slot: uniq/T, value: T) => ()\nfunc run(slot: uniq/(C during a), value: C during b)\n    origin b outlives a\n" + PutMove, "value@move: a > b")]
    [InlineData(InvariantCall + "    origin a == b\n    tie(slot, other)\n")]
    [InlineData(InvariantCall + "    tie(slot, other)\n", "other: b == a")]
    [InlineData(InvariantCall + "    origin b outlives a\n    tie(slot, other)\n", "other: a > b")]
    [InlineData(InvariantCall + "    origin a outlives b\n    tie(slot, other)\n", "other: b > a")]
    [InlineData(CovariantFit + "    origin b outlives a\n" + ReturnMove)]
    [InlineData(CovariantFit + ReturnMove, "value@move: b > a")]
    [InlineData(CovariantFit + "    origin a outlives b\n" + ReturnMove, "value@move: b > a")]
    [InlineData("func run(value: Option<ref/i32 during b>, anchor: ref/i32 during a) -> i32\n    let k: Option<ref/i32 during a> = value@move\n    return 0\n", "value@move: b > a")]
    [InlineData(ContravariantFit + "    origin a outlives b\n" + ReturnMove)]
    [InlineData(ContravariantFit + ReturnMove, "value@move: a > b")]
    [InlineData(ContravariantFit + "    origin b outlives a\n" + ReturnMove, "value@move: a > b")]
    [InlineData("func run(value: Option<C during b>) -> Option<C during a>\n" + ReturnMove, "value@move: a > b")]
    [InlineData(InvariantFit + "    origin a == b\n" + ReturnMove)]
    [InlineData(InvariantFit + ReturnMove, "value@move: b == a")]
    [InlineData(InvariantFit + "    origin b outlives a\n" + ReturnMove, "value@move: a > b")]
    [InlineData(Link + "    origin u outlives w\n" + LinkReturnAndCaller + "    origin a outlives b\n    link(p, q)\n")]
    [InlineData(Link + "    origin u outlives w\n" + LinkReturnAndCaller + "    link(p, q)\n", "p: a > b")]
    [InlineData(Link + "    origin u outlives w\n" + LinkReturnAndCaller + "    origin b outlives a\n    link(p, q)\n", "p: a > b")]
    [InlineData(Link + "    origin u == w\n" + LinkReturnAndCaller + "    origin a outlives b\n    link(p, q)\n", "q: b > a")]
    [InlineData(Link + "    origin w outlives u\n" + LinkReturnAndCaller + "    link(p, q)\n", "q: b > a")]
    [InlineData("func run<m/T>(value: m/(ref/i32 during b) during c) -> m/(ref/i32 during a) during c\n    m is owner or uniq\n    origin a outlives b\n" + ReturnMove, "value@move: b > a")]
    [InlineData("struct Box<E>\n    public let item: E\nfunc pick<m/T>(b: ref/Box<m/T during a> during c, x: ref/i32 during a) -> ref/i32 during c\n" +
        "    m is ref\n    return x\n")]
    public void AnOriginPositionRelatesByItsVariance(string source, params string[] expected)
    {
        var c = MirBuildTest.Compile(Holder + Callback + source + Main);
        Assert.Equal([.. expected.Select(static _ => nameof(DiagnosticCode.UnprovenOriginRelation_Kd))], MirTargetSide.MirSide(c).Select(static x => x.Code.ToString()));
        Assert.Equal(expected, Unknowns(c));
    }
}
