// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// G216 (a) U3a (U3-design.md's twelfth amendment): a callee Origin without a candidate is a call region (SPEC 15.6.4 step 1), A5 judges
// a meet at a shorter end by SPEC 15.3.6's meet law, and a failing chain at an enclosing call region is the located limit. Programs from
// the lane's oracle sets (`artifacts/verify/g216a-oracle/`), each with one record per in-edge, as `Code@line:col`.
public partial class OriginRelationDiagnosticTest
{
    // The meet-law family (`progs-meets/`, its oracle a brute-force model over sets): a valid and an invalid program per case, distinct
    // ends, a forced operand, an unbounded operand (valid for every source), nested meets, three operands, two edges into one meet and a
    // cycle of two meets.
    [Theory]
    [InlineData("dist_named_uruq_r", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, r)\n", "")]
    [InlineData("dist_named_uruq_short", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, short@ref)\n", "UnsatisfiedOriginRelation@4:37")]
    [InlineData("fixop_uruq_q", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), z: uniq/(ref/i32 during d), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, uq, q)\n", "")]
    [InlineData("fixop_uruq_e", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), z: uniq/(ref/i32 during d), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, uq, e)\n", "UnprovenOriginRelation@4:30")]
    [InlineData("free_short", "func mk(w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, uq, short@ref)\n", "")]
    [InlineData("nest_uque_r", "func mk(p: Option<ref/(ref/i32 during a) during (c and h)>, w1: Option<ref/(ref/i32 during c) during f>, y1: uniq/(ref/i32 during f), w2: Option<ref/(ref/i32 during h) during g>, y2: uniq/(ref/i32 during g), w3: Option<ref/(ref/i32 during d) during j>, y3: uniq/(ref/i32 during j), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, .None, ur, .None, uq, .None, ue, r)\n", "")]
    [InlineData("nest_uqur2_e", "func mk(p: Option<ref/(ref/i32 during a) during (c and h)>, w1: Option<ref/(ref/i32 during c) during f>, y1: uniq/(ref/i32 during f), w2: Option<ref/(ref/i32 during h) during g>, y2: uniq/(ref/i32 during g), w3: Option<ref/(ref/i32 during d) during j>, y3: uniq/(ref/i32 during j), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, .None, ur, .None, uq, .None, ur2, e)\n", "UnprovenOriginRelation@4:56")]
    [InlineData("three_ue_r", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), v: Option<ref/(ref/i32 during h) during j>, t: uniq/(ref/i32 during j), x: ref/i32 during (a and d and h)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, .None, ue, r)\n", "")]
    [InlineData("three_ue_short", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), v: Option<ref/(ref/i32 during h) during j>, t: uniq/(ref/i32 during j), x: ref/i32 during (a and d and h)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, .None, ue, short@ref)\n", "UnsatisfiedOriginRelation@4:48")]
    [InlineData("twoin_r_r", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d), x2: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, r, r)\n", "")]
    [InlineData("twoin_r_e", "func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d), x2: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, r, e)\n", "UnprovenOriginRelation@4:40")]
    [InlineData("cyc_r_q", "func mka(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc mkb(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), p2: Option<ref/(ref/i32 during a) during h>, y2: uniq/(ref/i32 during h), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    var v1 = r\n    var v2 = q\n    let t1 = mka(.None, v2@uniq, .None, ur, v1)\n    let t2 = mkb(.None, v1@uniq, .None, ue, .None, uq, v2)\n    return t1 + t2\n", "")]
    [InlineData("cyc_short_e", "func mka(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc mkb(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), p2: Option<ref/(ref/i32 during a) during h>, y2: uniq/(ref/i32 during h), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MirOriginsTest.MeetParams + ") -> i32\n    let short = 2\n    var v1 = short@ref\n    var v2 = e\n    let t1 = mka(.None, v2@uniq, .None, ur, v1)\n    let t2 = mkb(.None, v1@uniq, .None, ue, .None, uq, v2)\n    return t1 + t2\n", "UnsatisfiedOriginRelation@7:45")]
    public void AMeetLawProgramHasItsRecord(string form, string body, string records)
        => AssertPositions(form, body, records);

    // C-49's probes (`probe-c49/`, B1): `x: (a and d)` with operands bounded by distinct ends is Refuted for a local's Borrow (also through
    // a join and with static ends) and Unknown for a third parameter; with r it is valid.
    [Theory]
    [InlineData("mx_bad", MirOriginsTest.DistinctMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    return mk(.None, .None, short@ref, ur, uq)\n", "UnsatisfiedOriginRelation@4:29")]
    [InlineData("mx_fix", MirOriginsTest.DistinctMk + "func k(r: ref/i32, q: ref/i32, e: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    return mk(.None, .None, e, ur, uq)\n", "UnprovenOriginRelation@3:29")]
    [InlineData("mx_jroot", MirOriginsTest.DistinctMk + "func k(c: bool, r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    return mk(if c => .None else => .None, .None, short@ref, ur, uq)\n", "UnsatisfiedOriginRelation@4:51")]
    [InlineData("ms_bad", "func mk(p: Option<ref/(ref/i32 during a) during static>, w: Option<ref/(ref/i32 during d) during static>, x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(r: ref/i32) -> i32\n    let short = 2\n    return mk(.None, .None, short@ref)\n", "UnsatisfiedOriginRelation@4:29")]
    [InlineData("mx_ok", MirOriginsTest.DistinctMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    return mk(.None, .None, r, ur, uq)\n", "")]
    public void ADistinctEndsMeetHasItsRecord(string form, string body, string records)
        => AssertPositions(form, body, records);

    // The nested-body limit (`probe-u3a-r/`'s hole family): a Closure's call requires a relation of an enclosing call region, which only
    // the enclosing body's rows decide, so each is the located Unsupported_Kd at the chain's site; hole6_ok and hole9_ok are valid, the
    // owner's (i).
    [Theory]
    [InlineData("hole6_ok", "func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, s, s, s, func [s] (v, c2) => mk(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (v2, c3) => 0))\n", "Unsupported@4:230")]
    [InlineData("hole9_ok", "func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    let sr = short@ref\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, s, s, s, func [] (t: ref/i32) => 0, func [s] (v, c2) => mk(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (t: ref/i32 during static) => 0, func [] (v2, c3) => 0))\n", "Unsupported@5:257")]
    [InlineData("hole9", "func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    let sr = short@ref\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, sr, sr, s, func [] (t: ref/i32) => 0, func [s] (v, c2) => mk(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (t: ref/i32 during static) => 0, func [] (v2, c3) => 0))\n", "Unsupported@5:259")]
    [InlineData("hole9_mk2", "func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc mk2(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    let sr = short@ref\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, sr, sr, s, func [] (t: ref/i32) => 0, func [s] (v, c2) => mk2(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (t: ref/i32 during static) => 0, func [] (v2, c3) => 0))\n", "Unsupported@6:260")]
    [InlineData("hole9_tuple", "func mk(p: (Option<ref/(ref/i32 during a) during b>, Option<ref/(ref/i32 during e) during a>, Option<ref/(ref/i32 during d) during b>), y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    let sr = short@ref\n    return mk((.None, .None, .None), sr, sr, s, func [] (t: ref/i32) => 0, func [s] (v, c2) => mk((.None, .None, .None), s, s, v, func [] (t: ref/i32 during static) => 0, func [] (v2, c3) => 0))\n", "Unsupported@5:128")]
    public void AnEnclosingCallRegionsChainIsTheLocatedLimit(string form, string body, string records)
        => AssertPositions(form, body, records);

    // The error records as `Code@line:col` (the code without `_Kd`; one-based), in order.
    private static void AssertPositions(string form, string body, string records)
    {
        var source = body + Main;
        var shown = string.Join(' ', DiagnosticCorpus.Check(source).Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).Select(x =>
        {
            var start = x.Span is { } span ? span.Start : 0;
            var line = source.AsSpan(0, start).Count('\n') + 1;
            return $"{x.Code[..^3]}@{line}:{start - source.LastIndexOf('\n', start - 1)}";
        }));
        Assert.True(shown == records, $"{form}: expected [{records}], found [{shown}]");
    }
}
