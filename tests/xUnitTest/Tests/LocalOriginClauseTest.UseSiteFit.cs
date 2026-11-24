// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.6.1, 15.3.5, 10.7, 15.6.5, 23.1.2 (PLAN G492-a (i)): the records of a use-site fit, one row per form and boundary of the
// fit's walk of the Type relation. The fit walks the relation once and reads each part's answer from that walk; an invariant part that
// the relation meets reversed is walked once more in the fit's own orientation. Every row's records are those of a walk rooted at each
// part, which the fit made before.
// r4aL and w4L4 (valid) are accepted since the solve holds its representatives and runs its fixed point again (G1128 R1.3 (i)).
// A Function Type at an invariant position relates both ways (PLAN G900): rq1w, rq2a, rq2av and n1f are rejected as labelled.
public partial class LocalOriginClauseTest
{
    private const string Reader2 = "struct R2<T, U>\n    public let call: (T, U) -> i32\n    public init(call: (T, U) -> i32) => self.call = call@move\n\n";

    // A Function Item whose per-call Origins `a`, `b` are only a meet's operands (SPEC 15.3.6: a meet on the right is never split).
    private const string MeetOnly = "func m1(w: ref/i64 during (a and b)) -> i32\n    return 0\n";

    // MeetOnly's operands related by clauses to `c`, which is at an input contravariant in its slot (SPEC 15.3.5) and a covariant input.
    private const string MeetToInvariant = "struct Sink {a}\n    public let g: (ref/i64 during a) -> i32\nfunc m3(w: ref/i64 during (a and b), s: Sink during c, v: ref/i64 during c) -> i32\n    origin a outlives c\n    origin b outlives c\n    return 0\n";

