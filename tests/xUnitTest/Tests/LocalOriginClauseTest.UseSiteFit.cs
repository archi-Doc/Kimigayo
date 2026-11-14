// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.6.1, 15.3.5, 10.7, 15.6.5, 23.1.2 (PLAN G492-a (i)): the records of a use-site fit, one row per form and boundary of the
// fit's walk of the Type relation. The fit walks the relation once and reads each part's answer from that walk; an invariant part that
// the relation meets reversed is walked once more in the fit's own orientation. Every row's records are those of a walk rooted at each
// part, which the fit made before. The rows whose result differs from their label are not here: PLAN G900's (rq1w, rq2a, rq2av, n1f)
// and the `var` limit's (veqiv).
public partial class LocalOriginClauseTest
{
    private const string Reader2 = "struct R2<T, U>\n    public let call: (T, U) -> i32\n    public init(call: (T, U) -> i32) => self.call = call@move\n\n";

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
        // invalid: `q == p` is not proven.
        { "veqin", "func eqi(w: uniq/(ref/i64 during a), v: uniq/(ref/i64 during c)) -> i32\n    origin a == c\n    return 0\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    let x = q\n    let f: (uniq/(ref/i64 during x), uniq/(ref/i64 during p)) -> i32 = eqi\n    return 0\n", "UnprovenOriginContract_Kd@6:72 the 2nd parameter requires q == p, which is not proven" },

