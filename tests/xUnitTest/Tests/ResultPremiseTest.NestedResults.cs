// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests of nested results (see OriginRelationDiagnosticTest.NestedSignatures.cs): under DECISIONS R4-M162 a nested
// signature's result is judged where it is written, never a premise of a body or an obligation of a call.
public partial class ResultPremiseTest
{
    private const string StaticClause = "struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

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
