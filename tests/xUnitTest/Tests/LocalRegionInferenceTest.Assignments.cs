// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public partial class LocalRegionInferenceTest
{
    // SPEC 15.6.5, 15.6.2, 23.1.2 (R7-O1 H; H11b G1 and G2, the owner's G1 approval with R5-M192's amendment): an assignment or store
    // into a borrow-typed local is a row of its body that A5 judges, like its `let` analogue, so a valid one is accepted (it was
    // Binding's located Unsupported_Kd, the fit-failure gate) and an invalid one gets the Loan conflict or relation records the gate
    // hid; a `during self` layer's exclusive borrow keeps SPEC 5.7's record and its node, so the conflict at the later write stands
    // beside it. Each row: the program's published records by code and text (none: accepted).
    [Theory]
    // gate4_assign_fixed_to_borrow: valid (H11b G1): `s = a`, a fixed `a` outlives y's finite region (SPEC 15.6.5 Proven).
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = a\n    return 0\n", "")]
    // gate5_assign_local_to_borrow: valid (H11b G1): `s = z@ref`, z's borrow region joins y's (SPEC 15.6.5).
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    let z = 3\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = z@ref\n    return 0\n", "")]
    // gate7_tuple_assign: valid (H11b G1): `s = (a, 2)` into a borrow-typed tuple local.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    let y = x@ref\n    var s: (ref/i32 during y, i32) = (y, 1)\n    s = (a, 2)\n    return 0\n", "")]
    // gate9_escape_bad: valid (H11b G1, N-1: earlier misnamed): `s = z@ref` read in its region only.
    [InlineData("func g(a: ref/i32) -> ref/i32 during a\n    var x = 1\n    let z = 3\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = z@ref\n    return a\n", "")]
    // a10_param_slot_assign: valid: a and b are fixed, each outlives y's finite region (SPEC 15.6.5 Proven); gate4's form twice.
    [InlineData("func g(a: ref/i32, b: ref/i32) -> i32\n    var x = 1\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = a\n    s = b\n    return s@follow\n", "")]
    // a3v_uniq_write: valid: a3 without `z = 4`.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@uniq\n    var s: uniq/i32 during y = y\n    s = z@uniq\n    s@follow = 5\n    return 0\n", "")]
    // a4v_closure_capture: valid: a4 without `z = 4`.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = z@ref\n    let k = func [s] () => s@follow\n    return k()\n", "")]
    // a5v_option_payload: valid: a5 without `z = 4`.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@ref\n    var s: Option<ref/i32 during y> = .Some(y)\n    s = .Some(z@ref)\n    let t = s\n    return 0\n", "")]
    // a9v_generic_holder: valid: a9 without `z = 4`.
    [InlineData("struct H<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@ref\n    var s: H<ref/i32 during y> = H.init(y)\n    s = H.init(z@ref)\n    return s.item@follow\n", "")]
    // gate5n_conflict: invalid (H11b G2): SPEC 15.6.2, `z = 4` while s holds z's Loan and is read after.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = z@ref\n    z = 4\n    return s@follow\n", "ComparisonLoanConflict_Kd z = 4")]
    // bu_g1_inner_scope_borrow: invalid (H11b G2): SPEC 16, 15.6.2, z's storage ends at the inner block while s holds its Loan.
    [InlineData("func g(c: bool) -> i32\n    var x = 1\n    let y = x@ref\n    var s: ref/i32 during y = y\n    if c\n        let z = 3\n        s = z@ref\n    return s@follow\n", "ComparisonLoanConflict_Kd let z = 3")]
    // a1_escape_ret: invalid: SPEC 15.6.5 Refuted, s (during y, finite) returned during a (fixed); each failed chain (15.6.1).
    [InlineData("func g(a: ref/i32) -> ref/i32 during a\n    var x = 1\n    let z = 3\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = z@ref\n    return s\n", "UnsatisfiedOriginRelation_Kd s; UnsatisfiedOriginRelation_Kd s")]
    // a2_slot_store: invalid: SPEC 15.6.5 Refuted, s (finite) stored through slot at a (fixed); each failed chain (15.6.1).
    [InlineData("func g(a: ref/i32, slot: uniq/(ref/i32 during a)) -> i32\n    var x = 1\n    let z = 3\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = z@ref\n    slot@follow = s\n    return 0\n", "UnsatisfiedOriginRelation_Kd s; UnsatisfiedOriginRelation_Kd s")]
    // a3_uniq_write: invalid: SPEC 15.6.2, `z = 4` while s holds z's exclusive Loan and is used after.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@uniq\n    var s: uniq/i32 during y = y\n    s = z@uniq\n    z = 4\n    s@follow = 5\n    return 0\n", "ComparisonLoanConflict_Kd z = 4")]
    // a4_closure_capture: invalid: SPEC 15.8.2, 15.6.2, `z = 4` while k's environment keeps s.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@ref\n    var s: ref/i32 during y = y\n    s = z@ref\n    let k = func [s] () => s@follow\n    z = 4\n    return k()\n", "ComparisonLoanConflict_Kd z = 4")]
    // a5_option_payload: invalid: SPEC 15.6.2, `z = 4` while an Option holding z's Loan is read after.
    [InlineData("func g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@ref\n    var s: Option<ref/i32 during y> = .Some(y)\n    s = .Some(z@ref)\n    z = 4\n    let t = s\n    return 0\n", "ComparisonLoanConflict_Kd z = 4")]
    // a6_fixed_local: invalid: SPEC 15.6.5 Refuted, s's Type names a (fixed); z@ref cannot outlive it.
    [InlineData("func g(a: ref/i32) -> i32\n    var s: ref/i32 during a = a\n    let z = 3\n    s = z@ref\n    return 0\n", "UnsatisfiedOriginRelation_Kd z@ref")]
    // a7_var_return: invalid: SPEC 15.6.5 Refuted, s's region holds z's borrow, returned during a.
    [InlineData("func g(a: ref/i32) -> ref/i32 during a\n    var s: ref/i32 = a\n    let z = 3\n    s = z@ref\n    return s\n", "UnsatisfiedOriginRelation_Kd s")]
    // a8_inner_tuple: invalid: SPEC 16, 15.6.2, z's storage ends at the inner block while s holds its Loan.
    [InlineData("func g(c: bool) -> i32\n    var x = 1\n    let y = x@ref\n    var s: (ref/i32 during y, i32) = (y, 1)\n    if c\n        let z = 3\n        s = (z@ref, 2)\n    return s.0@follow\n", "ComparisonLoanConflict_Kd let z = 3")]
    // a9_generic_holder: invalid: SPEC 15.6.2, `z = 4` while a holder carrying z's Loan is read after.
    [InlineData("struct H<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc g(a: ref/i32) -> i32\n    var x = 1\n    var z = 3\n    let y = x@ref\n    var s: H<ref/i32 during y> = H.init(y)\n    s = H.init(z@ref)\n    z = 4\n    return s.item@follow\n", "ComparisonLoanConflict_Kd z = 4")]
    // hp3n: invalid twice, independent (SPEC 23.1.2): SPEC 5.7, an exclusive borrow of a `during self` layer's Place; SPEC 15.6.2, `b.data = null` while r holds that Loan
    // (the borrow issues its Loan with no PQ-10 limit since Z5-R1's R-B).
    [InlineData("struct Block\n    internal var data: raw/u8 during self\n    public init() => self.data = null\n    drop => ()\nfunc f() -> i32\n    var b = Block.init()\n    let r = b.data@uniq\n    b.data = null\n    let q = r@follow\n    return 0\n", "UnsatisfiedOriginRelation_Kd b.data@uniq; ComparisonLoanConflict_Kd b.data = null")]
    public void AnAssignmentIntoABorrowLocalIsJudgedByA5(string program, string records)
    {
        var source = program + "public func main() => ()\n";
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(records, string.Join("; ", diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}")));
    }
}
