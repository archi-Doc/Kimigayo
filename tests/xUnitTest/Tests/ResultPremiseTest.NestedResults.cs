// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests of nested results (see OriginRelationDiagnosticTest.NestedSignatures.cs): under the owner's (A) a nested
// signature's result well-formedness is its own, a premise of its body and an obligation of each call of it, never a premise or an
// obligation of the enclosing definition.
public partial class ResultPremiseTest
{
    private const string StaticClause = "struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

    private const string NestedResult = "func k(q: ref/i32, r: ref/i32, f: () -> Option<ref/(ref/i32 during q) during r>) -> ";

    private const string ResultCall = "func k(q: ref/i32, f: () -> Option<T during q>) -> i32\n";

    // A value call proves its callee's nested result, its Type's own clauses included (t7, as its named-callee twin t7n and its Type
    // argument twin t7l err); k's `return q` relies on no premise of f's result, so z1 errs there only and never at bad's call; a
    // declaration that names such a result is valid (r3, v2), and each call of it errs unless the caller states the relation (v3, r3_call).
    [Theory]
    [InlineData("t7", StaticClause + ResultCall + "    let o = f()\n    return 0\n", "UnprovenConstraint_Kd f()")]
    [InlineData("t7_c", StaticClause + ResultCall + "    origin q outlives static\n    let o = f()\n    return 0\n", "")]
    [InlineData("t7n", StaticClause + "func h(x: ref/i32) -> Option<T during x>\n    origin x outlives static\n    return .None\nfunc k(q: ref/i32) -> i32\n    let o = h(q)\n    return 0\n", "UnprovenConstraint_Kd h(q); UnprovenOriginRelation_Kd q")]
    [InlineData("t7l", StaticClause + "func k(q: ref/i32) -> i32\n    let o: Option<T during q> = .None\n    return 0\n", "UnprovenConstraint_Kd .None")]
    [InlineData("z1", NestedResult + "ref/i32 during r\n    return q\nfunc bad() -> i32\n    let long = 1\n    let lr = long@ref\n    var keep = lr\n    if true\n        let short = 2\n        let sq = short@ref\n        keep = k(sq, lr, func [] () => Option.None)\n    return keep@follow\n", "UnprovenOriginRelation_Kd q")]
    [InlineData("z1_c", NestedResult + "ref/i32 during r\n    origin q outlives r\n    return q\nfunc good() -> i32\n    let long = 1\n    let lr = long@ref\n    let short = 2\n    let sr = short@ref\n    let keep = k(lr, sr, func [] () => Option.None)\n    return keep@follow\n", "")]
    [InlineData("r3", "func g(q: ref/i32, cb: (ref/i32) -> Option<ref/(ref/i32 during q)>) -> i32 => 0\n", "")]
    [InlineData("r3_call", "func g(q: ref/i32, x: ref/i32, cb: (ref/i32) -> Option<ref/(ref/i32 during q)>) -> i32\n    let o = cb(x)\n    return 0\n", "UnprovenOriginRelation_Kd cb(x)")]
    [InlineData("v2", NestedResult + "i32 => 0\n", "")]
    [InlineData("v3", NestedResult + "i32\n    let o = f()\n    return 0\n", "UnprovenOriginRelation_Kd f()")]
    [InlineData("v3_c", NestedResult + "i32\n    origin q outlives r\n    let o = f()\n    return 0\n", "")]
    public void AValueCallProvesItsCalleesNestedResult(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // Not fixed yet (cl1, the owner's (1) of 2026-10-27 for the held revision): an Item whose declared clause the conversion target's own
    // Types give (cl1) is valid by the owner's (a), if the design decides so, while the erased Item of ResultPremiseTest's ItemUses stays
    // valid when called (rk4); a design that keeps cl1 a recorded limit fails its row and states why.
    [Theory]
    [InlineData("cl1", "func pin1(q: ref/i32) -> Option<ref/(ref/i32 during q) during static>\n    origin q outlives static\n    return .None\nfunc mk() -> i32\n    let g: (ref/i32) -> Option<ref/(ref/i32) during static> = pin1\n    return 0\n", "")]
    [InlineData("rk4", "func leak(p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n    return (.None, q)\nfunc use(x: ref/i32) -> i32\n    let local: i32 = 5\n    let g: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = leak\n    return g(x, local@ref).1@follow\n", "")]
    public void AnItemsClauseHoldsByItsTargetClausesTheTargetGives(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);
}