        // An invariant part the relation meets reversed (`uniq/X` in R's contravariant slot, SPEC 15.3.5): its facts are those of its own
        // walk in the fit's orientation, a Function Type's one oriented comparison included (10.7).
        // invalid: `a == b` is not proven, and the declared Type needs `b outlives q` (15.6.1).
        { "e3p", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(ref/i32 during a) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/(ref/i32 during b) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:5 requires a == b, which is not proven|UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven" },
        // invalid: the Function Types must be equal (`b == a` in the fit's orientation), and `b outlives q`.
        { "e3f", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@7:59 the 1st parameter requires b == a, which is not proven" },
        // invalid: with `a == b` the declared Type still needs `b outlives q`.
        { "e3fv", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> i32\n    origin q outlives p\n    origin a == b\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires a outlives q, which is not proven" },
        // valid: `a == b` and `b outlives q`.
        { "e3fw", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> i32\n    origin q outlives p\n    origin a == b\n    origin b outlives q\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", string.Empty },
        // invalid: e3f's comparison through a returned value (the relation's orientation, `a == b`).
        { "e3fr", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during a) -> i32) during p>) -> R<uniq/((ref/i32 during b) -> i32) during q>\n    origin q outlives p\n    return r0@move\n", "UnprovenOriginRelation_Kd@5:111 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@7:12 the 1st parameter requires a == b, which is not proven" },
        // invalid: a per-call input against a fixed one; the declared Type needs `b outlives q`.
        { "rq1", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven" },
        // invalid twice: rq1 without `q outlives p`.
        { "rq1n", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@6:5 requires q outlives p, which is not proven|UnprovenOriginRelation_Kd@6:14 requires b outlives q, which is not proven" },
        // invalid: rq1 through a `var` local.
        { "rq1v", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    let r: R<uniq/((ref/i32 during b) -> i32) during q> = r1@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires b outlives q, which is not proven" },
        // invalid: rq1's pair at an argument.
        { "rq1a", Reader + "func g(b: ref/i32, q: ref/i32, r: R<uniq/((ref/i32 during b) -> i32) during q>) -> i32\n    origin b outlives q\n    return 0\nfunc f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    return g(b, q, r0@move)\n", "UnprovenOriginContract_Kd@11:20 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq1a through a `var` local.
        { "rq1av", Reader + "func g(b: ref/i32, q: ref/i32, r: R<uniq/((ref/i32 during b) -> i32) during q>) -> i32\n    origin b outlives q\n    return 0\nfunc f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    var r1 = r0@move\n    return g(b, q, r1@move)\n", "UnprovenOriginContract_Kd@12:20 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq1's pair through a returned value.
        { "rq1r", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> R<uniq/((ref/i32 during b) -> i32) during q>\n    origin q outlives p\n    return r0@move\n", "UnprovenOriginRelation_Kd@5:90 requires b outlives q, which is not proven|UnprovenOriginContract_Kd@7:12 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq1 mirrored (the declared Function Type per-call); rejected by dev's limit.
        { "rq2", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/((ref/i32) -> i32) during q> = r0@move\n    return 0\n", "Unsupported_Kd@7:14" },
        // invalid twice: rq2 without `q outlives p`.
        { "rq2n", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    let r: R<uniq/((ref/i32) -> i32) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@6:5 requires q outlives p, which is not proven|Unsupported_Kd@6:14|UnprovenOriginContract_Kd@6:50 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq2 through a `var` local.
        { "rq2v", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    let r: R<uniq/((ref/i32) -> i32) during q> = r1@move\n    return 0\n", "Unsupported_Kd@8:14|UnprovenOriginContract_Kd@8:50 the 1st parameter requires the omitted Origin of ref/i32 == b, which is not proven" },
        // invalid: rq2's pair through a returned value.
        { "rq2r", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> R<uniq/((ref/i32) -> i32) during q>\n    origin q outlives p\n    return r0@move\n", "UnprovenOriginRelation_Kd@5:99 requires the omitted Origin of ref/i32 outlives q, which is not proven" },

        // The boundaries of that part's own walk: a contravariant slot again inside it (n1), two such parts in one fit and at an argument
        // (n3), a resting position under it (n5) and a pair layer's outer Origin under it (n6, SPEC 8.1.2). The `v` rows lack the
        // declared Type's `b outlives q` (15.6.1); the `w` rows add it.
        // invalid: `a == b` is not proven.
        { "n1", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/R<ref/i32 during b> during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:5 requires a == b, which is not proven|UnprovenOriginRelation_Kd@7:14 requires b outlives q, which is not proven" },
        // invalid: n1 through a `var` local.
        { "n1var", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    var r1 = r0@move\n    let r: R<uniq/R<ref/i32 during b> during q> = r1@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:14 requires b outlives a, which is not proven|UnprovenOriginRelation_Kd@8:5 requires a outlives b, which is not proven|UnprovenOriginRelation_Kd@8:14 requires b outlives q, which is not proven" },
        // invalid: a nested per-call Function Type on the declared side; rejected by dev's limit.
        { "n1fm", Reader + "func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<(ref/i32 during b) -> i32> during p>) -> i32\n    origin q outlives p\n    let r: R<uniq/R<(ref/i32) -> i32> during q> = r0@move\n    return 0\n", "Unsupported_Kd@7:14" },
        // invalid: with `a == b` the declared Type still needs `b outlives q`.
        { "n1v", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    origin a == b\n    let r: R<uniq/R<ref/i32 during b> during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:14 requires a outlives q, which is not proven" },
        // valid: `a == b` and `b outlives q`.
        { "n1w", Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<ref/i32 during a> during p>) -> i32\n    origin q outlives p\n    origin a == b\n    origin b outlives q\n    let r: R<uniq/R<ref/i32 during b> during q> = r0@move\n    return 0\n", string.Empty },
        // invalid: `a == b` and `c == d` are not proven, nor the declared Type's `b outlives q` and `d outlives q`.
        { "n3", Reader2 + "func f(a: ref/i32, b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during a) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    let r: R2<uniq/(ref/i32 during b) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@7:5 requires a == b, which is not proven|UnprovenOriginRelation_Kd@7:5 requires c == d, which is not proven|UnprovenOriginRelation_Kd@7:15 requires b outlives q, which is not proven|UnprovenOriginRelation_Kd@7:49 requires d outlives q, which is not proven" },
        // invalid: n3's pair at an argument through a `var` local.
        { "n3a", Reader2 + "func g(d: ref/i32, q: ref/i32, b: ref/i32, r: R2<uniq/(ref/i32 during b) during q, uniq/(ref/i32 during d) during q>) -> i32\n    origin b outlives q\n    origin d outlives q\n    return 0\nfunc f(a: ref/i32, b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during a) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    origin d outlives q\n    var r1 = r0@move\n    return g(d, q, b, r1@move)\n", "UnprovenOriginRelation_Kd@13:14 requires b outlives a, which is not proven|UnprovenOriginRelation_Kd@13:14 requires d outlives c, which is not proven|UnprovenOriginRelation_Kd@14:14 requires c outlives q, which is not proven|UnprovenOriginRelation_Kd@14:20 requires a outlives q, which is not proven" },
        // invalid: the first slot's Function Types differ, the second needs `c == d`.
        { "n3f", Reader2 + "func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/((ref/i32) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32\n    origin q outlives p\n    origin b outlives q\n    let r: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:5 requires c == d, which is not proven|UnprovenOriginRelation_Kd@8:58 requires d outlives q, which is not proven" },
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
        { "n6", Reader + "func f<s/T>(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    let r: R<uniq/(s/T during b) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:5 requires a == b, which is not proven|UnprovenOriginRelation_Kd@8:14 requires b outlives q, which is not proven" },
        // invalid: the original `s/T`'s implicit Origin against a written slot.
        { "n6o", Reader + "func f<s/T>(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    let r: R<uniq/(s/T during a) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:5 requires the omitted Origin of s/T == a, which is not proven|UnprovenOriginRelation_Kd@8:14 requires a outlives q, which is not proven" },
        // invalid: n6o mirrored.
        { "n6or", Reader + "func f<s/T>(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    let r: R<uniq/(s/T) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@8:5 requires a == the omitted Origin of s/T, which is not proven|UnprovenOriginRelation_Kd@8:14 requires the omitted Origin of s/T outlives q, which is not proven" },
        // invalid: n6o with `s is owner or ref`.
        { "n6ou", Reader + "func f<s/T>(a: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T) during p>) -> i32\n    s is owner or ref\n    T is Copy\n    origin q outlives p\n    let r: R<uniq/(s/T during a) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@9:5 requires the omitted Origin of s/T == a, which is not proven|UnprovenOriginRelation_Kd@9:14 requires a outlives q, which is not proven" },
        // invalid: with `a == b` the declared Type still needs `b outlives q`.
        { "n6v", Reader + "func f<s/T>(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    origin a == b\n    let r: R<uniq/(s/T during b) during q> = r0@move\n    return 0\n", "UnprovenOriginRelation_Kd@9:14 requires a outlives q, which is not proven" },
        // valid: `a == b` and `b outlives q`.
        { "n6w", Reader + "func f<s/T>(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/(s/T during a) during p>) -> i32\n    s is ref or uniq\n    origin q outlives p\n    origin a == b\n    origin b outlives q\n    let r: R<uniq/(s/T during b) during q> = r0@move\n    return 0\n", string.Empty },
    };

    [Theory]
    [MemberData(nameof(UseSiteFitRows))]
    public void AUseSiteFitRecordsEachPartsOwnFacts(string row, string body, string records)
    {
        var source = body + Main;
        var actual = string.Join('|', DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code}@{Position(source, x.Span)} {x.Label}".TrimEnd()));
        Assert.True(records == actual, $"{row}: {actual}");
    }

    // A span's start as `line:column`, both from 1.
    private static string Position(string source, SourceSpan? span)
    {
        var start = span is { } value ? value.Start : 0;
        var newline = start == 0 ? -1 : source.LastIndexOf('\n', start - 1);
        return $"{source.AsSpan(0, start).Count('\n') + 1}:{start - newline}";
    }
}
