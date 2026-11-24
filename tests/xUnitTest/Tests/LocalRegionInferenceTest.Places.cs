// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 15.6.5, 15.4.4 (PLAN G492-a (ii)): a slot's region is one per stored part and place. Each path of a reassignable binding's storage
// is its own place (a store writes one), so `var` keeps a region per path; a `let` local is one place, so a part met on several of its
// paths is one region. Rows: g492a-ii's probes (labels.md B and C, labels-rv.md), each with its records as `code@line:column`.
public partial class LocalRegionInferenceTest
{
    private const string MainDeclaration = "public func main() => ()\n";

    public static TheoryData<string, string, string> PlaceRows => new()
    {
        { "vp1", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var t = (p, p)\n    t.0 = q\n    return t.1\n", string.Empty },
        { "vp1b", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var t = (p, p)\n    t.0 = q\n    return t.0\n", "UnprovenOriginRelation_Kd@4:12" },
        { "vp1l", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var t = (p, q)\n    t.1 = p\n    return t.0\n", string.Empty },
        { "vp2", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var t = ((p, p), (p, p))\n    t.0.1 = q\n    return t.1.0\n", string.Empty },
        { "vp2n", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    var t = ((p, p), (p, p))\n    t.0.1 = q\n    return t.0.1\n", "UnprovenOriginRelation_Kd@4:12" },
        { "mt1", "func use() -> i32\n    let x: i32 = 1\n    let t: ref/i32 = x@ref\n    return t@follow\n", string.Empty },
        { "mt1v", "func use() -> i32\n    let x: i32 = 1\n    var t: ref/i32 = x@ref\n    return t@follow\n", string.Empty },
        { "rd1v", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    swap(a@uniq, b@uniq)\n    return 0\n", string.Empty },
        { "lp1", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    let t = (p, q)\n    return t.0\n", string.Empty },
        { "lp1n", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    let t = (p, q)\n    return t.1\n", "UnprovenOriginRelation_Kd@3:12" },
        { "lp2", "func f(p: ref/i64) -> ref/i64 during p\n    let t = (p, p)\n    let u = (t, t)\n    return u.1.0\n", string.Empty },
        { "rp1", "func f(p: ref/i64, q: ref/i64) -> i32\n    let u = (p, p)\n    let w: ((ref/i64 during q, ref/i64 during q), (ref/i64 during q, ref/i64 during q)) = (u, u)\n    return 0\n", "UnprovenOriginRelation_Kd@3:91" }, // At the initializer (R7-O1 H's D5).
        { "rp1v", "func f(p: ref/i64, q: ref/i64) -> i32\n    var u = (p, p)\n    let w: ((ref/i64 during q, ref/i64 during q), (ref/i64 during q, ref/i64 during q)) = (u, u)\n    return 0\n", "UnprovenOriginRelation_Kd@3:91" }, // At the initializer (R7-O1 H's D5).
        { "rp1q", "func f(p: ref/i64, q: ref/i64) -> i32\n    origin p outlives q\n    let u = (p, p)\n    let w: ((ref/i64 during q, ref/i64 during q), (ref/i64 during q, ref/i64 during q)) = (u, u)\n    return 0\n", string.Empty },
        { "rcs1v", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use(p: ref/i32) -> i32\n    var t = (p, p)\n    var c = func [var t] () -> ()\n        swap(t.0@uniq, t.1@uniq)\n    c()\n    return 0\n", string.Empty }, // R7-O2 A1: valid (the carried slots are fixed Origins equal to their candidate p; D-868, O-443 (a))
        { "rcs1vg", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use<U>(p: ref/U) -> i32\n    var t = (p, p)\n    var c = func [var t] () -> ()\n        swap(t.0@uniq, t.1@uniq)\n    c()\n    return 0\n", string.Empty }, // R7-O2 A1: valid (the carried slots are fixed Origins equal to their candidate p; D-868, O-443 (a))
        { "rcs1b", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use(p: ref/i32) -> i32\n    let t: (ref/i32 during p, ref/i32 during p) = (p, p)\n    var c = func [var t] () -> ()\n        swap(t.0@uniq, t.1@uniq)\n    c()\n    return 0\n", string.Empty },
        { "rcs2", "func use(p: ref/i32) -> i32\n    let t = (p, p)\n    var c = func [var t] (x: ref/i32 during p) -> ()\n        t.0 = x\n    c(p)\n    return 0\n", string.Empty }, // R7-O2 A1: valid (the carried slots are fixed Origins equal to their candidate p; D-868, O-443 (a))
        { "rcs2b", "func use(p: ref/i32) -> i32\n    let t: (ref/i32 during p, ref/i32 during p) = (p, p)\n    var c = func [var t] (x: ref/i32 during p) -> ()\n        t.0 = x\n    c(p)\n    return 0\n", string.Empty },
        { "rcs3v", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use(p: ref/i32) -> i32\n    let t = (p, p)\n    var u = t\n    var c = func [var u] () -> ()\n        swap(u.0@uniq, u.1@uniq)\n    c()\n    return 0\n", string.Empty }, // R7-O2 A1: valid (the carried slots are fixed Origins equal to their candidate p; D-868, O-443 (a))
        { "gc1", "func f<T>(p: ref/T, q: ref/T) -> ref/T during p\n    let t = (p, q)\n    let u = (t, t)\n    return u.1.1\n", "UnprovenOriginRelation_Kd@4:12" },
        { "gc1b", "func f<T>(p: ref/T, q: ref/T) -> ref/T during p\n    let t = (p, q)\n    let u = (t, t)\n    return u.1.0\n", string.Empty },
        { "cc1", "func outer(z: ref/i64) -> i32\n    let g = func(p: ref/i64, q: ref/i64) -> ref/i64 during p\n        let t = (p, p)\n        let u = (t, t)\n        return u.1.0\n    return 0\n", string.Empty },
        { "cc1n", "func outer(z: ref/i64) -> i32\n    let g = func(p: ref/i64, q: ref/i64) -> ref/i64 during p\n        let t = (p, q)\n        let u = (t, t)\n        return u.0.1\n    return 0\n", "UnprovenOriginRelation_Kd@5:16" },
        { "cl1", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    let t = (p, q)\n    let g = func [t] () -> ref/i64 during p => t.0\n    return g()\n", string.Empty },
        { "cl1n", "func f(p: ref/i64, q: ref/i64) -> ref/i64 during p\n    let t = (p, q)\n    let g = func [t] () -> ref/i64 during p => t.1\n    return g()\n", "UnprovenOriginRelation_Kd@3:48" },
        { "cl2", "func f(p: ref/i64) -> ref/i64 during p\n    let t = (p, p)\n    let u = (t, t)\n    let g = func [u] () -> ref/i64 during p => u.1.0\n    return g()\n", string.Empty },
    };

    // PLAN G922's known class 1 (not asserted as correct): a `var` capture's environment binding keeps the captured `let` local's Type,
    // where SPEC 7.7.2 initializes it as `var t = t` would, whose slots are its own regions; so `swap(t.0@uniq, t.1@uniq)` over two slots
    // of one stored part is accepted, invalid by the words. Removal: G922 forms a `var` capture's environment binding as its own place;
    // these rows then take rcs1v's record (UnprovenOriginRelation_Kd at the call).
    public static TheoryData<string, string, string> VarCaptureOfALetRows => new()
    {
        { "rcs1", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use(p: ref/i32) -> i32\n    let t = (p, p)\n    var c = func [var t] () -> ()\n        swap(t.0@uniq, t.1@uniq)\n    c()\n    return 0\n", string.Empty },
        { "rcs1g", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use<U>(p: ref/U) -> i32\n    let t = (p, p)\n    var c = func [var t] () -> ()\n        swap(t.0@uniq, t.1@uniq)\n    c()\n    return 0\n", string.Empty },
        { "rcs3", "func swap<T>(x: uniq/T, y: uniq/T) => ()\nfunc use(p: ref/i32) -> i32\n    let t = (p, p)\n    let u = t\n    var c = func [var u] () -> ()\n        swap(u.0@uniq, u.1@uniq)\n    c()\n    return 0\n", string.Empty },
    };

    [Theory]
    [MemberData(nameof(PlaceRows))]
    public void ASlotsRegionIsOnePerStoredPartAndPlace(string row, string body, string records)
        => Assert.True(records == LocalOriginClauseTest.Records(body + MainDeclaration), $"{row}: {LocalOriginClauseTest.Records(body + MainDeclaration)}");

    [Theory]
    [MemberData(nameof(VarCaptureOfALetRows))]
    public void AVarCaptureOfALetLocalKeepsItsTypePendingG922(string row, string body, string records)
        => Assert.True(records == LocalOriginClauseTest.Records(body + MainDeclaration), $"{row}: {LocalOriginClauseTest.Records(body + MainDeclaration)}");
}