    // Each row: its name, the program without `main`, and its records in order as `code@line:column label`, joined by `|`.
    public static TheoryData<string, string, string> UseSiteFitRows => new()
    {
        // The walk's own parts: a `var` local's regions (SPEC 15.6.5), a shape stop after or before a failing position (15.6.1), an inferred
        // length (4.3.1), a resting position (23.1.2) and an `==` clause over two exclusive inputs (10.7).
        // valid: the region of `r` is bounded by `p`.
        { "c1", "func f(p: ref/i64) -> ref/i64 during p\n    var r = p\n    return r\n", string.Empty },
        // invalid: `q outlives p` is not proven.
        { "c1n", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var r = p\n    r = q\n    return r\n", "UnprovenOriginRelation_Kd@4:12 requires q outlives p, which is not proven" },
        // valid: two parts of one Tuple; the returned part holds `p` only.
        { "c2", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var t = (p, p)\n    t.0 = q\n    return t.1\n", string.Empty },
        // invalid: the returned part holds `q`.
        { "c2n", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var t = (p, p)\n    t.0 = q\n    return t.0\n", "UnprovenOriginRelation_Kd@4:12 requires q outlives p, which is not proven" },
        // invalid: a Type mismatch after a failing Origin position; the Origin relation beside it is dev's record selection.
        { "c3n", "func f(p: ref/i64, q: ref/i64, b: bool) -> (ref/i64 during p, i32)\n    var t = (q, b)\n    return t\n", "TypeMismatch_Kd@3:12 expected (ref/i64, i32), found (ref/i64, bool)|UnprovenOriginRelation_Kd@3:12 requires q outlives p, which is not proven" },
        // invalid: the Type mismatch first.
        { "c3bn", "func f(p: ref/i64, q: ref/i64, b: bool) -> (i32, ref/i64 during p)\n    var t = (b, q)\n    return t\n", "TypeMismatch_Kd@3:12 expected (i32, ref/i64), found (bool, ref/i64)" },
        // invalid: the Type mismatch one level down (as c3n).
        { "c3cn", "func f(p: ref/i64, q: ref/i64, b: bool) -> ((ref/i64 during p, i32), ref/i64 during p)\n    var t = ((q, b), q)\n    return t\n", "TypeMismatch_Kd@3:12 expected ((ref/i64, i32), ref/i64), found ((ref/i64, bool), ref/i64)|UnprovenOriginRelation_Kd@3:12 requires q outlives p, which is not proven" },
        // valid: an exclusive borrow of a region local.
        { "c4", "func f(p: ref/i64) -> i64\n    var r = p\n    var s = r\n    let u = s@uniq\n    return 0\n", string.Empty },
        // valid: an inferred length at a fitted part.
        { "c5", "func f(p: ref/i64) -> ref/i64 during p\n    var a: [_ of ref/i64] = [p, p]\n    return a[0]\n", string.Empty },
        // invalid: `q outlives p` is not proven under the inferred length.
        { "c5n", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var a: [_ of ref/i64] = [p, q]\n    return a[0]\n", "UnprovenOriginRelation_Kd@3:12 requires q outlives p, which is not proven" },
        // invalid: the unknown Origin is the one record; the fits resting on it add none.
        { "c6n", "func f(p: ref/i64) -> ref/i64 during p\n    var r: ref/i64 during zz = p\n    return r\n", "MissingOriginBinding_Kd@2:27" },
        // invalid: an earlier sibling's obligation bounds the region a later sibling reads; `q outlives p` is not proven.
        { "e4a", "func f(p: ref/i64, q: ref/i64) -> (ref/i64 during p, ref/i64 during p)\n    var t = (p, p)\n    t = (q, t.0)\n    return t\n", "UnprovenOriginRelation_Kd@4:12 requires q outlives p, which is not proven" },
        // valid: the clause `a == c` holds for `x`'s Origin `p`.
        { "veqi", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> i32\n    let x = p\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqi\n    return 0\n", string.Empty },
        // invalid: `q == p` is not proven. PLAN G913's Revision 2 names the first failing bound, `p outlives q` (the owner's O-348 (a));
        // with PLAN G492-a (iii)'s comparison the record is the equality, `p == q` on (iii) alone, and in the joint landing with (ii),
        // where x's slot is its region in the comparison (O-363 (a)), named as held: the Middle's text (board C-350, 11-17 22:35).
        { "veqin", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    let x = q\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqi\n    return 0\n", "UnprovenOriginContract_Kd@6:72 the 2nd parameter requires p == the omitted Origin of x, which is not proven" },

        // An invariant part the relation meets reversed (`uniq/X` in R's contravariant slot, SPEC 15.3.5): its facts are those of its own
        // walk in the fit's orientation, a Function Type's one oriented comparison included (10.7).
        // invalid: `a == b` is not proven, and the declared Type needs `b outlives q` (15.6.1).
        { "e3p", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(ref/i32 during a) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/(ref/i32 during b) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@7:50 requires a == b, which is not proven" },
        // invalid: the Function Types must be equal (`b == a` in the fit's orientation), and `b outlives q`.
        { "e3f", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@7:59 the 1st parameter requires b == a, which is not proven" },
        // invalid: with `a == b` the declared Type still needs `b outlives q`.
        { "e3fv", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> i32\n    origin q outlives p\n    origin a == b\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires a outlives q, which is not proven" },
        // valid: `a == b` and `b outlives q`.
        { "e3fw", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> i32\n    origin q outlives p\n    origin a == b\n    origin b outlives q\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", string.Empty },
        // invalid: e3f's comparison through a returned value (the relation's orientation, `a == b`).
        // F-H-11 (R7-O1 H): the contract record prints the use-site fit's orientation, as that row states it.
        { "e3fr", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> R<uniq/((ref/i32 during b) -> i32) during q>\n    origin q outlives p\n    return r0@move\n", "UnprovenOriginRelation_Kd@5:111 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@7:12 the 1st parameter requires b == a, which is not proven" },
        // invalid: a per-call input against a fixed one: the Function Types do not relate both ways (PLAN G900), and the declared
        // Type needs `b outlives q`.
        { "rq1", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@7:59 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven" },
        // invalid: rq1 with `b outlives q`; the Function Types' inequality is the one failure.
        { "rq1w", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginContract_Kd@8:59 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven" },
        // invalid three times: rq1 without `q outlives p`.
        { "rq1n", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@6:14 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@6:59 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven|UnprovenOriginRelation_Kd@6:59 requires q outlives p, which is not proven" },
        // invalid: rq1 through a `var` local.
        { "rq1v", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r1@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@8:59 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven" },
        // invalid: rq1's pair at an argument.
        { "rq1a", Reader + "func g(b: ref/i32, q: ref/i32, r: R<uniq/((ref/i32 during b) -> i32) during q>) -> i32\n    origin b outlives q\n    return 0\nfunc f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    return g(b, q, r0@move)\n", "UnprovenOriginContract_Kd@11:20 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq1a through a `var` local.
        { "rq1av", Reader + "func g(b: ref/i32, q: ref/i32, r: R<uniq/((ref/i32 during b) -> i32) during q>) -> i32\n    origin b outlives q\n    return 0\nfunc f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    var r1 = r0@move\n    return g(b, q, r1@move)\n", "UnprovenOriginContract_Kd@12:20 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq1's pair through a returned value.
        { "rq1r", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> R<uniq/((ref/i32 during b) -> i32) during q>\n    origin q outlives p\n    return r0@move\n", "UnprovenOriginRelation_Kd@5:90 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@7:12 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven" },
        // invalid: rq1 mirrored (the declared Function Type per-call): the Function Types' inequality; the declared Type relates no
        // per-call Origin to `q` (SPEC 15.2.3).
        { "rq2", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/((ref/i32) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginContract_Kd@7:50 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid twice: rq2 without `q outlives p`.
        { "rq2n", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    let r: R<uniq/((ref/i32) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginContract_Kd@6:50 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven|UnprovenOriginRelation_Kd@6:50 requires q outlives p, which is not proven" },
        // invalid: rq2 through a `var` local.
        { "rq2v", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    let r: R<uniq/((ref/i32) -> i32) during q> = r1@move\n    return 0\n", "UnprovenOriginContract_Kd@8:50 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq2's pair through a returned value.
        { "rq2r", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> R<uniq/((ref/i32) -> i32) during q>\n    origin q outlives p\n    return r0@move\n", "UnprovenOriginContract_Kd@7:12 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // valid: rq2r's twin without `b`: the result relates no per-call Origin to `q` (SPEC 15.2.3), and both `uniq/` targets are per-call.
        { "f_rq2r_ok", Reader + "func f(q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during q>) -> R<uniq/((ref/i32) -> i32) during q>\n    return r0@move\n", string.Empty },
        // invalid: rq2's pair at an argument: the argument's `uniq/` target and the parameter's do not relate both ways.
        { "rq2a", Reader + "func g(q: ref/i32, r: R<uniq/((ref/i32) -> i32) during q>) -> i32 => 0\nfunc f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    origin q outlives p\n    return g(q, r0@move)\n", "UnprovenOriginContract_Kd@8:17 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven" },
        // invalid: rq2a through a `var` local.
        { "rq2av", Reader + "func g(q: ref/i32, r: R<uniq/((ref/i32) -> i32) during q>) -> i32 => 0\nfunc f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    return g(q, r1@move)\n", "UnprovenOriginContract_Kd@9:17 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven" },

        // A held Function Type's part of a declared Type's well-formedness is its call signature with its own per-call Origins closed
        // (SPEC 15.2.3, 15.6.1, 15.3.4; DECISIONS BU-73): its fixed Origins outlive the layer, its per-call Origins never leave their binder.
        // valid: OwnedOrigins of `(ref/i32) -> i32` is empty.
        { "f_loc_ref_ok", "func f(g: (ref/i32) -> i32) -> i32\n    let r: ref/((ref/i32) -> i32) = g@ref\n    return 0\n", string.Empty },
        // valid: the result's omitted Origin is the input's (SPEC 15.4.3 rule 1), of the same binder.
        { "f_loc_res_ok", "func f(g: (ref/i32) -> ref/i32) -> i32\n    let r: ref/((ref/i32) -> ref/i32) = g@ref\n    return 0\n", string.Empty },
        // valid: two binders, a Function Type held in another's input; neither's per-call Origin escapes.
        { "f_nest_ok", "func f(g: (uniq/((ref/i32) -> i32)) -> i32) -> i32\n    let r: ref/((uniq/((ref/i32) -> i32)) -> i32) = g@ref\n    return 0\n", string.Empty },
        // valid: `b` is a fixed Origin the Function Type writes, and u0's premise gives `b outlives q`.
        { "f_prem_ok", "func f(b: ref/i32, q: ref/i32, u0: ref/((ref/i32 during b) -> i32) during q) -> i32\n    let u: ref/((ref/i32 during b) -> i32) during q = u0\n    return 0\n", string.Empty },
        // invalid twice: the fit needs `u0 outlives q`, and the declared Type's well-formedness `b outlives q` (the fixed Origin's row stays).
        { "f_wf_bad", "func f(b: ref/i32, q: ref/i32, u0: ref/((ref/i32 during b) -> i32)) -> i32\n    let u: ref/((ref/i32 during b) -> i32) during q = u0\n    return 0\n", "UnprovenOriginRelation_Kd@2:12 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@2:55 requires u0 outlives q, which is not proven" },

        // The boundaries of that part's own walk: a contravariant slot again inside it (n1), two such parts in one fit and at an argument
        // (n3), a resting position under it (n5) and a pair layer's outer Origin under it (n6, SPEC 8.1.2). The `v` rows lack the
        // declared Type's `b outlives q` (15.6.1); the `w` rows add it.
        // invalid: `a == b` is not proven.
        { "n1", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/R<ref/i32 during b> during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@7:51 requires a == b, which is not proven" },
        // invalid: n1 through a `var` local.
        { "n1var", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    let r: R<uniq/R<ref/i32 during b> during q> = r1@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives a, which is not proven|UnprovenOriginRelation_Kd@8:14 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@8:51 requires a outlives b, which is not proven" },
        // invalid: the nested R's argument a per-call against a fixed Function Type, with `b outlives q`: the Function Types must be equal.
        { "n1f", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<(ref/i32) -> i32> during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    let r: R<uniq/R<(ref/i32 during b) -> i32> during q> = r0@move\n    return 0\n", "UnprovenOriginContract_Kd@8:60 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven" },
        // invalid: a nested per-call Function Type on the declared side: the Function Types' inequality; the declared Type relates no
        // per-call Origin to `q` (SPEC 15.2.3).
        { "n1fm", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<(ref/i32 during b) -> i32> during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/R<(ref/i32) -> i32> during q> = r0@move\n    return 0\n", "UnprovenOriginContract_Kd@7:51 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: with `a == b` the declared Type still needs `b outlives q`.
        { "n1v", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    origin a == b\n    let r: R<uniq/R<ref/i32 during b> during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires a outlives q, which is not proven" },
        // valid: `a == b` and `b outlives q`.
        { "n1w", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    origin a == b\n    origin b outlives q\n    let r: R<uniq/R<ref/i32 during b> during q> = r0@move\n    return 0\n", string.Empty },
        // invalid: `a == b` and `c == d` are not proven, nor the declared Type's `b outlives q` and `d outlives q`.
        { "n3", Reader2 + "func f(a: ref/i32, b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during a) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    let r: R2<uniq/(ref/i32 during b) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:15 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@7:49 requires d outlives q, which is not proven|UnprovenOriginRelation_Kd@7:85 requires a == b, which is not proven|UnprovenOriginRelation_Kd@7:85 requires c == d, which is not proven" },
        // invalid: n3's pair at an argument through a `var` local.
        { "n3a", Reader2 + "func g(d: ref/i32, q: ref/i32, b: ref/i32, r: R2<uniq/(ref/i32 during b) during q, uniq/(ref/i32 during d) during q>) -> i32\n    origin b outlives q\n    origin d outlives q\n    return 0\nfunc f(a: ref/i32, b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during a) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    origin d outlives q\n    var r1 = r0@move\n    return g(d, q, b, r1@move)\n", "UnprovenOriginRelation_Kd@13:14 requires b outlives a, which is not proven|UnprovenOriginRelation_Kd@13:14 requires d outlives c, which is not proven|UnprovenOriginRelation_Kd@14:14 requires c outlives q, which is not proven|UnprovenOriginRelation_Kd@14:20 requires a outlives q, which is not proven" },
        // invalid: the first slot's Function Types differ, the second needs `c == d`.
        { "n3f", Reader2 + "func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/((ref/i32) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    let r: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:58 requires d outlives q, which is not proven|UnprovenOriginContract_Kd@8:94 the 1st parameter requires the omitted Origin of ref/i32 outlives b, which is not proven|UnprovenOriginRelation_Kd@8:94 requires c == d, which is not proven" },
        // invalid: with both equalities the declared Type still needs `b outlives q` and `d outlives q`.
        { "n3v", Reader2 + "func f(a: ref/i32, b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during a) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    origin a == b\n    origin c == d\n    let r: R2<uniq/(ref/i32 during b) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@9:15 requires a outlives q, which is not proven|UnprovenOriginRelation_Kd@9:49 requires c outlives q, which is not proven" },
        // valid: both equalities and both well-formedness premises.
        { "n3w", Reader2 + "func f(a: ref/i32, b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during a) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    origin a == b\n    origin c == d\n    origin b outlives q\n    origin d outlives q\n    let r: R2<uniq/(ref/i32 during b) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", string.Empty },
        // valid: n3w through a `var` local.
        { "n3wv", Reader2 + "func f(a: ref/i32, b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during a) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    origin a == b\n    origin c == d\n    origin b outlives q\n    origin d outlives q\n    var r1 = r0@move\n    let r: R2<uniq/(ref/i32 during b) during q, uniq/(ref/i32 during d) during q> = r1@move\n    return 0\n", string.Empty },
        // invalid: the unknown Origin is the one record.
        { "n5", Reader + "func f(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(ref/i32 during a) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/(ref/i32 during zz) during q> = r0@move\n    return 0\n", "MissingOriginBinding_Kd@7:35" },
        // invalid: n5 through a `var` local.
        { "n5v", Reader + "func f(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(ref/i32 during a) during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    let r: R<uniq/(ref/i32 during zz) during q> = r1@move\n    return 0\n", "MissingOriginBinding_Kd@8:35" },
        // invalid: a pair layer's slots, `a == b` is not proven.
        { "n6", Reader + "func f<s/T>(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    let r: R<uniq/(s/T during b) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@8:46 requires a == b, which is not proven" },
        // invalid: the original `s/T`'s implicit Origin against a written slot.
        { "n6o", Reader + "func f<s/T>(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    let r: R<uniq/(s/T during a) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires a outlives q, which is not proven|UnprovenOriginRelation_Kd@8:46 requires the omitted Origin of s/T == a, which is not proven" },
        // invalid: n6o mirrored.
        { "n6or", Reader + "func f<s/T>(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    let r: R<uniq/(s/T) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires the omitted Origin of s/T outlives q, which is not proven|UnprovenOriginRelation_Kd@8:37 requires a == the omitted Origin of s/T, which is not proven" },
        // invalid: n6o with `s is owner or ref`.
        { "n6ou", Reader + "func f<s/T>(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T) during p>) -> i32\n    s is owner or ref\n    T is Copy\n    origin q outlives p\n    let r: R<uniq/(s/T during a) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@9:14 requires a outlives q, which is not proven|UnprovenOriginRelation_Kd@9:46 requires the omitted Origin of s/T == a, which is not proven" },
        // invalid: with `a == b` the declared Type still needs `b outlives q`.
        { "n6v", Reader + "func f<s/T>(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    origin a == b\n    let r: R<uniq/(s/T during b) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@9:14 requires a outlives q, which is not proven" },
        // valid: `a == b` and `b outlives q`.
        { "n6w", Reader + "func f<s/T>(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    origin a == b\n    origin b outlives q\n    let r: R<uniq/(s/T during b) during q> = r0@move\n    return 0\n", string.Empty },

        // The decomposition's precondition (a reversed part's structure does not depend on the orientation) and its stop at a structural
        // end: an inferred length over a reversed part (x1, SPEC 4.3.1), a pair-layer input in a reversed Function part in both
        // orientations (x4, x5; 8.1.2, 10.7), the same forms with a later sibling (y4, y6o), and a Type mismatch after a failed Function
        // part (w2).
        // invalid: `a == b` is not proven, and the declared element Type needs `b outlives q` (as e3p).
        { "x1", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(ref/i32 during a) during p>) -> i32\n    origin q outlives p\n    let r: [_ of R<uniq/(ref/i32 during b) during q>] = [r0@move]\n    return 0\n", "UnprovenOriginRelation_Kd@7:20 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@7:57 requires a == b, which is not proven" },
        // invalid: the Function Types do not relate both ways: the argument's input `s/T` is per call, the declared one at `b`.
        { "x4", Reader + "func f<s/T>(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((s/T) -> i32) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    origin b outlives q\n    let r: R<uniq/((s/T during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginContract_Kd@9:55 the 1st parameter requires b == the omitted Origin of s/T, which is not proven" },
        // invalid: x4 mirrored under `s is owner or ref` (the argument's input at `a`), with the declared Type's record for the omitted
        // Origin of `s/T` (as n6or).
        { "x5", Reader + "func f<s/T>(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((s/T during a) -> i32) during p>) -> i32\n    s is owner or ref\n    origin q outlives p\n    origin a outlives q\n    let r: R<uniq/((s/T) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@9:14 requires the omitted Origin of s/T outlives q, which is not proven|UnprovenOriginContract_Kd@9:46 the 1st parameter requires the omitted Origin of s/T == a, which is not proven" },
        // invalid twice: x4's pair, and the later sibling's `c == d` is not proven.
        { "y4", Reader2 + "func f<s/T>(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/((s/T) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    origin b outlives q\n    origin d outlives q\n    let r: R2<uniq/((s/T during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", "UnprovenOriginContract_Kd@10:90 the 1st parameter requires b == the omitted Origin of s/T, which is not proven|UnprovenOriginRelation_Kd@10:90 requires c == d, which is not proven" },
        // invalid three times: n6o's pair, the later sibling's `c == d`, and the declared Type's `a outlives q`.
        { "y6o", Reader2 + "func f<s/T>(a: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(s/T) during p, uniq/(ref/i32 during c) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    origin d outlives q\n    let r: R2<uniq/(s/T during a) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@9:15 requires a outlives q, which is not proven|UnprovenOriginRelation_Kd@9:81 requires the omitted Origin of s/T == a, which is not proven|UnprovenOriginRelation_Kd@9:81 requires c == d, which is not proven" },
        // invalid: a failed Function part, then a nested part whose `c == d` comes before a Type mismatch, then `c == d`: the mismatch
        // ends the fit (PLAN G900); the nested `c == d` before it is kept.
        { "w2", "func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: (uniq/((ref/i32) -> i32) during p, (uniq/(ref/i32 during c) during p, uniq/i32 during p), uniq/(ref/i32 during c) during p)) -> i32\n    origin p outlives q\n    origin b outlives q\n    origin c outlives q\n    origin d outlives q\n    let r: (uniq/((ref/i32 during b) -> i32) during q, (uniq/(ref/i32 during d) during q, uniq/i64 during q), uniq/(ref/i32 during d) during q) = r0@move\n    return 0\n", "TypeMismatch_Kd@6:147 expected (uniq/(ref/i32) -> i32, (uniq/ref/i32, uniq/i64), uniq/ref/i32), found (uniq/(ref/i32) -> i32, (uniq/ref/i32, uniq/i32), uniq/ref/i32)|UnprovenOriginRelation_Kd@6:147 requires c == d, which is not proven" },

        // A Function Item compared with a Function Type whose solve leaves a per-call Origin with no bound that reached a fixed Origin
        // when the representatives were taken (SPEC 15.3.6, 15.3.7; PLAN G492-a (iii)): the located limit where the comparison's failure
        // is recorded (a `let`, a default argument), never an internal error.
        // invalid: no bound reaches `a` or `b`, so they have no candidate; the limit is the record (record selection).
        { "r3M", MeetOnly + "func outer(p: ref/i64) -> i32\n    let f: (ref/i64 during p) -> i32 = m1\n    return 0\n", "Unsupported_Kd@4:40" },
        // invalid: r3M as a default argument's value, checked at its declaration (SPEC 7.2.3).
        { "w4X4", MeetOnly + "func h(g: (ref/i64 during static) -> i32 = m1) -> i32\n    return 0\n", "Unsupported_Kd@3:44" },
        // valid: `c` is invariant without a proven equality, so it takes p, checked against q by the premise, and `a`, `b` take `c`: the
        // solve holds c's representative and runs its fixed point again, so `a`, `b` read it (G1128 R1.3 (i); the located limit before).
        { "r4aL", MeetToInvariant + "func outer(p: ref/i64, q: ref/i64) -> i32\n    origin p outlives q\n    let f: (ref/i64 during p, Sink during q, ref/i64 during p) -> i32 = m3\n    return 0\n", string.Empty },
        // invalid: `c` lies between p and q, and p outlives q is not proven: that member's record, once `a`, `b` read c's held value
        // (G1128 R1.3 (i); the located limit before, record selection).
        { "r4aLn", MeetToInvariant + "func outer(p: ref/i64, q: ref/i64) -> i32\n    let f: (ref/i64 during p, Sink during q, ref/i64 during p) -> i32 = m3\n    return 0\n", "UnprovenOriginContract_Kd@8:73 the 2nd parameter requires p outlives q, which is not proven" },
        // valid: r4aL at a call argument (p and q are fixed in `take3`, SPEC 15.3.4), accepted since its comparison's solve reaches no
        // located limit (G1128 R1.3 (i)); it was PLAN G1101's record, a discarded limit that made the candidate inapplicable.
        { "w4L4", MeetToInvariant + "func outer(p: ref/i64, q: ref/i64) -> i32\n    origin p outlives q\n    func take3(g: (ref/i64 during p, Sink during q, ref/i64 during p) -> i32) -> i32\n        return 0\n    return take3(m3)\n", string.Empty },
        // valid: w4L4 without the meet.
        { "w4L4c", "struct Sink {a}\n    public let g: (ref/i64 during a) -> i32\nfunc m3(w: ref/i64 during a, s: Sink during c, v: ref/i64 during c) -> i32\n    origin a outlives c\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin p outlives q\n    func take3(g: (ref/i64 during p, Sink during q, ref/i64 during p) -> i32) -> i32\n        return 0\n    return take3(m3)\n", string.Empty },

        // A Function Item or a Closure compared with a Function Type that names a `var` local's region (SPEC 15.3.6, 15.3.7, 15.6.5;
        // the owner's O-375 Q2 (a); PLAN G492-a (iii)): the comparison treats the region as an inference variable and the solve gives
        // the implementation's per-call Origin its candidate from G913's rows. Each is valid and accepted (dev rejected them).
        // Plain and Function-typed positions (covariant and contravariant bounds on one per-call Origin):
        { "r2A", "func cc(w: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (ref/i64 during p, (ref/i64 during x) -> i32) -> i32 = cc\n    return 0\n", string.Empty },
        { "r2Aq", "func cc(w: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin p outlives q\n    var x = q\n    let f: (ref/i64 during p, (ref/i64 during x) -> i32) -> i32 = cc\n    return 0\n", string.Empty },
        { "r2Bv", "func cc2u(w1: ref/i64 during a, w2: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, s: ref/i64) -> i32\n    origin s outlives p\n    var x = p\n    let f: (ref/i64 during x, ref/i64 during s, (ref/i64 during p) -> i32) -> i32 = cc2u\n    return 0\n", string.Empty },
        { "r2Bvr", "func cc2u(w1: ref/i64 during a, w2: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, s: ref/i64) -> i32\n    origin s outlives p\n    var x = p\n    let f: (ref/i64 during p, ref/i64 during s, (ref/i64 during x) -> i32) -> i32 = cc2u\n    return 0\n", string.Empty },
        { "r2G", "func cc2f(g1: (ref/i64 during a) -> i32, g2: (ref/i64 during a) -> i32, w: ref/i64 during a) -> i32\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin q outlives p\n    var x = q\n    let f: ((ref/i64 during x) -> i32, (ref/i64 during p) -> i32, ref/i64 during q) -> i32 = cc2f\n    return 0\n", string.Empty },
        { "r3Bx", "func fu4(w: ref/i64 during a, g: (ref/i64 during a) -> i32) -> (ref/i64 during a) -> i32\n    return g@move\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (ref/i64 during x, (ref/i64 during p) -> i32) -> (ref/i64 during x) -> i32 = fu4\n    return 0\n", string.Empty },
        { "rrA", "func m4(g: (ref/i64 during a) -> i32, w: ref/i64 during a, v: uniq/(ref/i64 during a)) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: ((ref/i64 during p) -> i32, ref/i64 during p, uniq/(ref/i64 during x)) -> i32 = m4\n    return 0\n", string.Empty },
        { "rrB", "func cr3(w: ref/i64 during a, g: (ref/i64 during a) -> i32) -> ref/i64 during a\n    return w\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (ref/i64 during x, (ref/i64 during x) -> i32) -> ref/i64 during p = cr3\n    return 0\n", string.Empty },
        { "rv3A", "func cc2u(w1: ref/i64 during a, w2: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, q: ref/i64, s: ref/i64) -> i32\n    origin p outlives q\n    origin s outlives q\n    var x = q\n    let f: (ref/i64 during p, ref/i64 during s, (ref/i64 during x) -> i32) -> i32 = cc2u\n    return 0\n", string.Empty },
        { "rv3Ae", "func cc2u(w1: ref/i64 during a, w2: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, q: ref/i64, s: ref/i64) -> i32\n    origin p outlives q\n    origin s outlives q\n    origin q outlives s\n    var x = q\n    let f: (ref/i64 during p, ref/i64 during s, (ref/i64 during x) -> i32) -> i32 = cc2u\n    return 0\n", string.Empty },
        { "rv3D", "func cc2u(w1: ref/i64 during a, w2: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, q: ref/i64, s: ref/i64) -> i32\n    origin q outlives p\n    origin s outlives p\n    origin p outlives s\n    var x = q\n    let f: (ref/i64 during x, ref/i64 during s, (ref/i64 during p) -> i32) -> i32 = cc2u\n    return 0\n", string.Empty },
        { "rv3Dn", "func cc2u(w1: ref/i64 during a, w2: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, q: ref/i64, s: ref/i64) -> i32\n    origin q outlives p\n    origin s outlives p\n    var x = q\n    let f: (ref/i64 during x, ref/i64 during s, (ref/i64 during p) -> i32) -> i32 = cc2u\n    return 0\n", string.Empty },
        { "rv3U2", "func k(v: ref/i64) -> i32\n    return 0\nfunc fu2(u: ref/i64 during c, g: (ref/i64 during c) -> i32, w: ref/i64 during a) -> (ref/i64 during a) -> i32\n    return k\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (ref/i64 during x, (ref/i64 during p) -> i32, ref/i64 during p) -> (ref/i64 during p) -> i32 = fu2\n    return 0\n", string.Empty },
        { "vcc3v", "func cc3(w: ref/i64 during a, g: (ref/i64 during a) -> i32, h: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (ref/i64 during x, (ref/i64 during x) -> i32, (ref/i64 during p) -> i32) -> i32 = cc3\n    return 0\n", string.Empty },
        { "vccv", "func cc(w: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (ref/i64 during x, (ref/i64 during p) -> i32) -> i32 = cc\n    return 0\n", string.Empty },
        { "vccvq", "func cc(w: ref/i64 during a, g: (ref/i64 during a) -> i32) -> i32\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin q outlives p\n    var x = q\n    let f: (ref/i64 during x, (ref/i64 during p) -> i32) -> i32 = cc\n    return 0\n", string.Empty },
        // Exclusive layers, invariant in their target (SPEC 15.3.5), among them the owner's veqiv and rrD (O-375 Q2 (a), O-391 (a));
        // vccv's program is also vccv2's.
        { "r2Fe", "func ch7(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during a), u: ref/i64 during c, g: (ref/i64 during c) -> i32) -> i32\n    origin c outlives a\n    return 0\nfunc outer(p: ref/i64, q: ref/i64, r: ref/i64) -> i32\n    origin q outlives p\n    origin p outlives q\n    origin p outlives r\n    var x = r\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during r), ref/i64 during q, (ref/i64 during p) -> i32) -> i32 = ch7\n    return 0\n", string.Empty },
        { "rrD", "func ch6(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during a), u: ref/i64 during c, g: (ref/i64 during c) -> i32) -> i32\n    origin a outlives c\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin q outlives p\n    origin p outlives q\n    var x = p\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p), ref/i64 during q, (ref/i64 during p) -> i32) -> i32 = ch6\n    return 0\n", string.Empty },
        { "rv3B", "func ch6(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during a), u: ref/i64 during c, g: (ref/i64 during c) -> i32) -> i32\n    origin a outlives c\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin p outlives q\n    var x = q\n    let f: (uniq/(ref/i64 during p), uniq/(ref/i64 during p), ref/i64 during q, (ref/i64 during x) -> i32) -> i32 = ch6\n    return 0\n", string.Empty },
        { "vargv", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let c = func (g: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32) -> i32 => 0\n    return c(eqi)\n", string.Empty },
        { "vclov", "func outer(p: ref/i64) -> i32\n    var x = p\n    let c = func (w: uniq/(ref/i64 during x), v: uniq/(ref/i64 during x)) -> i32 => 0\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = c\n    return 0\n", string.Empty },
        { "veq1v", "func eq1(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during a)) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eq1\n    return 0\n", string.Empty },
        { "veq1vr", "func eq1(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during a)) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (uniq/(ref/i64 during p), uniq/(ref/i64 during x)) -> i32 = eq1\n    return 0\n", string.Empty },
        { "veq3v", "func eq3(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during a), u: uniq/(ref/i64 during a)) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    var y = p\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p), uniq/(ref/i64 during y)) -> i32 = eq3\n    return 0\n", string.Empty },
        { "veqiv", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqi\n    return 0\n", string.Empty },
        { "veqiva", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    var g: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqi\n    g = eqi\n    return 0\n", string.Empty },
        { "veqive", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin q == p\n    var x = q\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqi\n    return 0\n", string.Empty },
        { "veqivr", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (uniq/(ref/i64 during p), uniq/(ref/i64 during x)) -> i32 = eqi\n    return 0\n", string.Empty },
        { "veqivret", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> ref/i64 during p\n    var x = p\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqi\n    return x\n", string.Empty },
        { "veqivt", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let t: ((uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32, i32) = (eqi, 0)\n    return 0\n", string.Empty },
        { "veqjv", "func eqj(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin c == a\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqj\n    return 0\n", string.Empty },
        { "veqzv", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    var z = x@ref\n    let f: (uniq/(ref/i64 during z), uniq/(ref/i64 during x)) -> i32 = eqi\n    return 0\n", string.Empty },
        { "vmixrv", "func mixr(w: uniq/(ref/i64 during a)) -> ref/i64 during a => $abort(\"a\")\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (uniq/(ref/i64 during x)) -> ref/i64 during p = mixr\n    return 0\n", string.Empty },
        { "vmixv", "func mix(w: uniq/(ref/i64 during a), v: ref/i64 during a) -> i32\n    return 0\nfunc outer(p: ref/i64) -> i32\n    var x = p\n    let f: (uniq/(ref/i64 during x), ref/i64 during p) -> i32 = mix\n    return 0\n", string.Empty },

        // A pair binder's contravariant slot in a contract comparison (SPEC 8.10, 15.3.6): H's `put` returns a closure whose Type
        // carries `put`'s inherited `t` and `U` (SPEC 3.2.1, 6.1.3), so erasing it to the result's Function Type needs `U` Owned
        // (7.7.4, 15.2.3; DECISIONS BU-222). invalid: no clause proves it.
        { "r3P", "contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during p) -> (t/i32 during p) -> i32\n        t is owner or ref\nstruct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during a) -> (t/i32 during a) -> i32\n        t is owner or ref\n        return func (v: t/i32 during a) -> i32 => 0\n", "UnprovenConstraint_Kd@8:16" },
        { "r3Pq", "contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during p) -> (t/i32 during q) -> i32\n        t is owner or ref\nstruct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during a) -> (t/i32 during a) -> i32\n        t is owner or ref\n        return func (v: t/i32 during a) -> i32 => 0\n", "UnprovenConstraint_Kd@8:16" },
        { "rv4P1", "contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during q) -> (t/i32 during p) -> i32\n        t is owner or ref\n        origin p outlives q\nstruct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during a) -> (t/i32 during a) -> i32\n        t is owner or ref\n        return func (v: t/i32 during a) -> i32 => 0\n", "UnprovenConstraint_Kd@9:16" },
        // valid: the same comparison with a body that never returns (no closure is formed or erased): the slot's relation as the
        // rows test it. (`U is Owned` would not do: `t/U` is a borrow when `t` is `ref`.)
        { "r3Pa", "contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during p) -> (t/i32 during p) -> i32\n        t is owner or ref\nstruct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during a) -> (t/i32 during a) -> i32\n        t is owner or ref\n        return $abort(\"never\")\n", string.Empty },
        { "r3Pqa", "contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during p) -> (t/i32 during q) -> i32\n        t is owner or ref\nstruct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during a) -> (t/i32 during a) -> i32\n        t is owner or ref\n        return $abort(\"never\")\n", string.Empty },
        { "rv4P1a", "contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during q) -> (t/i32 during p) -> i32\n        t is owner or ref\n        origin p outlives q\nstruct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, r: ref/i32, w: ref/i32 during a) -> (t/i32 during a) -> i32\n        t is owner or ref\n        return $abort(\"never\")\n", string.Empty },
    };

    [Theory]
    [MemberData(nameof(UseSiteFitRows))]
    public void AUseSiteFitRecordsEachPartsOwnFacts(string row, string body, string records)
    {
        var source = body + Main;
        var actual = string.Join('|', DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code}@{Position(source, x.Span)} {x.Label}".TrimEnd()));
        Assert.True(records == actual, $"{row}: {actual}");
    }

    // A program's records in order as `code@line:column`, joined by `|` (PLAN G492-a (ii)'s rows).
    internal static string Records(string source)
        => string.Join('|', DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code}@{Position(source, x.Span)}"));

    // A span's start as `line:column`, both from 1.
    private static string Position(string source, SourceSpan? span)
    {
        var start = span is { } value ? value.Start : 0;
        var newline = start == 0 ? -1 : source.LastIndexOf('\n', start - 1);
        return $"{source.AsSpan(0, start).Count('\n') + 1}:{start - newline}";
    }
}
