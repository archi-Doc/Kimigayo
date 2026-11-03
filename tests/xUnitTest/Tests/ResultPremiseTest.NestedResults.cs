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

    // A nested result relating Origins its inputs do not relate is the declaration's record where it is written, whether or not the
    // body calls it (z1 beside `return q`, r3, r3_call, v2, v3; DECISIONS R4-M162); t7 is ResultPremiseTest's Type-clause half.
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
    public void ANestedResultIsJudgedWhereItIsWritten(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // An Item whose declared clause a conversion target would give (cl1): the target's result relates its per-call Origin to `static`
    // beyond its input, so it is recorded where it is written (DECISIONS R4-M162); the erased Item of ItemUses stays valid when called (rk4).
    [Theory]
    [InlineData("cl1", "func pin1(q: ref/i32) -> Option<ref/(ref/i32 during q) during static>\n    origin q outlives static\n    return .None\nfunc mk() -> i32\n    let g: (ref/i32) -> Option<ref/(ref/i32) during static> = pin1\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during static")]
    [InlineData("rk4", "func leak(p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n    return (.None, q)\nfunc use(x: ref/i32) -> i32\n    let local: i32 = 5\n    let g: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = leak\n    return g(x, local@ref).1@follow\n", "")]
    public void AnItemsClauseHoldsByItsTargetClausesTheTargetGives(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);
}
