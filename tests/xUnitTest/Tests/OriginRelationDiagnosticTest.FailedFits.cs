// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// G216 (a) Part 1's U1.6h rows (DECISIONS BU-77): FailedFit, the candidate's pattern at each unbound failed part its evidence walk
// meets, and its readers. The programs are R4's probes, copied as written.
public partial class OriginRelationDiagnosticTest
{
    private const string HTx = "func h<T>(x: T, y: i32) -> i32 => 0\n";

    private const string FOptionTuples = "func f(x: (Option<i64>, i32)) -> i32 => 0\nfunc f(x: (Option<bool>, i32)) -> i32 => 1\n";

    private const string GOptionPairs = "func g(x: i64, y: Option<i64>) -> i32 => 0\nfunc g(x: i64, y: Option<bool>) -> i32 => 1\n";

    private const string GBool1 = "func f(x: bool) -> i32 => 1\n";

    private const string ApI64 = "func ap(f: (i64) -> i64) -> i32 => 0\n";

    private const string DeferredRecords = "NoApplicableOverload_Kd identity(4); TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName";

    private const string GSlotPairs = "func g<T>(x: T, y: Option<i64>) -> i32 => 0\nfunc g<U>(x: U, y: Option<bool>) -> i32 => 1\n";

    private const string GOptionTuple = "func g<T>(x: (Option<T>, i32)) -> i32 => 0\n";

    private const string GOptionTuples = "func g<T>(x: ((Option<T>, i32), i32)) -> i32 => 0\n";

    private const string GOptionArray = "func g<T>(x: Array<Option<T>>) -> i32 => 0\n";

