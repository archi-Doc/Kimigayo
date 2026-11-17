// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests of nested results (see OriginRelationDiagnosticTest.NestedSignatures.cs): under DECISIONS R4-M162 a nested
// signature's result is judged where it is written, never a premise of a body or an obligation of a call.
public partial class ResultPremiseTest
{
    private const string StaticClause = "struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

    // A clause every Origin satisfies (SPEC 15.2.3: static outlives every Origin).
    private const string AllClause = "struct Q {a}\n    origin static outlives a\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

    private const string NestedResult = "func k(q: ref/i32, r: ref/i32, f: () -> Option<ref/(ref/i32 during q) during r>) -> ";

    private const string ResultCall = "func k(q: ref/i32, f: () -> Option<T during q>) -> i32\n";

    // An enclosing body's local whose value Origin a named nested signature names (BU-56's unannotated class).
    private const string LocalR = "func leak(a: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(a)\n    let r = arr[0]\n";

    // A nested result relating Origins its inputs do not relate is the declaration's record where it is written, whether or not the
    // body calls it (z1 beside `return q`, r3, r3_call, v2, v3; DECISIONS R4-M162); t7 is ResultPremiseTest's Type-clause half. An
    // enclosing body's Origin the declaration names is a fixed Origin of it (SPEC 15.3.4; DECISIONS BU-89): r3loc and rootloc (a
    // document-local) are Binding's record, and a relation of it with another enclosing Origin needs the declaration's clause, in either
    // direction (encloc, encloc_rev; dev accepted both, and gave rootloc UnsatisfiedOriginRelation_Kd).
    [Theory]
    [InlineData("t7", StaticClause + ResultCall + "    let o = f()\n    return 0\n", "UnprovenOriginRelation_Kd Option<T during q>")]
    [InlineData("t7_c", StaticClause + ResultCall + "    origin q outlives static\n    let o = f()\n    return 0\n", "")]
    [InlineData("t7n", StaticClause + "func h(x: ref/i32) -> Option<T during x>\n    origin x outlives static\n    return .None\nfunc k(q: ref/i32) -> i32\n    let o = h(q)\n    return 0\n", "UnprovenConstraint_Kd h(q); UnprovenOriginRelation_Kd q")]
    [InlineData("t7l", StaticClause + "func k(q: ref/i32) -> i32\n    let o: Option<T during q> = .None\n    return 0\n", "UnprovenConstraint_Kd .None; UnprovenOriginRelation_Kd Option<T during q>")] // + the MIR's record, published directly (R5-M184, H3b T51)
    [InlineData("z1", NestedResult + "ref/i32 during r\n    return q\nfunc bad() -> i32\n    let long = 1\n    let lr = long@ref\n    var keep = lr\n    if true\n        let short = 2\n        let sq = short@ref\n        keep = k(sq, lr, func [] () => Option.None)\n    return keep@follow\n", "UnprovenOriginRelation_Kd q; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("z1_c", NestedResult + "ref/i32 during r\n    origin q outlives r\n    return q\nfunc good() -> i32\n    let long = 1\n    let lr = long@ref\n    let short = 2\n    let sr = short@ref\n    let keep = k(lr, sr, func [] () => Option.None)\n    return keep@follow\n", "")]
    [InlineData("r3", "func g(q: ref/i32, cb: (ref/i32) -> Option<ref/(ref/i32 during q)>) -> i32 => 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("r3_call", "func g(q: ref/i32, x: ref/i32, cb: (ref/i32) -> Option<ref/(ref/i32 during q)>) -> i32\n    let o = cb(x)\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("v2", NestedResult + "i32 => 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("v3", NestedResult + "i32\n    let o = f()\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("v3_c", NestedResult + "i32\n    origin q outlives r\n    let o = f()\n    return 0\n", "")]
    [InlineData("r3loc", LocalR + "    func k(cb: (ref/i32) -> Option<ref/(ref/i32 during r)>) -> i32 => 0\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during r)")]
    [InlineData("encloc", LocalR + "    func k(cb: () -> Option<ref/(ref/i32 during r) during a>) -> i32 => 0\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during r) during a")]
    [InlineData("encloc_rev", LocalR + "    func k(cb: () -> Option<ref/(ref/i32 during a) during r>) -> i32 => 0\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during a) during r")]
    [InlineData("rootloc", "var arr: Array<ref/i32> = []\nlet z: i32 = 0\narr.append(z@ref)\nlet r = arr[0]\nfunc k(cb: (ref/i32) -> Option<ref/(ref/i32 during r)>) -> i32 => 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during r)")]
    // PLAN G962 (the owner's O-397 (A); SPEC 15.3.4, 15.6.1, 15.3.3): an anonymous function's written result is formed where it is
    // written as a Function Type's is (FormNestedResult), each clause under the enclosing premises and the signature's own inputs, for
    // every choice of its per-call Origins: whether or not the literal is called (cl2, cl2n), in a component (cl2o), over an enclosing
    // Origin (cl2e, cl2e_c), a nested Closure's (cl2r), a generic body's (cl2g, once), at a failed header fit (cl2m) and over a value
    // call's region (cl2v, J2's cl1); a premise of k gives no per-call Origin (pc1); a body-local Borrow's Origin (esc1) is the walk's
    // body-chain side; a clause every per-call Origin satisfies (cl2q, `static outlives a`; r10l on the Function Type side, newly
    // accepted) or one the signature's inputs entail (cl2y) forms no row. F-a (the owner's O-2): cl2, cl2n, cl2o, cl2g, cl2m, cl2v and pc1
    // are A5's located Unsupported_Kd at the written result until U-O3 / R7-O3 restores the Language record at the same site.
    [InlineData("cl2", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "Unsupported_Kd T during x")] // F-a
    [InlineData("cl2n", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    return 0\n", "Unsupported_Kd T during x")] // F-a
    [InlineData("cl2o", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> Option<T during x> => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "Unsupported_Kd Option<T during x>")] // F-a
    [InlineData("cl2e", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] () -> T during p => $abort(\"no\")\n    let t = c()\n    return 0\n", "UnprovenOriginRelation_Kd T during p")]
    [InlineData("cl2e_c", StaticClause + "func k(p: ref/i32) -> i32\n    origin p outlives static\n    let c = func [] () -> T during p => $abort(\"no\")\n    let t = c()\n    return 0\n", "")]
    [InlineData("cl2s", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> T during static => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "")]
    [InlineData("cl2q", AllClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> Q during x => $abort(\"no\")\n    return 0\n", "")]
    [InlineData("cl2y", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32, y: ref/(ref/i32 during x) during static) -> T during x => $abort(\"no\")\n    return 0\n", "")]
    [InlineData("cl2r", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (y: ref/i32) -> i32\n        let d = func [] () -> T during y => $abort(\"no\")\n        return 0\n    let n = c(p)\n    return 0\n", "UnprovenOriginRelation_Kd T during y")]
    [InlineData("cl2g", StaticClause + "func mk2<U>(u: ref/U) -> i32\n    let c = func [] (x: ref/U) -> T during x => $abort(\"no\")\n    return 0\n" + "func k(p: ref/i32) -> i32\n    let a = mk2(p)\n    let bv: bool = true\n    let b = mk2(bv@ref)\n    return 0\n", "Unsupported_Kd T during x")] // F-a
    [InlineData("cl2m", StaticClause + "func k(p: ref/i32) -> i32\n    let g: (ref/i32) -> i32 = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    return 0\n", "TypeMismatch_Kd func [] (x: ref/i32) -> T during x => $abort(\"no\"); Unsupported_Kd T during x")] // F-a
    [InlineData("cl2v", StaticClause + "func k() -> i32\n    let v: i32 = 5\n    let c = func [] (x: ref/i32, y: Option<ref/i32 during x>) -> T during x => $abort(\"no\")\n    let t = c($abort(\"n\"), .Some(v@ref))\n    return 0\n", "Unsupported_Kd T during x")] // F-a
    [InlineData("pc1", StaticClause + "func k(p: ref/i32) -> i32\n    origin p outlives static\n    let c = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "Unsupported_Kd T during x")] // F-a
    [InlineData("esc1", StaticClause + "func k(p: ref/i32) -> i32\n    let v: i32 = 1\n    let r = v@ref\n    let c = func [] () -> T during r => $abort(\"no\")\n    return 0\n", "UnsatisfiedOriginRelation_Kd T during r")]
    [InlineData("r10l", AllClause + "func k(p: ref/i32) -> i32\n    let g: Option<(ref/i32) -> Q> = .None\n    return 0\n", "")]
    public void ANestedResultIsJudgedWhereItIsWritten(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // An Item whose declared clause a conversion target would give (cl1): the target's result relates its per-call Origin to `static`
    // beyond its input, so it is recorded where it is written (DECISIONS R4-M162); the erased Item of ItemUses stays valid when called (rk4).
    // F-a (H3b; the owner's O-2, 「一覧に加える」: H5's (e) list, PLAN G216 (b)): on cl1 Binding records nothing, so A5's located
    // Unsupported_Kd at the target's written relation (its text occurs once) keeps it rejected where its UnprovenOriginRelation_Kd was;
    // U-O3 (BU's R7-O3) moves the Language record to Binding at the same site and re-points the row back.
    [Theory]
    [InlineData("cl1", "func pin1(q: ref/i32) -> Option<ref/(ref/i32 during q) during static>\n    origin q outlives static\n    return .None\nfunc mk() -> i32\n    let g: (ref/i32) -> Option<ref/(ref/i32) during static> = pin1\n    return 0\n", "Unsupported_Kd ref/(ref/i32) during static")] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("rk4", "func leak(p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n    return (.None, q)\nfunc use(x: ref/i32) -> i32\n    let local: i32 = 5\n    let g: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = leak\n    return g(x, local@ref).1@follow\n", "")]
    public void AnItemsClauseHoldsByItsTargetClausesTheTargetGives(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);
}
