// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// G216 (a) Part 1's U1.6h rows. Rule W (DECISIONS BU-79; the owner, 2026-11-02) supersedes FailedFit and its readers (BU-77): a
// failed part is a wildcard and no evidence, so a call beside it reports its own record, Ambiguous, UnboundTypeArgument or
// NoApplicable, a dependent record SPEC 23.1.2 allows; the rows that rested now pin it. The programs are R4's probes, copied as
// written; the comments below keep each unit's reasoning for its guards.
public partial class OriginRelationDiagnosticTest
{
    private const string HTx = "func h<T>(x: T, y: i32) -> i32 => 0\n";

    private const string FOptionTuples = "func f(x: (Option<i64>, i32)) -> i32 => 0\nfunc f(x: (Option<bool>, i32)) -> i32 => 1\n";

    private const string GOptionPairs = "func g(x: i64, y: Option<i64>) -> i32 => 0\nfunc g(x: i64, y: Option<bool>) -> i32 => 1\n";

    private const string GBool1 = "func f(x: bool) -> i32 => 1\n";

    private const string KDX = "func k(c: bool, d: bool, x: i64) -> i32\n";

    private const string KDEX = "func k(c: bool, d: bool, e: bool, x: i64) -> i32\n";

    private const string ApI64 = "func ap(f: (i64) -> i64) -> i32 => 0\n";

    private const string DeferredRecords = "NoApplicableOverload_Kd identity(4); TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName";

    private const string GSlotPairs = "func g<T>(x: T, y: Option<i64>) -> i32 => 0\nfunc g<U>(x: U, y: Option<bool>) -> i32 => 1\n";

    private const string GOptionTuple = "func g<T>(x: (Option<T>, i32)) -> i32 => 0\n";

    private const string GOptionTuples = "func g<T>(x: ((Option<T>, i32), i32)) -> i32 => 0\n";

    private const string GOptionArray = "func g<T>(x: Array<Option<T>>) -> i32 => 0\n";

    private const string GTargetPairs = "func g<s/T>(x: T, y: Option<i64>) -> i32 => 0\nfunc g<r/U>(x: U, y: Option<bool>) -> i32 => 1\n";

    private const string GPairWholes = "func g<s/T>(x: s/T, y: Option<i64>) -> i32 => 0\nfunc g<r/U>(x: r/U, y: Option<bool>) -> i32 => 1\n";

    private const string GEquatables = "func g<T>(x: T, y: Option<i64>) -> i32\n    T is Equatable\n    return 0\nfunc g<U>(x: U, y: Option<bool>) -> i32\n    U is Equatable\n    return 1\n";

    private const string GAddables = "func g<T>(x: T, y: Option<i64>) -> i32\n    T is Addable<T>\n    return 0\nfunc g<U>(x: U, y: Option<bool>) -> i32\n    U is Addable<U>\n    return 1\n";

    private const string GEquatableAddable = "func g<T>(x: T, y: Option<i64>) -> i32\n    T is Equatable\n    T is Addable<T>\n    return 0\n";

    private const string GClauseSlots = "func g<T, X>(x: T, y: Option<X>, z: Option<i64>) -> i32\n    T is Addable<X>\n    return 0\nfunc g<U, W>(x: U, y: Option<W>, z: Option<bool>) -> i32\n    U is Addable<W>\n    return 1\n";

    private const string GDuplicateHeads = "func g<T, X, Y>(x: T, y: Option<i64>) -> i32\n    T is Addable<X>\n    T is Addable<Y>\n    return 0\nfunc g<U, W, Z>(x: U, y: Option<bool>) -> i32\n    U is Addable<W>\n    U is Addable<Z>\n    return 1\n";

    private const string GDifferentClauses = "func g<T>(x: T, y: Option<i64>) -> i32\n    T is Equatable\n    return 0\nfunc g<U>(x: U, y: Option<bool>) -> i32\n    U is Addable<U>\n    return 1\n";

    private const string GPermuted = "func g<T, U>(x: U, y: T) -> i32 => 0\nfunc g<V>(x: V, y: bool) -> i32 => 1\n";

    private const string GIncPairs = "func g<T>(x: (T, T), y: Option<i64>) -> i32 => 0\nfunc g<V, W>(x: (V, W), y: Option<bool>) -> i32 => 1\n";

