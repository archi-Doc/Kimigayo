// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 15.3.4, 15.4.2, 15.6.5, 15.2.1 (PLAN G492-a (ii); the owner's O-249 Q1 (a)): a `let` local's unwritten Origin slots are its
// regions, as a `var` local's are, so a nested signature that names the local names the local's own Origin, a fixed Origin there with no
// premise from the declaring body. Rows: g492a-ii's probes (labels.md, labels-rv.md), each with its records as `code@line:column`.
public partial class OriginPremiseCatalogTest
{
    // The class 1 family (invalid rows accepted before (ii)), their annotated and `var` twins, and the forms beside them. a1p and a1s3
    // take their `var` twin's records: the omitted slot's located limit at the nested signature (a1pv, a1s3v; design-ii §6 Exposure).
    public static TheoryData<string, string, string> NestedSignatureRows => new()
    {
        { "a1", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r = p\n    func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n        return v\n    return 0\n", "UnprovenOriginRelation_Kd@4:16" },
        { "a1b", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r: ref/i64 during p = p\n    func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n        return v\n    return 0\n", string.Empty },
        { "a1n", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r = p\n    func inner(w: ref/(ref/i64 during q) during q, v: ref/i64 during p) -> ref/i64 during q\n        return v\n    return 0\n", "UnprovenOriginRelation_Kd@4:16" },
        { "a1q", "func outer(p: ref/i64, q: ref/i64) -> i32\n    origin p outlives q\n    let r = p\n    func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n        return v\n    return 0\n", string.Empty },
        { "a1p", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r: ref/i64 = p\n    func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n        return v\n    return 0\n", "Unsupported_Kd@3:39|UnprovenOriginRelation_Kd@4:16" },
        { "a1pv", "func outer(p: ref/i64, q: ref/i64) -> i32\n    var r: ref/i64 = p\n    func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n        return v\n    return 0\n", "Unsupported_Kd@3:39|UnprovenOriginRelation_Kd@4:16" },
        { "a3", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r = p\n    let f = func(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q => v\n    return 0\n", "UnprovenOriginRelation_Kd@3:98" },
        { "a3b", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r: ref/i64 during p = p\n    let f = func(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q => v\n    return 0\n", string.Empty },
        { "a5a", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r = p\n    let f = func(v: ref/i64 during p) -> ref/i64 during r => v\n    return 0\n", "UnprovenOriginRelation_Kd@3:62" },
        { "m1", "struct S\n    public let k: i32\n    public init(k: i32) => self.k = k\n    public func m(self: ref/Self, p: ref/i64, q: ref/i64) -> i32\n        let r = p\n        func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n            return v\n        return 0\n", "UnprovenOriginRelation_Kd@7:20" },
        { "m1b", "struct S\n    public let k: i32\n    public init(k: i32) => self.k = k\n    public func m(self: ref/Self, p: ref/i64, q: ref/i64) -> i32\n        let r: ref/i64 during p = p\n        func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n            return v\n        return 0\n", string.Empty },
        { "c1", "func outer(z: ref/i64) -> i32\n    let g = func(p: ref/i64, q: ref/i64) -> i32\n        let r = p\n        func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n            return v\n        return 0\n    return 0\n", "UnprovenOriginRelation_Kd@5:20" },
        { "c1b", "func outer(z: ref/i64) -> i32\n    let g = func(p: ref/i64, q: ref/i64) -> i32\n        let r: ref/i64 during p = p\n        func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n            return v\n        return 0\n    return 0\n", string.Empty },
        { "g1", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r = p\n    func inner<T>(w: ref/(ref/i64 during r) during q, v: ref/i64 during p, t: ref/T) -> ref/i64 during q\n        return v\n    return 0\n", "UnprovenOriginRelation_Kd@4:16" },
        { "g1b", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r: ref/i64 during p = p\n    func inner<T>(w: ref/(ref/i64 during r) during q, v: ref/i64 during p, t: ref/T) -> ref/i64 during q\n        return v\n    return 0\n", string.Empty },
        { "n2", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r = p\n    func mid() -> i32\n        func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n            return v\n        return 0\n    return mid()\n", "UnprovenOriginRelation_Kd@5:20" },
        { "n2b", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r: ref/i64 during p = p\n    func mid() -> i32\n        func inner(w: ref/(ref/i64 during r) during q, v: ref/i64 during p) -> ref/i64 during q\n            return v\n        return 0\n    return mid()\n", string.Empty },
        { "t1", "func outer<T>(p: ref/T, q: ref/T) -> i32\n    let r = p\n    func inner(w: ref/(ref/T during r) during q, v: ref/T during p) -> ref/T during q\n        return v\n    return 0\n", "UnprovenOriginRelation_Kd@4:16" },
        { "t1b", "func outer<T>(p: ref/T, q: ref/T) -> i32\n    let r: ref/T during p = p\n    func inner(w: ref/(ref/T during r) during q, v: ref/T during p) -> ref/T during q\n        return v\n    return 0\n", string.Empty },
        { "a1s", "struct View {source}\n    public let data: ref/i64 during source\n    public init(data: ref/i64 during source) => self.data = data\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    let v = View.init(p)\n    func inner(w: ref/(ref/i64 during v.source) during q, x: ref/i64 during p) -> ref/i64 during q\n        return x\n    return 0\n", "UnprovenOriginRelation_Kd@7:16" },
        { "a1sb", "struct View {source}\n    public let data: ref/i64 during source\n    public init(data: ref/i64 during source) => self.data = data\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    let v: View during p = View.init(p)\n    func inner(w: ref/(ref/i64 during v.source) during q, x: ref/i64 during p) -> ref/i64 during q\n        return x\n    return 0\n", string.Empty },
        { "a1s3", "struct Two {a, b}\n    public let x: ref/i64 during a\n    public let y: ref/i64 during b\n    public init(x: ref/i64 during a, y: ref/i64 during b)\n        self.x = x\n        self.y = y\nfunc outer(p: ref/i64, q: ref/i64, z: ref/i64) -> i32\n    let v = Two.init(x: p, y: p)\n    func inner(w: ref/(ref/i64 during v.a) during q, x: ref/(ref/i64 during v.b) during z) -> ref/i64 during q\n        return x@follow\n    return 0\n", "Unsupported_Kd@10:16" },
        { "a1s3b", "struct Two {a, b}\n    public let x: ref/i64 during a\n    public let y: ref/i64 during b\n    public init(x: ref/i64 during a, y: ref/i64 during b)\n        self.x = x\n        self.y = y\nfunc outer(p: ref/i64, q: ref/i64, z: ref/i64) -> i32\n    let v = Two.init(x: p, y: p)\n    func inner(w: ref/(ref/i64 during v.a) during q, x: ref/(ref/i64 during v.b) during z) -> ref/i64 during z\n        return x@follow\n    return 0\n", string.Empty },
        { "a1s3v", "struct Two {a, b}\n    public let x: ref/i64 during a\n    public let y: ref/i64 during b\n    public init(x: ref/i64 during a, y: ref/i64 during b)\n        self.x = x\n        self.y = y\nfunc outer(p: ref/i64, q: ref/i64, z: ref/i64) -> i32\n    var v = Two.init(x: p, y: p)\n    func inner(w: ref/(ref/i64 during v.a) during q, x: ref/(ref/i64 during v.b) during z) -> ref/i64 during q\n        return x@follow\n    return 0\n", "Unsupported_Kd@10:16" },
        { "a1s3c", "struct Two {a, b}\n    public let x: ref/i64 during a\n    public let y: ref/i64 during b\n    public init(x: ref/i64 during a, y: ref/i64 during b)\n        self.x = x\n        self.y = y\nfunc outer(p: ref/i64, q: ref/i64, z: ref/i64) -> i32\n    let v = Two.init(x: p, y: p)\n    func inner(w: ref/(ref/i64 during v.a) during q, x: ref/(ref/i64 during v.b) during q) -> ref/i64 during q\n        return x@follow\n    return 0\n", string.Empty },
        { "a1s3cv", "struct Two {a, b}\n    public let x: ref/i64 during a\n    public let y: ref/i64 during b\n    public init(x: ref/i64 during a, y: ref/i64 during b)\n        self.x = x\n        self.y = y\nfunc outer(p: ref/i64, q: ref/i64, z: ref/i64) -> i32\n    var v = Two.init(x: p, y: p)\n    func inner(w: ref/(ref/i64 during v.a) during q, x: ref/(ref/i64 during v.b) during q) -> ref/i64 during q\n        return x@follow\n    return 0\n", string.Empty },
        { "a6a", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let r = p\n    let f = func(v: ref/i64 during r) -> ref/i64 during r => v\n    return 0\n", string.Empty },
        { "a6c", "func outer(p: ref/i64, q: ref/i64) -> i64\n    let r = p\n    func inner(v: ref/i64 during r) -> i64\n        return v@follow\n    return inner(r)\n", string.Empty },
        { "z1", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let x = p\n    let z = x@ref\n    func inner(w: ref/(ref/i64 during z) during q, v: ref/i64 during p) -> ref/i64 during q\n        return v\n    return 0\n", "UnprovenOriginRelation_Kd@5:16" },
        { "z2", "func outer(p: ref/i64, q: ref/i64) -> i32\n    let x = p\n    let z = x@ref\n    let f = func() -> ref/i64 during z => x\n    let k = f()\n    return 0\n", "UnprovenOriginRelation_Kd@4:43" },
        { "k1", "func f(p: ref/i64) -> ref/i64 during p\n    let r = p\n    let g = func() -> ref/i64 during p => r\n    return g()\n", string.Empty },
        { "k1v", "func f(p: ref/i64) -> ref/i64 during p\n    var r = p\n    let g = func() -> ref/i64 during p => r\n    return g()\n", string.Empty },
    };

    // The rows (ii) gives a `let` local's region that the comparison rules of G492-a (iii) decide (the Middle's C-350, 16:16): on (ii)
    // alone each keeps a pre-existing record; with (iii) merged each meets its label: c6 valid (O-286 (a)); vi3l, vr5nr, vl1l and opc5
    // (OriginPremiseCatalogTest's `(x and z)` row) invalid with a Language record; vl1c and vcc1l invalid, an unproven constraint; vl1cn
    // invalid with a constraint and a relation record. Each row moves to its label when (iii) lands, which precedes (ii) (O-328 (a)).
    public static TheoryData<string, string, string> ComparisonRegionRows => new()
    {
        { "c6", "func h(w: ref/i64 during a, v: ref/i64 during c) -> ref/i64 during c\n    origin a outlives c\n    return w\nfunc outer(p: ref/i64) -> i32\n    let x = p\n    let z = x@ref\n    let f: (ref/i64 during z, ref/i64 during x) -> ref/i64 during x = h\n    return 0\n", "UnprovenOriginContract_Kd@7:71" },
        { "vi3l", "func idf(w: ref/i64 during a) -> ref/i64 during a => w\nfunc outer(p: ref/i64) -> i32\n    let v: i64 = 1\n    let x = v@ref\n    let f: (ref/i64 during x) -> ref/i64 during p = idf\n    return 0\n", "UnprovenOriginContract_Kd@5:53" },
        { "vr5nr", "func h(w: ref/i64 during a, v: ref/i64 during c) -> ref/i64 during c\n    origin a outlives c\n    return w\nfunc outer(p: ref/i64) -> ref/i64 during p\n    let x = p\n    let z = x@ref\n    let f: (ref/i64 during z, ref/i64 during x) -> ref/i64 during x = h\n    return x\n", "UnprovenOriginContract_Kd@7:71" },
        { "vl1l", "func outer(p: ref/i64) -> i32\n    let v: i64 = 1\n    let x = v@ref\n    let c = func (w: ref/i64) -> ref/i64 during x => $abort(\"a\")\n    let f: (ref/i64) -> ref/i64 = c\n    return 0\n", "UnprovenOriginContract_Kd@5:35" },
        { "opc5", "func g(w: ref/(ref/i64 during a) during b, v: ref/i64 during c) -> ref/i64 during b\n    origin c outlives b\n    return v\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    let y: i64 = 1\n    let x = y@ref\n    let z = x@ref\n    let f: (ref/(ref/i64 during z) during q, ref/i64 during (x and z)) -> ref/i64 during q = g\n    return 0\n", "UnprovenOriginContract_Kd@8:94" },
        { "vl1c", "func outer(s: ref/i64 during static) -> i32\n    let x = s\n    let c = func [x] (v: ref/i64) -> ref/i64 during x => x\n    let f: (ref/i64) -> ref/i64 = c\n    return 0\n", "UnprovenConstraint_Kd@4:35" },
        { "vcc1l", "func callIt<F>(f: ref/F, y: ref/i32) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    return 0\nfunc use(x: ref/i32 during static) -> i32\n    let w = x\n    let c = func (n: ref/i32) -> ref/i32 during w => $abort(\"a\")\n    let local: i32 = 4\n    return callIt(c@ref, local@ref)\n", "UnprovenConstraint_Kd@8:19" },
        { "vl1cn", "func outer(p: ref/i64) -> i32\n    let x = p\n    let c = func [x] (v: ref/i64) -> ref/i64 during x => x\n    let f: (ref/i64) -> ref/i64 = c\n    return 0\n", "UnprovenConstraint_Kd@4:35" },
    };

    [Theory]
    [MemberData(nameof(NestedSignatureRows))]
    public void ANestedSignatureNamesAnEnclosingLocalsOwnRegion(string row, string body, string records)
        => Assert.True(records == LocalOriginClauseTest.Records(body + Main), $"{row}: {LocalOriginClauseTest.Records(body + Main)}");

    [Theory]
    [MemberData(nameof(ComparisonRegionRows))]
    public void ALetLocalsRegionReachesTheComparisonRules(string row, string body, string records)
        => Assert.True(records == LocalOriginClauseTest.Records(body + Main), $"{row}: {LocalOriginClauseTest.Records(body + Main)}");
}
