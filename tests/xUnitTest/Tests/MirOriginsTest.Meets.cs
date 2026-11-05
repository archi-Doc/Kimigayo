// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// A5's meet law at a shorter end (SPEC 15.3.6; U3-design.md's twelfth amendment) and the nested-body limit, on the view's rows: each
// operand has one bound here, so each in-edge takes one choice and its record is that edge's.
public sealed partial class MirOriginsTest
{
    // The caller's parameters of the meet-law family (U3-design.md's `gen-meets.py`): three free Origins, static, and a forcing reference
    // to each.
    internal const string MeetParams = "r: ref/i32, q: ref/i32, e: ref/i32, s: ref/i32 during static, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q), ue: uniq/(ref/i32 during e), us: uniq/(ref/i32 during static), ur2: uniq/(ref/i32 during r), uq2: uniq/(ref/i32 during q), ue2: uniq/(ref/i32 during e)";

    // `x: (a and d)` with two call regions bounded by distinct ends r1 and q1 (C-49's B1).
    internal const string DistinctMk = "func mk(p: Option<ref/(ref/i32 during a) during r1>, w: Option<ref/(ref/i32 during d) during q1>, x: ref/i32 during (a and d), y: uniq/(ref/i32 during r1), z: uniq/(ref/i32 during q1)) -> i32 => 0\n";

    // `x: (a and d)` with a call region `a` bounded by r1 (a forcing reference) and `d` taken from a parameter: one literal Fixed operand.
    private const string LiteralMk = "func mk(p: Option<ref/(ref/i32 during a) during r1>, y: uniq/(ref/i32 during r1), w: ref/i32 during d, x: ref/i32 during (a and d)) -> i32 => 0\n";

    // A callee whose Closure argument's input and result lie during the call region `a`, which is Fixed in the Closure's body.
    private const string RegionMk = "func mk(p: Option<ref/(ref/i32 during a) during b>, y: ref/i32 during b, f: (ref/i32 during a) -> ref/i32 during a) -> i32 => 0\n";

    // SPEC 15.3.6: `u outlives (o1 and ... and on)` is judged as `u outlives (g1 and ... and gn)` for each choice of one bound gi per
    // operand, at the in-edge's site: a literal Fixed operand (q) beside a call region bounded by r; distinct ends (r and q), Refuted for
    // a local's Borrow and Unknown for a third parameter (C-49's mx_bad, mx_fix); a nested meet (the bound of a call region bounded by
    // a meet, the record at p's row); a cycle of two meets through two locals; two in-edges into one meet, each judged; and an operand
    // whose only edge is its axiom edge into its own meet, which is never walked, so it has no bound and a finite source is valid.
    [Theory]
    [InlineData(LiteralMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r)) -> i32\n    let short = 2\n    return mk(.None, ur, q, short@ref)\n", "UnsatisfiedOriginRelation_Kd short@ref")]
    [InlineData(LiteralMk + "func k(r: ref/i32, q: ref/i32, e: ref/i32, ur: uniq/(ref/i32 during r)) -> i32\n    return mk(.None, ur, q, e)\n", "UnprovenOriginRelation_Kd e")]
    [InlineData(LiteralMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r)) -> i32\n    let short = 2\n    return mk(.None, ur, q, r)\n")]
    [InlineData(DistinctMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    return mk(.None, .None, short@ref, ur, uq)\n", "UnsatisfiedOriginRelation_Kd short@ref")]
    [InlineData(DistinctMk + "func k(r: ref/i32, q: ref/i32, e: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    return mk(.None, .None, e, ur, uq)\n", "UnprovenOriginRelation_Kd e")]
    [InlineData(DistinctMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    return mk(.None, .None, r, ur, uq)\n")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during (c and h)>, w1: Option<ref/(ref/i32 during c) during f>, y1: uniq/(ref/i32 during f), w2: Option<ref/(ref/i32 during h) during g>, y2: uniq/(ref/i32 during g), x: ref/i32 during a) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, .None, ur, .None, uq, short@ref)\n", "UnsatisfiedOriginRelation_Kd .None")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during (c and h)>, w1: Option<ref/(ref/i32 during c) during f>, y1: uniq/(ref/i32 during f), w2: Option<ref/(ref/i32 during h) during g>, y2: uniq/(ref/i32 during g), x: ref/i32 during a) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, .None, ur, .None, uq, r)\n")]
    [InlineData("func mka(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc mkb(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), p2: Option<ref/(ref/i32 during a) during h>, y2: uniq/(ref/i32 during h), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    var v1 = r\n    var v2 = q\n    let t1 = mka(.None, v2@uniq, .None, ur, v1)\n    let t2 = mkb(.None, v1@uniq, .None, ue, .None, uq, v2)\n    return t1 + t2\n")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d), x2: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, r, e)\n", "UnprovenOriginRelation_Kd e")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d), x2: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, r, r)\n")]
    [InlineData("func mk(w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, uq, short@ref)\n")]
    public void AMeetAtAShorterEndOutlivesOneBoundPerOperand(string source, params string[] problems)
        => Assert.Equal(problems, Origins(MirBuildTest.Compile(source + Main)));

    // The nested-body limit (U3-design.md's N4): an enclosing body's call region is Fixed in a Closure's body, where only the enclosing
    // body's rows decide it, so a failing chain with such an end is the located Unsupported_Kd at the chain's site: Refuted at a target
    // region (a literal's Borrow through pick into the Closure's result), Unknown at a target region (a captured parameter returned) and
    // Unknown at a source region (hole9_ok's `a outlives static`, valid, the owner's (i)).
    [Theory]
    [InlineData(RegionMk + "func pick(x: ref/i32 during s, y: ref/i32 during s) -> ref/i32 during s => x\nfunc k(t: ref/i32, u: ref/i32) -> i32\n    return mk(.None, t, func [] (v) => pick(v, 7@ref))\n", "Unsupported_Kd pick(v, 7@ref)")]
    [InlineData(RegionMk + "func k(t: ref/i32, u: ref/i32) -> i32\n    return mk(.None, t, func [u] (v) => u)\n", "Unsupported_Kd u")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    let sr = short@ref\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, s, s, s, func [] (t: ref/i32) => 0, func [s] (v, c2) => mk(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (t: ref/i32 during static) => 0, func [] (v2, c3) => 0))\n", "Unsupported_Kd v")]
    public void AFailingChainAtAnEnclosingCallRegionIsTheLocatedLimit(string source, string problem)
        => Assert.Equal([problem], Origins(MirBuildTest.Compile(source + Main)));
}