    private const string GCounts = "func g<T>(x: T, y: Option<i64>) -> i32 => 0\nfunc g(x: i64, y: Option<bool>) -> i32 => 1\n";

    private const string GSelectorA = "func g<s/T, U>(x: (U, T), y: Option<i64>) -> i32 => 0\n";

    private const string GNameCall = KHead + "    return g(if c => undefinedName else => identity(3), .None)\n";

    private const string GTwinCall = KHead + "    return g(if c => identity(4) else => identity(3), .None)\n";

    private const string GNameAmbiguous = "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName";

    private const string GTwinAmbiguous = "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .None)";

    private const string GChain = "func g<T, X>(x: T, y: Option<X>, z: Option<i64>) -> i32\n    T is Addable<X>\n    X is Equatable\n    return 0\n";

    private const string GChainB = "func g<U, W>(x: U, y: Option<W>, z: Option<bool>) -> i32\n    U is Addable<W>\n    W is Equatable\n    return 1\n";

    private const string GMixed = "func g<T, X>(x: T, y: Option<X>, z: Option<i64>) -> i32\n    T is Equatable\n    T is Addable<X>\n    X is Copy\n    return 0\nfunc g<U, W>(x: U, y: Option<W>, z: Option<bool>) -> i32\n    U is Addable<W>\n    U is Equatable\n    return 1\n";

    private const string GSlotsNameCall = KHead + "    return g(if c => undefinedName else => identity(3), .Some(identity(5)), .None)\n";

    private const string GSlotsTwinCall = KHead + "    return g(if c => identity(4) else => identity(3), .Some(identity(5)), .None)\n";

    private const string GSlotsNameAmbiguous = "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .Some(identity(5)), .None); UnresolvedBinding_Kd undefinedName";

    private const string GSlotsTwinAmbiguous = "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .Some(identity(5)), .None)";

    private const string GPairSlots = "func g<s/T, r/U>(x: (U, T), y: Option<i64>) -> i32 => 0\nfunc g<p/V, q/W>(x: (V, W), y: Option<bool>) -> i32 => 1\n";

    private const string GPairSlotRecords = "OwnershipCaseLimit_Kd g<p/V, q/W>(x: (V, W), y: Option<bool>) -> i32; OwnershipCaseLimit_Kd g<s/T, r/U>(x: (U, T), y: Option<i64>) -> i32; UnprovenConstraint_Kd T; UnprovenConstraint_Kd U; UnprovenConstraint_Kd V; UnprovenConstraint_Kd W";

    private const string GWholeSelector = "func g<s/T, U>(x: (U, s/T), y: Option<i64>) -> i32 => 0\n";

    private const string KM = "func k(c: bool, m: i64) -> i32\n";

    private const string GEvidenceFixed = "func g<T>(x: Option<T>, y: T, z: Option<i64>) -> i32 => 0\n";

    private const string KV = "func k(c: bool, v: i64) -> i32\n";

    private const string FFunctions = "func idg<T>(v: T) -> T => v@move\nfunc f(x: (i64) -> i64, y: Option<i64>) -> i32 => 0\nfunc f(x: (bool) -> bool, y: Option<bool>) -> i32 => 1\n";

    private const string GHeads = "func g<T>(x: Option<T>, y: Option<i64>) -> i32 => 0\nfunc g<U>(x: Array<U>, y: Option<bool>) -> i32 => 1\n";