    // U1.6h-1 (SPEC 23.1.2, 10.8; R4's C15, C22): the selected candidate's unsolved slots rest on a failed part only when every one occurs
    // in the candidate's pattern at a failed part, whose Type could have bound it. So `T` at the name's position is no record (g028, and
    // the first call of s1, s2 and s4), while a slot no failed part sits at keeps it: `T` at `Option<T>` beside the name at i64 (h4b; h4,
    // with a literal, from U1.6h-5's site 4: before it the literal join rested at classification), and `U` beside the name at `T` (h7);
    // their twins without the name give the same record (h4b_t, h7_t). FailedFit lives in its call's evaluation: another call of `h`
    // keeps its independent record, in the same function (s1), in another (s2) and with `T` bound by its other argument in the failing
    // call (s4), and a call nested in the failing call's outer call keeps the outer's (s3). A Case payload is a waiting form, never
    // evidence and never walked for FailedFit, so `.Some(undefinedName)` leaves `T` unsolved (n13, R4's N13). So in a join's results,
    // which an unselected call completes as its argument parts (CompleteJoinResults, U1.6 fix C), the name keeps its record in a Tuple
    // (n13b, n13b_deep nested, n13b_match) and an array (n13c) beside an independent UnboundTypeArgument_Kd: a context-typed Case waits
    // for its call's expectation (SPEC 10.5.1), so it is no evidence for `T` with a typed payload either, which the typed twins pin
    // (n13b_v, n13b_deep_v, n13b_match_v, n13c_v).
    [Theory]
    [InlineData("U1.6h-1", "g028", IdentityT + "func g<T>(x: (T, i32)) -> i32 => 0\n" + KD + "    return g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "h4", "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => 3, .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => 3, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h4b", IdentityT + "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h4b_t", IdentityT + "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => identity(4) else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-1", "h7", IdentityT + "func h<T, U>(x: T, y: Option<U>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h7_t", IdentityT + "func h<T, U>(x: T, y: Option<U>) -> i32 => 0\n" + KHead + "    return h(if c => identity(4) else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-1", "s1", IdentityT + HTx + KHead + "    let a = h(if c => undefinedName else => identity(3), 1)\n    return h(identity(4), 2)\n", "UnboundTypeArgument_Kd h(identity(4), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s2", IdentityT + HTx + KHead + "    let a = h(if c => undefinedName else => identity(3), 1)\n    return 0\nfunc m() -> i32\n    return h(identity(4), 2)\n", "UnboundTypeArgument_Kd h(identity(4), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s3", IdentityT + HTx + "func g<U>(x: i32, y: Option<U>) -> i32 => 0\n" + KHead + "    return g(h(if c => undefinedName else => identity(3), 1), .None)\n", "UnboundTypeArgument_Kd g(h(if c => undefinedName else => identity(3), 1), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "n13", GOptionTuple + KHead + "    return g((.Some(undefinedName), 3))\n", "UnboundTypeArgument_Kd g((.Some(undefinedName), 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s4", IdentityT + "func h<T>(x: T, y: Option<T>) -> i32 => 0\n" + KHead + "    let a = h(if c => undefinedName else => identity(3), .None)\n    let b = h(identity(4), .None)\n    return a\n", "UnboundTypeArgument_Kd h(identity(4), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 fix C", "n13b", GOptionTuple + KHead + "    return g(if c => (.Some(undefinedName), 3) else => (.None, 4))\n", "UnboundTypeArgument_Kd g(if c => (.Some(undefinedName), 3) else => (.None, 4)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 fix C", "n13b_v", GOptionTuple + KX + "    return g(if c => (.Some(x), 3) else => (.None, 4))\n", "UnboundTypeArgument_Kd g(if c => (.Some(x), 3) else => (.None, 4))")]
    [InlineData("U1.6 fix C", "n13b_deep", GOptionTuples + KHead + "    return g(if c => ((.Some(undefinedName), 3), 1) else => ((.None, 4), 1))\n", "UnboundTypeArgument_Kd g(if c => ((.Some(undefinedName), 3), 1) else => ((.None, 4), 1)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 fix C", "n13b_deep_v", GOptionTuples + KX + "    return g(if c => ((.Some(x), 3), 1) else => ((.None, 4), 1))\n", "UnboundTypeArgument_Kd g(if c => ((.Some(x), 3), 1) else => ((.None, 4), 1))")]
    [InlineData("U1.6 fix C", "n13b_match", GOptionTuple + KHead + "    return g(match c\n        true => (.Some(undefinedName), 3)\n        false => (.None, 4)\n    )\n", "UnboundTypeArgument_Kd g(match c; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 fix C", "n13b_match_v", GOptionTuple + KX + "    return g(match c\n        true => (.Some(x), 3)\n        false => (.None, 4)\n    )\n", "UnboundTypeArgument_Kd g(match c")]
    [InlineData("U1.6 fix C", "n13c", GOptionArray + KHead + "    return g(if c => [.Some(undefinedName)] else => [.None])\n", "UnboundTypeArgument_Kd g(if c => [.Some(undefinedName)] else => [.None]); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 fix C", "n13c_v", GOptionArray + KX + "    return g(if c => [.Some(x)] else => [.None])\n", "UnboundTypeArgument_Kd g(if c => [.Some(x)] else => [.None])")]
    public void AnUnsolvedSlotRestsOnlyWhereEveryOneIsAtAFailedPart(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6h-2 (SPEC 23.1.2, 10.4; R4's C16): an ambiguity rests on a failed part only where the kept candidates' patterns there differ,
    // structurally and modulo their own slots, so that a Type of the part could have separated them: i64 against bool at the join's
    // position (n1_ov), as an element (f2_ov) and at a child position (e3_ov), Option<i64> against Option<bool> in a nested join (g238,
    // and R4's g238g). Where they agree, no Type of the part separates them and the ambiguity is independent: i64 against i64 (h3e, and
    // its twin without the name h3e_t), T against U, each its candidate's own slot (h8, h8_t), and literal-only joins (b3f). A literal
    // beside the name: i64 against i64 is independent (h3, h3b, from U1.6h-5's site 4, which classifies the join), and i64 against bool
    // rests (h3c).
    [Theory]
    [InlineData("U1.6h-2", "n1_ov", IdentityT + FScalars + KHead + "    return f(if c => undefinedName else => identity(1))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "f2_ov", IdentityT + FTuples + KHead + "    return f((if c => identity(1) else => undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "e3_ov", IdentityT + FTuples + KHead + "    return f(if c => (identity(1), 2) else => (undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "g238", IdentityT + FOptionTuples + KD + "    return f((if c => (if d => .None else => identity(.Some(4))) else => undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "g238g", IdentityT + FOptionTuples + "func k(c1: bool, c2: bool, c3: bool, c4: bool, c5: bool, c6: bool) -> i32\n    return f((if c5 => (if c3 => .Some(6) else if c4 => (if c1 => .None else if c2 => identity(.Some(4)) else => undefinedName) else => .None) else if c6 => .Some(8) else => .None, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3e", IdentityT + GOptionPairs + KHead + "    return g(if c => undefinedName else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3e_t", IdentityT + GOptionPairs + KHead + "    return g(if c => identity(4) else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-2", "h8", IdentityT + GSlotPairs + KHead + "    return g(if c => undefinedName else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h8_t", IdentityT + GSlotPairs + KHead + "    return g(if c => identity(4) else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-2", "b3f", "func g(x: i64) -> i32 => 0\nfunc g(x: i32) -> i32 => 1\n" + KHead + "    return g(if c => (if c => 5 else => 6) else => 7)\n", "AmbiguousBinding_Kd g(if c => (if c => 5 else => 6) else => 7)")]
    [InlineData("U1.6h-5", "h3", GOptionPairs + KHead + "    return g(if c => undefinedName else => 3, .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => 3, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "h3b", "func g(x: (i64, Option<i64>)) -> i32 => 0\nfunc g(x: (i64, Option<bool>)) -> i32 => 1\n" + KHead + "    return g((if c => undefinedName else => 3, .None))\n", "AmbiguousBinding_Kd g((if c => undefinedName else => 3, .None)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3c", "func g(x: i64, y: Option<i64>) -> i32 => 0\nfunc g(x: bool, y: Option<bool>) -> i32 => 1\n" + KHead + "    return g(if c => undefinedName else => 3, .None)\n", "UnresolvedBinding_Kd undefinedName")]
    public void AnAmbiguityRestsOnlyWhereTheKeptCandidatesDifferAtAFailedPart(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6h-3 (SPEC 23.1.2, 14.9.2, 10.5.1): a failed source takes no part in its join's kind; the join records it as its cause and
    // rests on it at its verdict (DecideRest), so its siblings keep their kinds and meet the selected candidate's Type: identity(4) and
    // 8 at bool give their own records beside the name's (fp2_root_deferred, fp2_tupleElem_deferred, fp_seed_g026m), a Tuple join fits
    // no candidate whatever the name is (n1_typ), and a slot no failed part sits at is recorded (h5, as h4b). The join no longer binds
    // its literal at its default without a Type, so a default that cannot hold it writes nothing (lit_big; its twin lit_big_t is
    // valid), and a waited Closure's body is bound, so its own error is reported (clo_body, clo_body_typed; clo_body_t keeps its
    // TypeMismatch). An unselected call publishes only what its waiting join judged (B4): the literal-only and Tuple joins b3a-b3e keep
    // their call's record alone.
    [Theory]
    [InlineData("U1.6h-3", "fp2_root_deferred", IdentityT + GBool1 + "func k(c: bool, d: bool, x: i64) -> i32\n    return f(if c => (if d => undefinedName else => identity(4)) else => 8)\n", DeferredRecords)]
    [InlineData("U1.6h-3", "fp2_tupleElem_deferred", IdentityT + "func f(x: (bool, i32)) -> i32 => 1\nfunc k(c: bool, d: bool, x: i64) -> i32\n    return f((if c => (if d => undefinedName else => identity(4)) else => 8, 3))\n", DeferredRecords)]
    [InlineData("U1.6h-3", "fp_seed_g026m", IdentityT + GBool1 + KD + "    return f(if c => (if d => undefinedName else => identity(4)) else => 8)\n", DeferredRecords)]
    [InlineData("U1.6h-3", "n1_typ", IdentityT + FScalars + KX + "    return f(if c => (undefinedName, 1) else => (x, 2)).0\n", "NoApplicableOverload_Kd f(if c => (undefinedName, 1) else => (x, 2)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3", "h5", "func h<T>(x: (i64, Option<T>)) -> i32 => 0\n" + KHead + "    return h(if c => (undefinedName, .None) else => (3, .None))\n", "UnboundTypeArgument_Kd h(if c => (undefinedName, .None) else => (3, .None)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3", "lit_big", "func f(x: u128) -> i32 => 0\n" + KHead + "    return f(if c => undefinedName else => 100000000000000000000)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3", "lit_big_t", "func f(x: u128) -> i32 => 0\nfunc k(c: bool, y: u128) -> i32\n    return f(if c => y else => 100000000000000000000)\n", "")]
    [InlineData("U1.6h-3", "clo_body", ApI64 + KHead + "    return ap(if c => undefinedName else => func [] (v) => undefinedBody)\n", "UnresolvedBinding_Kd undefinedBody; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3", "clo_body_typed", ApI64 + KHead + "    return ap(if c => undefinedName else => func [] (v: i64) -> i64 => undefinedBody)\n", "UnresolvedBinding_Kd undefinedBody; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3", "clo_body_t", ApI64 + "func twice(v: i64) -> i64 => v\n" + KHead + "    return ap(if c => twice else => func [] (v) => undefinedBody)\n", "TypeMismatch_Kd func [] (v) => undefinedBody")]
    [InlineData("U1.6h-3", "b3a", GBool1 + KHead + "    return f(if c => 5 else => 2.5)\n", "NoApplicableOverload_Kd f(if c => 5 else => 2.5)")]
    [InlineData("U1.6h-3", "b3b", "func f(x: (bool, i64)) -> i32 => 1\n" + KX + "    return f(if c => (5, x) else => (2.5, x))\n", "NoApplicableOverload_Kd f(if c => (5, x) else => (2.5, x))")]
    [InlineData("U1.6h-3", "b3c", "func g(x: (f64, Option<i64>)) -> i32 => 0\nfunc g(x: (f64, Option<bool>)) -> i32 => 1\n" + KHead + "    return g(if c => (5, .None) else => (2.5, .None))\n", "NoApplicableOverload_Kd g(if c => (5, .None) else => (2.5, .None))")]
    [InlineData("U1.6h-3", "b3d", "func h<T>(x: (i64, Option<T>)) -> i32 => 0\n" + KHead + "    return h(if c => (3, .None) else => (4, .None))\n", "UnboundTypeArgument_Kd h(if c => (3, .None) else => (4, .None))")]
    [InlineData("U1.6h-3", "b3e", "func f(x: (bool, i64)) -> i32 => 1\nfunc k(c: bool, d: bool, x: i64) -> i32\n    return f(if c => (if d => (5, x) else => (2.5, x)) else => (7, x))\n", "NoApplicableOverload_Kd f(if c => (if d => (5, x) else => (2.5, x)) else => (7, x))")]
    [InlineData("U1.6h-3'", "h2_one", "func f(x: i64) -> i64 => x\n" + KHead + "    let r = f(if c => undefinedName else => 8)\n    let s: bool = r\n    return 0\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "h2_ovl_lit", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\n" + KHead + "    let r = f(if c => undefinedName else => 8)\n    let s: bool = r\n    return 0\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "n5", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\n" + KHead + "    let r = f(if c => undefinedName else => 8)\n    return 0\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "n4c", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\n" + KD + "    let r = f(if c => (if d => undefinedName else => 8) else => 9)\n    return 0\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "n4", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\nfunc k(c: bool, d: bool, e: bool) -> i32\n    let r = f(if c => (if d => (if e => undefinedName else => 7) else => 8) else => 9)\n    return 0\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "h2_ok", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\n" + KHead + "    let r = f(if c => 7 else => 8)\n    let s: i64 = r\n    return 0\n", "")]
    [InlineData("U1.6h-3'", "fp1_joinChildTuple_literal", "func f(x: (bool, i32)) -> i32 => 1\n" + KX + "    return f(if c => (undefinedName, 3) else => (8, 4))\n", "TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "fp1_joinChildTuple_typed", "func f(x: (bool, i32)) -> i32 => 1\n" + KX + "    return f(if c => (undefinedName, 3) else => (x, 4))\n", "TypeMismatch_Kd if c => (undefinedName, 3) else => (x, 4); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "g259", "func f(x: (bool, i32)) -> i32 => 1\nfunc k(c1: bool, c2: bool) -> i32\n    return f(if c2 => (4, 4) else => ((if c1 => undefinedName else => 3), 3))\n", "TypeMismatch_Kd 3; TypeMismatch_Kd 4; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "fp1_root_literal", GBool1 + KX + "    return f(if c => undefinedName else => 8)\n", "TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "fp_seed_g026m2", GBool1 + KHead + "    return f(if c => undefinedName else => 8)\n", "TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "fp1_tupleElem_literal", "func f(x: (bool, i32)) -> i32 => 1\n" + KX + "    return f((if c => undefinedName else => 8, 3))\n", "TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "fp2_root_inlit_literal", GBool1 + KD + "    return f(if c => (if d => undefinedName else => 5) else => 8)\n", "TypeMismatch_Kd 5; TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "fp2_tupleElem_inlit_literal", "func f(x: (bool, i32)) -> i32 => 1\n" + KD + "    return f((if c => (if d => undefinedName else => 5) else => 8, 3))\n", "TypeMismatch_Kd 5; TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    public void AFailedSourceRestsItsJoinOnlyAtItsVerdict(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6h-4 (SPEC 14.9.2 steps 2-3, 23.1.2): a failed source takes no part in whether its join's sources await (SourcesAwait,
    // BoundSince), and one that fails when its join decides is the join's cause. So in g192 the inner join J1 [twice, J2] takes twice's
    // Item Type from twice alone, and J2's anonymous source fails at it on its own (TypeMismatch_Kd 4:89) beside the name's record and the
    // call's (C2's written header at f's one candidate). Its twins: without the name (t1), with C2's header at (bool) -> bool (t2), with
    // twice (t3) or an unwritten header (t4) for C2.
    [Theory]
    [InlineData("U1.6h-4", "g192", "undefinedName", "func [] (v: i64) -> i64 => v", "NoApplicableOverload_Kd f(label lp: if c3 => (if c2 => twice else => (if c1 => undefinedName else => func [] (v) => v)) else => func [] (v: i64) -> i64 => v); TypeMismatch_Kd func [] (v) => v; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-4", "g192t1", "twice", "func [] (v: i64) -> i64 => v", "NoApplicableOverload_Kd f(label lp: if c3 => (if c2 => twice else => (if c1 => twice else => func [] (v) => v)) else => func [] (v: i64) -> i64 => v); TypeMismatch_Kd func [] (v) => v; TypeMismatch_Kd func [] (v: i64) -> i64 => v")]
    [InlineData("U1.6h-4", "g192t2", "undefinedName", "func [] (v: bool) -> bool => v", "TypeMismatch_Kd func [] (v) => v; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-4", "g192t3", "undefinedName", "twice", "TypeMismatch_Kd func [] (v) => v; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-4", "g192t4", "undefinedName", "func [] (v) => v", "TypeMismatch_Kd func [] (v) => v; UnresolvedBinding_Kd undefinedName")]
    public void AFailedSourceTakesNoPartInWhetherItsJoinAwaits(string unit, string form, string inner, string outer, string records)
        => AssertRecords(
            $"{form} [{unit}]",
            $"{Twice}func f(x: (bool) -> bool) -> i32 => 1\nfunc k(c1: bool, c2: bool, c3: bool) -> i32\n    return f(label lp: if c3 => (if c2 => twice else => (if c1 => {inner} else => func [] (v) => v)) else => {outer})\n",
            records,
            null);

    // U1.6h-1 and U1.6h-2: a warm rebind publishes and releases FailedFit and compares the kept candidates' patterns in reused storage
    // (two calls whose slots rest on their failed parts, and an ambiguity that rests on its failed part).
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfFailedFitsAllocatesNothing()
    {
        var c = MirBuildTest.Compile(IdentityT + HTx + FTuples + "func g<T>(x: (T, i32)) -> i32 => 0\n" + KD +
            "    let a = h(if c => undefinedName else => identity(3), 1)\n" +
            "    let b = f(if c => (identity(1), 2) else => (undefinedName, 3))\n" +
            "    return g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3))\n" + Main);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }
}