    private const string GLengths = "func g(x: [2 of i64], y: Option<i64>) -> i32 => 0\nfunc g(x: [3 of i64], y: Option<bool>) -> i32 => 1\n";

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
    // (n13b_v, n13b_deep_v, n13b_match_v, n13c_v). A slot that only a nested join's failed part sits at rests too (n2_nested_slot, R4's
    // Q4).
    [Theory]
    [InlineData("U1.6h-1", "g028", IdentityT + "func g<T>(x: (T, i32)) -> i32 => 0\n" + KD + "    return g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3))\n", "UnboundTypeArgument_Kd g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "h4", "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => 3, .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => 3, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h4b", IdentityT + "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h4b_t", IdentityT + "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => identity(4) else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-1", "h7", IdentityT + "func h<T, U>(x: T, y: Option<U>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h7_t", IdentityT + "func h<T, U>(x: T, y: Option<U>) -> i32 => 0\n" + KHead + "    return h(if c => identity(4) else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-1", "s1", IdentityT + HTx + KHead + "    let a = h(if c => undefinedName else => identity(3), 1)\n    return h(identity(4), 2)\n", "UnboundTypeArgument_Kd h(identity(4), 2); UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), 1); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s2", IdentityT + HTx + KHead + "    let a = h(if c => undefinedName else => identity(3), 1)\n    return 0\nfunc m() -> i32\n    return h(identity(4), 2)\n", "UnboundTypeArgument_Kd h(identity(4), 2); UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), 1); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s3", IdentityT + HTx + "func g<U>(x: i32, y: Option<U>) -> i32 => 0\n" + KHead + "    return g(h(if c => undefinedName else => identity(3), 1), .None)\n", "UnboundTypeArgument_Kd g(h(if c => undefinedName else => identity(3), 1), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "n13", GOptionTuple + KHead + "    return g((.Some(undefinedName), 3))\n", "UnboundTypeArgument_Kd g((.Some(undefinedName), 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "n2_nested_slot", IdentityT + "func first<T>(value: (T, i64)) -> () => ()\nfunc k(c: bool, d: bool, x: i64) -> ()\n    first(if c => ((if d => undefinedName else => identity(3)), x) else => (identity(4), x))\n", "UnboundTypeArgument_Kd first(if c => ((if d => undefinedName else => identity(3)), x) else => (identity(4), x)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s4", IdentityT + "func h<T>(x: T, y: Option<T>) -> i32 => 0\n" + KHead + "    let a = h(if c => undefinedName else => identity(3), .None)\n    let b = h(identity(4), .None)\n    return a\n", "UnboundTypeArgument_Kd h(identity(4), .None); UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
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

    // U1.6h-2 (SPEC 23.1.2, 10.4; the owner's rule of 2026-11-02, superseding C16): an ambiguity rests on a failed part only where the
    // kept candidates' patterns there mismatch with certainty, a head mismatch outside every own slot and Function Type that the
    // adaptation rules bridge in neither direction: i64 against bool at the join's position (n1_ov), as an element (f2_ov) and at a
    // child position (e3_ov), Option<i64> against Option<bool> in a nested join (g238, g238g), Option<T> against Array<U> (rh_head) and
    // [2 of i64] against [3 of i64] (rl_len), whose typed twins are valid. Every other ambiguity is reported: i64 against i64 (h3e,
    // h3, h3b), own slots (h8, perm, p1, p2, sel3_order, sel3_swap, sel6), an evidence-fixed slot (pe1: y fixes T, so the name cannot
    // separate them; pe3), clauses, which are not compared (c1-c4, c7_chain, c9_mixed), and a Function Type, whose value's own header
    // decides its fit (fe: a generic reference fits both); literal-only joins (b3f) too. Each twin is Ambiguous, but pe1's typed
    // twins: their join at x is an extra source in g<T> only, so the call contract fails first (SPEC 7.4.4, G512). Toward reporting, a
    // dependent record SPEC 23.1.2 allows: Equatable against Addable (c6), own slots that meet (inc_pair, c5, sel2_order) and c3_same,
    // c7_same and c8_order_chain, as dev gives. The selector rows keep their declarations' records, as p2's do; two pair slots per
    // function hit OwnershipCaseLimit_Kd (sel2_order), so no wholly valid selector form exists.
    [Theory]
    [InlineData("U1.6h-2", "n1_ov", IdentityT + FScalars + KHead + "    return f(if c => undefinedName else => identity(1))\n", "AmbiguousBinding_Kd f(if c => undefinedName else => identity(1)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "f2_ov", IdentityT + FTuples + KHead + "    return f((if c => identity(1) else => undefinedName, 3))\n", "AmbiguousBinding_Kd f((if c => identity(1) else => undefinedName, 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "e3_ov", IdentityT + FTuples + KHead + "    return f(if c => (identity(1), 2) else => (undefinedName, 3))\n", "AmbiguousBinding_Kd f(if c => (identity(1), 2) else => (undefinedName, 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "g238", IdentityT + FOptionTuples + KD + "    return f((if c => (if d => .None else => identity(.Some(4))) else => undefinedName, 3))\n", "AmbiguousBinding_Kd f((if c => (if d => .None else => identity(.Some(4))) else => undefinedName, 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "g238g", IdentityT + FOptionTuples + "func k(c1: bool, c2: bool, c3: bool, c4: bool, c5: bool, c6: bool) -> i32\n    return f((if c5 => (if c3 => .Some(6) else if c4 => (if c1 => .None else if c2 => identity(.Some(4)) else => undefinedName) else => .None) else if c6 => .Some(8) else => .None, 3))\n", "AmbiguousBinding_Kd f((if c5 => (if c3 => .Some(6) else if c4 => (if c1 => .None else if c2 => identity(.Some(4)) else => undefinedName) else => .None) else if c6 => .Some(8) else => .None, 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3e", IdentityT + GOptionPairs + KHead + "    return g(if c => undefinedName else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3e_t", IdentityT + GOptionPairs + KHead + "    return g(if c => identity(4) else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-2", "h8", IdentityT + GSlotPairs + KHead + "    return g(if c => undefinedName else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h8_t", IdentityT + GSlotPairs + KHead + "    return g(if c => identity(4) else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-2", "b3f", "func g(x: i64) -> i32 => 0\nfunc g(x: i32) -> i32 => 1\n" + KHead + "    return g(if c => (if c => 5 else => 6) else => 7)\n", "AmbiguousBinding_Kd g(if c => (if c => 5 else => 6) else => 7)")]
    [InlineData("U1.6h-5", "h3", GOptionPairs + KHead + "    return g(if c => undefinedName else => 3, .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => 3, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-5", "h3b", "func g(x: (i64, Option<i64>)) -> i32 => 0\nfunc g(x: (i64, Option<bool>)) -> i32 => 1\n" + KHead + "    return g((if c => undefinedName else => 3, .None))\n", "AmbiguousBinding_Kd g((if c => undefinedName else => 3, .None)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3c", "func g(x: i64, y: Option<i64>) -> i32 => 0\nfunc g(x: bool, y: Option<bool>) -> i32 => 1\n" + KHead + "    return g(if c => undefinedName else => 3, .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => 3, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "perm", IdentityT + GPermuted + KHead + "    return g(if c => undefinedName else => identity(3), true)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), true); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "perm_t", IdentityT + GPermuted + KHead + "    return g(if c => identity(4) else => identity(3), true)\n", "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), true)")]
    [InlineData("U1.6 cert", "p2", IdentityT + GTargetPairs + GNameCall, "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnprovenConstraint_Kd T; UnprovenConstraint_Kd U; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "p2_t", IdentityT + GTargetPairs + GTwinCall, GTwinAmbiguous + "; UnprovenConstraint_Kd T; UnprovenConstraint_Kd U")]
    [InlineData("U1.6 cert", "p1", IdentityT + GPairWholes + GNameCall, GNameAmbiguous)]
    [InlineData("U1.6 cert", "p1_t", IdentityT + GPairWholes + GTwinCall, GTwinAmbiguous)]
    [InlineData("U1.6 cert", "c1", IdentityT + GEquatables + GNameCall, GNameAmbiguous)]
    [InlineData("U1.6 cert", "c1_t", IdentityT + GEquatables + GTwinCall, GTwinAmbiguous)]
    [InlineData("U1.6 cert", "c2", IdentityT + GAddables + GNameCall, GNameAmbiguous)]
    [InlineData("U1.6 cert", "c2_t", IdentityT + GAddables + GTwinCall, GTwinAmbiguous)]
    [InlineData("U1.6 cert", "c3_order", IdentityT + GEquatableAddable + "func g<U>(x: U, y: Option<bool>) -> i32\n    U is Addable<U>\n    U is Equatable\n    return 1\n" + GNameCall, GNameAmbiguous)]
    [InlineData("U1.6 cert", "c3_t", IdentityT + GEquatableAddable + "func g<U>(x: U, y: Option<bool>) -> i32\n    U is Addable<U>\n    U is Equatable\n    return 1\n" + GTwinCall, GTwinAmbiguous)]
    [InlineData("U1.6 cert", "c3_same", IdentityT + GEquatableAddable + "func g<U>(x: U, y: Option<bool>) -> i32\n    U is Equatable\n    U is Addable<U>\n    return 1\n" + GNameCall, GNameAmbiguous)]
    [InlineData("U1.6 cert", "c4", IdentityT + GClauseSlots + GSlotsNameCall, GSlotsNameAmbiguous)]
    [InlineData("U1.6 cert", "c4_t", IdentityT + GClauseSlots + GSlotsTwinCall, GSlotsTwinAmbiguous)]
    [InlineData("U1.6 cert", "c7_chain", IdentityT + GChain + "func g<U, W>(x: U, y: Option<W>, z: Option<bool>) -> i32\n    U is Addable<W>\n    return 1\n" + GSlotsNameCall, GSlotsNameAmbiguous)]
    [InlineData("U1.6 cert", "c7_t", IdentityT + GChain + "func g<U, W>(x: U, y: Option<W>, z: Option<bool>) -> i32\n    U is Addable<W>\n    return 1\n" + GSlotsTwinCall, GSlotsTwinAmbiguous)]
    [InlineData("U1.6 cert", "c7_same", IdentityT + GChain + GChainB + GSlotsNameCall, GSlotsNameAmbiguous)]
    [InlineData("U1.6 cert", "c8_order_chain", IdentityT + "func g<T, X>(x: T, y: Option<X>, z: Option<i64>) -> i32\n    X is Equatable\n    T is Addable<X>\n    return 0\n" + GChainB + GSlotsNameCall, GSlotsNameAmbiguous)]
    [InlineData("U1.6 cert", "c9_mixed", IdentityT + GMixed + GSlotsNameCall, GSlotsNameAmbiguous)]
    [InlineData("U1.6 cert", "c9_t", IdentityT + GMixed + GSlotsTwinCall, GSlotsTwinAmbiguous)]
    [InlineData("U1.6 cert", "c5", IdentityT + GDuplicateHeads + GNameCall, GNameAmbiguous)]
    [InlineData("U1.6 cert", "c5_t", IdentityT + GDuplicateHeads + GTwinCall, GTwinAmbiguous)]
    [InlineData("U1.6 cert", "c6", IdentityT + GDifferentClauses + GNameCall, GNameAmbiguous)]
    [InlineData("U1.6 cert", "c6_t", IdentityT + GDifferentClauses + GTwinCall, GTwinAmbiguous)]
    [InlineData("U1.6 cert", "sel3_order", IdentityT + GSelectorA + "func g<V, p/W>(x: (V, W), y: Option<bool>) -> i32 => 1\n" + KHead + "    return g(if c => undefinedName else => (identity(3), identity(4)), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => (identity(3), identity(4)), .None); UnprovenConstraint_Kd T; UnprovenConstraint_Kd W; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "sel3_t", IdentityT + GSelectorA + "func g<V, p/W>(x: (V, W), y: Option<bool>) -> i32 => 1\n" + KHead + "    return g(if c => (identity(5), identity(6)) else => (identity(3), identity(4)), .None)\n", "AmbiguousBinding_Kd g(if c => (identity(5), identity(6)) else => (identity(3), identity(4)), .None); UnprovenConstraint_Kd T; UnprovenConstraint_Kd W")]
    [InlineData("U1.6 cert", "sel3_swap", IdentityT + GSelectorA + "func g<V, p/W>(x: (W, V), y: Option<bool>) -> i32 => 1\n" + KHead + "    return g(if c => undefinedName else => (identity(3), identity(4)), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => (identity(3), identity(4)), .None); UnprovenConstraint_Kd T; UnprovenConstraint_Kd W; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "sel3_swap_t", IdentityT + GSelectorA + "func g<V, p/W>(x: (W, V), y: Option<bool>) -> i32 => 1\n" + KHead + "    return g((identity(3), identity(4)), .None)\n", "AmbiguousBinding_Kd g((identity(3), identity(4)), .None); UnprovenConstraint_Kd T; UnprovenConstraint_Kd W")]
    [InlineData("U1.6 cert", "sel2_order", IdentityT + GPairSlots + KHead + "    return g(if c => undefinedName else => (identity(3), identity(4)), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => (identity(3), identity(4)), .None); " + GPairSlotRecords + "; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "sel2_t", IdentityT + GPairSlots + KHead + "    return g(if c => (identity(5), identity(6)) else => (identity(3), identity(4)), .None)\n", "AmbiguousBinding_Kd g(if c => (identity(5), identity(6)) else => (identity(3), identity(4)), .None); " + GPairSlotRecords)]
    [InlineData("U1.6 cert", "sel6_whole_join", IdentityT + GWholeSelector + "func g<V, p/W>(x: (V, p/W), y: Option<bool>) -> i32 => 1\n" + KM + "    return g(if c => undefinedName else => (identity(3), m), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => (identity(3), m), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "sel6_swap", IdentityT + GWholeSelector + "func g<p/W, V>(x: (p/W, V), y: Option<bool>) -> i32 => 1\n" + KM + "    return g(if c => undefinedName else => (identity(3), m), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => (identity(3), m), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "sel6_t", IdentityT + GWholeSelector + "func g<V, p/W>(x: (V, p/W), y: Option<bool>) -> i32 => 1\n" + KM + "    return g(if c => (identity(4), m) else => (identity(3), m), .None)\n", "AmbiguousBinding_Kd g(if c => (identity(4), m) else => (identity(3), m), .None)")]
    [InlineData("U1.6 cert", "pe1", GEvidenceFixed + "func g<U>(x: Option<i64>, y: U, z: Option<bool>) -> i32 => 1\n" + KV + "    return g(if c => undefinedName else => .None, v, .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => .None, v, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "pe1_t", GEvidenceFixed + "func g<U>(x: Option<i64>, y: U, z: Option<bool>) -> i32 => 1\n" + "func k(c: bool, v: i64, w: Option<i64>) -> i32\n    return g(if c => w else => .None, v, .None)\n", "UnprovenAcquisitionCorrelation_Kd g(if c => w else => .None, v, .None)")]
    [InlineData("U1.6 cert", "pe1_d", GEvidenceFixed + "func g<U>(x: Option<i64>, y: U, z: Option<bool>) -> i32 => 1\n" + "func k(c: bool, v: i64, w: Option<bool>) -> i32\n    return g(if c => w else => .None, v, .None)\n", "UnprovenAcquisitionCorrelation_Kd g(if c => w else => .None, v, .None)")]
    [InlineData("U1.6 cert", "pe3", GEvidenceFixed + "func g<U>(x: Option<U>, y: U, z: Option<bool>) -> i32 => 1\n" + KV + "    return g(if c => undefinedName else => .None, v, .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => .None, v, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "pe3_t", GEvidenceFixed + "func g<U>(x: Option<U>, y: U, z: Option<bool>) -> i32 => 1\n" + "func k(c: bool, v: i64, w: Option<i64>) -> i32\n    return g(if c => w else => .None, v, .None)\n", "AmbiguousBinding_Kd g(if c => w else => .None, v, .None)")]
    [InlineData("U1.6 cert", "fe", IdentityT + FFunctions + KHead + "    return f(if c => undefinedName else => idg, .None)\n", "AmbiguousBinding_Kd f(if c => undefinedName else => idg, .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "fe_t", IdentityT + FFunctions + KHead + "    return f(if c => idg else => idg, .None)\n", "AmbiguousBinding_Kd f(if c => idg else => idg, .None)")]
    [InlineData("U1.6 cert", "rh_head", IdentityT + GHeads + KHead + "    return g(if c => undefinedName else => identity(.None), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(.None), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "rh_head_t", IdentityT + GHeads + "func k(c: bool, o: Option<i64>) -> i32\n    return g(if c => o else => identity(.None), .None)\n", "")]
    [InlineData("U1.6 cert", "rl_len", IdentityT + GLengths + KHead + "    return g(if c => undefinedName else => identity([1, 2]), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity([1, 2]), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "rl_len_t", IdentityT + GLengths + "func k(c: bool, a: [2 of i64]) -> i32\n    return g(if c => a else => identity([1, 2]), .None)\n", "")]
    [InlineData("U1.6 cert", "inc_pair", IdentityT + GIncPairs + KHead + "    return g(if c => undefinedName else => (identity(3), identity(4)), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => (identity(3), identity(4)), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "inc_pair_t", IdentityT + GIncPairs + KHead + "    return g((3, true), .None)\n", "")]
    [InlineData("U1.6 cert", "cnt", IdentityT + GCounts + GNameCall, "UnboundTypeArgument_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6 cert", "cnt_t", IdentityT + GCounts + KHead + "    return g(true, .None)\n", "")]
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
    [InlineData("U1.6h-3'", "h2_ovl_lit", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\n" + KHead + "    let r = f(if c => undefinedName else => 8)\n    let s: bool = r\n    return 0\n", "AmbiguousBinding_Kd f(if c => undefinedName else => 8); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "n5", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\n" + KHead + "    let r = f(if c => undefinedName else => 8)\n    return 0\n", "AmbiguousBinding_Kd f(if c => undefinedName else => 8); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "n4c", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\n" + KD + "    let r = f(if c => (if d => undefinedName else => 8) else => 9)\n    return 0\n", "AmbiguousBinding_Kd f(if c => (if d => undefinedName else => 8) else => 9); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-3'", "n4", "func f(x: i64) -> i64 => x\nfunc f(x: bool) -> bool => x\nfunc k(c: bool, d: bool, e: bool) -> i32\n    let r = f(if c => (if d => (if e => undefinedName else => 7) else => 8) else => 9)\n    return 0\n", "AmbiguousBinding_Kd f(if c => (if d => (if e => undefinedName else => 7) else => 8) else => 9); UnresolvedBinding_Kd undefinedName")]
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

    // G368 (1) (SPEC 14.9.2 step 2, 10.5.1, 23.1.2; DECISIONS BU-78 as amended): a typed join whose survey found a typed part and which
    // holds a failed source is its call's Join, as a literal one is (site 4): the call selects with the failed part a wildcard, and the
    // typed source meets the selected candidate's Type on its own, at a named and a value call, a Tuple and an array element and beside a
    // failed operator source. A typed join nested in it rests at its verdict, and its typed sources meet the selected Type when the root
    // binds it (BindWaitedSources: a resting join's bound sources keep their records), also where every typed part lies in nested joins
    // (two_nested, nested_failed, nested_never, tuple_two_nested). An inner literal that fits the tree's own Type then meets the selected
    // one too, a dependent record rule W allows (inlit_fb's 5, deferred_fb's identity(4), nested_typed_fb's 8). Its twins keep their
    // records: the name repaired as `true` (w1) and as Never (w2), x fitting the one candidate (fi), and a literal sibling at the join's
    // own Type (lit_sib). Over two candidates the failed position has no evidence (site 3), so the call's Ambiguous is rule W's dependent
    // record (fov).
    [Theory]
    [InlineData("fb", GBool1 + KX + "    return f(if c => undefinedName else => x)\n", "TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("value_fb", "func fh(x: bool) -> i32 => 0\n" + KX + "    let h = fh\n    return h(if c => undefinedName else => x)\n", "TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("tupleElem_fb", "func f(x: (bool, i32)) -> i32 => 1\n" + KX + "    return f((if c => undefinedName else => x, 3))\n", "TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("arrayElem_fb", "func f(x: Array<bool>) -> i32 => 1\n" + KX + "    return f([if c => undefinedName else => x, true])\n", "TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("inlit_fb", GBool1 + KDX + "    return f(if c => (if d => undefinedName else => 5) else => x)\n", "TypeMismatch_Kd 5; TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("deferred_fb", IdentityT + GBool1 + KDX + "    return f(if c => (if d => undefinedName else => identity(4)) else => x)\n", "TypeMismatch_Kd identity(4); TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("nested_typed_fb", GBool1 + KDX + "    return f(if c => (if d => undefinedName else => x) else => 8)\n", "TypeMismatch_Kd 8; TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("nested_two_typed_fb", GBool1 + "func k(c: bool, d: bool, x: i64, y: i64) -> i32\n    return f(if c => (if d => undefinedName else => x) else => y)\n", "TypeMismatch_Kd x; TypeMismatch_Kd y; UnresolvedBinding_Kd undefinedName")]
    [InlineData("nested_tupleElem_fb", "func f(x: (bool, i32)) -> i32 => 1\n" + KDX + "    return f((if c => (if d => undefinedName else => x) else => 8, 3))\n", "TypeMismatch_Kd 8; TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("two_nested", GBool1 + KDEX + "    return f(if c => (if d => undefinedName else => x) else => (if e => undefinedName2 else => x))\n", "TypeMismatch_Kd x; TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName; UnresolvedBinding_Kd undefinedName2")]
    [InlineData("nested_failed", GBool1 + KDX + "    return f(if c => (if d => undefinedName else => x) else => undefinedName2)\n", "TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName; UnresolvedBinding_Kd undefinedName2")]
    [InlineData("nested_never", "func stop() -> Never => $abort(\"stop\")\n" + GBool1 + KDX + "    return f(if c => (if d => undefinedName else => x) else => stop())\n", "TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName")]
    [InlineData("tuple_two_nested", "func f(x: (bool, i32)) -> i32 => 1\n" + KDEX + "    return f((if c => (if d => undefinedName else => x) else => (if e => undefinedName2 else => x), 3))\n", "TypeMismatch_Kd x; TypeMismatch_Kd x; UnresolvedBinding_Kd undefinedName; UnresolvedBinding_Kd undefinedName2")]
    [InlineData("operator_fb", GBool1 + KX + "    return f(if c => 1 + true else => x)\n", "NonNumericOperand_Kd 1 + true; TypeMismatch_Kd x")]
    [InlineData("fov", FScalars + KX + "    return f(if c => undefinedName else => x)\n", "AmbiguousBinding_Kd f(if c => undefinedName else => x); UnresolvedBinding_Kd undefinedName")]
    [InlineData("fi", "func f(x: i64) -> i32 => 0\n" + KX + "    return f(if c => undefinedName else => x)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("lit_sib", GBool1 + "func k(c: bool, d: bool, x: bool) -> i32\n    return f(if c => undefinedName else if d => x else => 8)\n", "TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("w1", GBool1 + KX + "    return f(if c => true else => x)\n", "TypeMismatch_Kd x")]
    [InlineData("w2", "func stop() -> Never => $abort(\"stop\")\n" + GBool1 + KX + "    return f(if c => stop() else => x)\n", "NoApplicableOverload_Kd f(if c => stop() else => x)")]
    public void ATypedJoinWithAFailedSourceIsItsCallsJoin(string form, string body, string records)
        => AssertRecords($"{form} [G368 (1)]", body, records, null);

    // U1.6h-4 (SPEC 14.9.2 steps 2-3, 23.1.2): a failed source takes no part in whether its join's sources await (SourcesAwait,
    // BoundSince), and one that fails when its join decides is the join's cause. So in g192 the inner join J1 [twice, J2] takes twice's
    // Item Type from twice alone, and J2's anonymous source fails at it on its own (TypeMismatch_Kd 4:89) beside the name's record and the
    // call's (C2's written header at f's one candidate). Its twins: without the name (t1), with C2's header at (bool) -> bool (t2), with
    // twice (t3) or an unwritten header (t4) for C2. In t2-t4 the call selects (G368 (1)), and J1, resting on J2, is bound at f's
    // (bool) -> bool, where J1's twice fails on its own (SPEC 23.1.2: its no-failure twin is NoApplicable for the same Type), as t3's
    // outer twice does; J2's anonymous source keeps its record at twice's Item (sites 2 and 6), and g192 is unchanged.
    [Theory]
    [InlineData("U1.6h-4", "g192", "undefinedName", "func [] (v: i64) -> i64 => v", "NoApplicableOverload_Kd f(label lp: if c3 => (if c2 => twice else => (if c1 => undefinedName else => func [] (v) => v)) else => func [] (v: i64) -> i64 => v); TypeMismatch_Kd func [] (v) => v; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-4", "g192t1", "twice", "func [] (v: i64) -> i64 => v", "NoApplicableOverload_Kd f(label lp: if c3 => (if c2 => twice else => (if c1 => twice else => func [] (v) => v)) else => func [] (v: i64) -> i64 => v); TypeMismatch_Kd func [] (v) => v; TypeMismatch_Kd func [] (v: i64) -> i64 => v")]
    [InlineData("U1.6h-4", "g192t2", "undefinedName", "func [] (v: bool) -> bool => v", "TypeMismatch_Kd func [] (v) => v; TypeMismatch_Kd twice; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-4", "g192t3", "undefinedName", "twice", "TypeMismatch_Kd func [] (v) => v; TypeMismatch_Kd twice; TypeMismatch_Kd twice; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-4", "g192t4", "undefinedName", "func [] (v) => v", "TypeMismatch_Kd func [] (v) => v; TypeMismatch_Kd twice; UnresolvedBinding_Kd undefinedName")]
    public void AFailedSourceTakesNoPartInWhetherItsJoinAwaits(string unit, string form, string inner, string outer, string records)
        => AssertRecords(
            $"{form} [{unit}]",
            $"{Twice}func f(x: (bool) -> bool) -> i32 => 1\nfunc k(c1: bool, c2: bool, c3: bool) -> i32\n    return f(label lp: if c3 => (if c2 => twice else => (if c1 => {inner} else => func [] (v) => v)) else => {outer})\n",
            records,
            null);
}
