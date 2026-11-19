// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of the G216 (a) redesign, written before either design from its problem statement: SPEC 15.6.1, 15.6.4,
// 15.3.6, 15.3.7, 23.1.2, the owner's (A) (2026-10-26: a nested signature's well-formedness, its inputs and its result, is its own; each
// call of it proves it for the instantiated Types), the owner's (a) (2026-10-27: a Function Item converted to a Function Type may assume
// the target's own well-formedness, as a Function value does), the owner's G56 answer and PLAN G269 (C). Each row names its form and states
// every Error record of the program, as `Code text` (the first line of the primary span) in ordinal order.
// The rows that wait for PLAN G216 (a) U2.2 (contextual parts, joins, nested generic calls, instantiated Types at calls and
// constructions, D-1) are kept on `claude/a5-g216a-t2-tests` and return with U2.2; the comments below may name them.
public partial class OriginRelationDiagnosticTest
{
    internal const string IndependentConflict = "    var x = 1\n    let w = x@ref\n    x = 2\n    let u = w@follow\n";

    private const string NestedHead = "func k(q: ref/i32, r: ref/i32) -> ref/i32 during r\n";

    private const string NestedInner = "    func inner(p: Option<ref/(ref/i32 during q) during r>, x: ref/i32 during q) -> ref/i32 during r => x\n";

    private const string GenericLayer = "func f<E>(x: E, y: ref/E during c, z: uniq/(ref/i32 during c)) -> () => ()\n";

    private const string LayerBox = "struct Box<T>\n    public let item: T\n    public init(item: T) => self.item = item@move\n";

    private const string Container = "struct H {a, b}\n    public let x: ref/i32 during a\n    public let y: ref/i32 during b\n" +
        "    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\n" +
        "    public func m(self: ref/Self, w: Option<ref/(ref/i32 during a) during b>, p: uniq/(ref/i32 during b)) -> ()\n        p@follow = self.x\n";

    private const string StoreHead = "func k(q: ref/i32, r: ref/i32, s: uniq/(ref/i32 during r)) -> ()\n";

    private const string StoreInner = "    func inner(w: Option<ref/(ref/i32 during q) during r>, x: ref/i32 during q, p: uniq/(ref/i32 during r)) -> ()\n" +
        "        p@follow = x\n    inner(Option.None, q, s)\n";

    // A body that reads `slot` after `short` ends, which k may store into it only through a relation q outlives r.
    private const string Dangling = "func bad() -> i32\n    let long = 1\n    var slot = long@ref\n    if true\n        let short = 2\n" +
        "        k(short@ref, long@ref, slot@uniq)\n    return slot@follow\n";

    private const string FixedHead = "func k(q: ref/i32, r: ref/i32) -> i32\n";

    private const string FixedClosure = "    let c = func [] (p: Option<ref/(ref/i32 during q) during r>) -> i32 => 0\n";

    private const string FixedNone = "    let none: Option<ref/(ref/i32 during q) during r> = Option.None\n";

    private const string TypedOwn = "func h(q: ref/i32, r: ref/i32, x: Option<ref/(ref/i32 during q) during r>) -> ref/i32 during r\n    return q\n" +
        "func k(a: ref/i32, b: ref/i32) -> ref/i32 during b\n";

    private const string ImpliedStore = "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a>)\n    slot@follow = v\n";

    private const string ImpliedCheck = "func chk(w: Option<ref/(ref/i32 during b) during a>, s: uniq/(ref/i32 during a), v: ref/i32 during b) -> i32\n    return 0\n";

    private const string ShortBody = "func bad(p: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    let sr = short@ref\n";

    private const string LongBody = "func bad(p: uniq/(ref/i32 during q), v: ref/i32 during q) -> i32\n";

    private const string ClauseStruct = "struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

    private const string H4Head = "struct E<T>\n    public init() => ()\nfunc h4(q: ref/i32, r: ref/i32, x: E<Option<ref/(ref/i32 during q) during r>>) -> ref/i32 during r\n    return q\n";

    private const string PickHead = "func pick(q: ref/i32, r: ref/i32) -> ref/i32 during r\n    origin q outlives r\n    return q\n";

    // ClauseStruct without its clause.
    private const string FreeStruct = "struct U {a}\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

    private const string LiteralHead = "func k(p: ref/i32) -> i32\n";

    private const string StaticApp = "func app(f: (ref/i32, U during static) -> i32) -> i32 => 0\n\n";

    // Nested inputs (the owner's (A); SPEC 15.6.4 step 3): each call proves its callee's input well-formedness for the instantiated Types,
    // at the argument whose Type it is (DIAGNOSTICS: an instantiated parameter Type's well-formedness is judged at its argument), also
    // for a nested named function, a method relating its struct's slots and a contextually typed `.None`; the enclosing definition
    // neither assumes it nor owes it at the nested declaration. A generic layer (g1, g2, g4) errs at `q` as dev does. Each valid twin
    // states the relation; nn1x's moves the obligation to the caller, whose `short` then ends while `slot` borrows it.
    [Theory]
    [InlineData("g1", GenericLayer + "func g(q: ref/(ref/i32 during s) during t, w: uniq/(ref/i32 during t)) -> ()\n    let m: i32 = 5\n    f(m@ref, q, w)\n", "UnsatisfiedOriginRelation_Kd q", "requires the borrow m@ref outlives t, which is false")]
    [InlineData("g2", LayerBox + "func f<E>(x: E, y: ref/Box<E> during c, z: uniq/(ref/i32 during c)) -> () => ()\nfunc g(q: ref/Box<ref/i32 during s> during t, w: uniq/(ref/i32 during t)) -> ()\n    let m: i32 = 5\n    f(m@ref, q, w)\n", "UnsatisfiedOriginRelation_Kd q", "requires the borrow m@ref outlives t, which is false")]
    [InlineData("g4", GenericLayer + "func g(v: ref/i32, q: ref/(ref/i32 during s) during t, w: uniq/(ref/i32 during t)) -> ()\n    f(v, q, w)\n", "UnprovenOriginRelation_Kd q", "requires v outlives t, which is not proven")]
    [InlineData("g1ok", GenericLayer + "func g(v: ref/i32 during s, q: ref/(ref/i32 during s) during t, w: uniq/(ref/i32 during t)) -> ()\n    f(v, q, w)\n", "", null)]
    [InlineData("nn1", NestedHead + NestedInner + "    return inner(Option.None, q)\n", "UnprovenOriginRelation_Kd Option.None", null)]
    [InlineData("nn1b", NestedHead + NestedInner + "    return inner(.None, q)\n", "UnprovenOriginRelation_Kd .None", null)]
    [InlineData("nn1ok", NestedHead + "    origin q outlives r\n" + NestedInner + "    return inner(Option.None, q)\n", "", null)]
    [InlineData("h2", Container + StoreHead + "    let h = H.init(x: q, y: r)\n    h.m(Option.None, s)\n" + Dangling, "UnprovenOriginRelation_Kd Option.None", null)]
    [InlineData("h2c", Container + StoreHead + "    origin q outlives r\n    let h = H.init(x: q, y: r)\n    h.m(Option.None, s)\n", "", null)]
    [InlineData("nn1x", StoreHead + StoreInner + Dangling, "UnprovenOriginRelation_Kd Option.None", null)]
    [InlineData("nn1x_c", StoreHead + "    origin q outlives r\n" + StoreInner + Dangling, "ComparisonLoanConflict_Kd let short = 2", null)]
    public void EachCallProvesItsCalleesNestedInputs(string form, string body, string records, string? label)
        => AssertRecords(form, body, records, label);

    // Located records that do not fail Binding (SPEC 23.1.2; the owner's G56 answer allows only an unrelated Binding failure to hide
    // one): a Loan conflict and a local annotation's well-formedness are reported beside the call's relation record. A written argument
    // Type is judged at its occurrence, so n7h2's call adds no second record for it; a contextually typed `.None` is judged at the call.
    // Each twin states the relation and keeps the independent records.
    [Theory]
    [InlineData("n7g2_c", FixedHead + "    origin q outlives r\n" + FixedClosure + IndependentConflict + "    return c(Option.None)\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("n7h2", FixedHead + FixedClosure + FixedNone + IndependentConflict + "    return c(none)\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("n7h2_c", FixedHead + "    origin q outlives r\n" + FixedClosure + FixedNone + IndependentConflict + "    return c(none)\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("p1b_c", TypedOwn + "    origin a outlives b\n    let none: Option<ref/(ref/i32 during a) during b> = Option.None\n    return h(a, b, none)\n", "")]
    public void ALocatedRecordThatDoesNotFailBindingIsReported(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // PLAN G269 (C): a contextually typed `.None` argument over the call's own Origins has its instantiated well-formedness judged at that
    // argument; a Borrow of `short` cannot outlive `q` (Refuted). The twins pass a value that outlives `q`.
    [Theory]
    [InlineData("x5", ImpliedStore + ShortBody + "    put2(p, sr, Option.None)\n    return 0\n", "UnsatisfiedOriginRelation_Kd Option.None")]
    [InlineData("x5_ok", ImpliedStore + LongBody + "    put2(p, v, Option.None)\n    return 0\n", "")]
    [InlineData("x8", ImpliedCheck + ShortBody + "    let n = chk(Option.None, p, sr)\n    return 0\n", "UnsatisfiedOriginRelation_Kd Option.None")]
    [InlineData("x8_ok", ImpliedCheck + LongBody + "    let n = chk(Option.None, p, v)\n    return 0\n", "")]
    public void AContextualNoneArgumentHasItsInstantiatedWellFormednessJudged(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // G216 (a) T2: PLAN G277's (3) and (4) (e1c, G277 (2), is CallableOriginForwardingTest's U2.1 row). s4, decided by the owner
    // (2026-10-31, 「すべて推奨の内容でお願いします」, PLAN 7f3e61b1b): a written result Type's own clause is a body obligation (SPEC 15.6.1,
    // 15.3.2, 15.6.4 step 3), as n4u's, and G277 (3) is no defect; so the Closure body's `.None` keeps dev's record (s4u), and a call of it
    // adds none (s4c; DECISIONS R4-M162: a nested signature's result is judged where it is written). Since the owner's O-397 (A)
    // (2026-11-17, PLAN G962) the written result `Option<T during q>` is also formed where it is written, as a Function Type's is
    // (FormNestedResult), so each gains that record beside the body's: two checks of two occurrences (SPEC 23.1.2). h4: a call whose contextual `E.init()` needs q outlives r is valid
    // when the result is read while `long` lives (dev no longer reports G277 (4)'s Loan conflict). Its twin h4_n reads the result after
    // `short` ends: the call proves q outlives r for its instantiated Origins (SPEC 15.6.1, 15.6.4), so `short`'s Borrow must reach the
    // `return` through `keep`, which no choice of regions allows: the Loan conflict at `short` (SPEC 15.6.6; rejected since R6-check H5,
    // when the MIR decides acceptance). h6 declares the relation with `origin q outlives r`, and h9 assigns the result to another local.
    [Theory]
    [InlineData("U2.1", "s4u", ClauseStruct + "func k(q: ref/i32) -> i32\n    let c = func [] () -> Option<T during q> => .None\n    return 0\n", "UnprovenConstraint_Kd .None; UnprovenOriginRelation_Kd Option<T during q>")]
    [InlineData("U2.3", "s4c", ClauseStruct + "func k(q: ref/i32) -> i32\n    let c = func [] () -> Option<T during q> => .None\n    let o = c()\n    return 0\n", "UnprovenConstraint_Kd .None; UnprovenOriginRelation_Kd Option<T during q>")]
    [InlineData("U2.2", "h4", H4Head + "func ok(b: ref/i32) -> i32\n    let long = 1\n    let x = h4(long@ref, b, E.init())\n    return x@follow\n", "")]
    [InlineData("U2.2", "h4_n", H4Head + "func bad() -> i32\n    let long = 1\n    var keep = long@ref\n    if true\n        let short = 2\n        keep = h4(short@ref, keep, E.init())\n    return keep@follow\n", "ComparisonLoanConflict_Kd let short = 2")]
    [InlineData("U2.2", "h6", PickHead + "func bad() -> i32\n    let long = 1\n    var keep = long@ref\n    if true\n        let short = 2\n        keep = pick(short@ref, keep)\n    return keep@follow\n", "ComparisonLoanConflict_Kd let short = 2")]
    [InlineData("U2.2", "h9", PickHead + "func bad() -> i32\n    let long = 1\n    let keep = long@ref\n    var out = long@ref\n    if true\n        let short = 2\n        out = pick(short@ref, keep)\n    return out@follow\n", "ComparisonLoanConflict_Kd let short = 2")]
    public void APlanG277FormHasItsVerdict(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // PLAN G972 (SPEC 15.3.4 "Nested signatures", 8.10, 3; the owner's O-403 (b)): no written Origin in a nested signature's input names
    // that signature's per-call Origin, an anonymous function's (its direct borrowed input's omitted Origin, named through the input) or
    // a Function Type's: not in an aggregate slot (n1, n2, n3, r6, in a meet's operand n9, in any order n10), not in a nested or written
    // direct borrow layer (g216: G216's OriginRowsTest row; n5, n6), not in a Function Type inside an input (n7, n7r), also under a local
    // annotation (n8), a header probe (n11b), a generic body (n15, once), with a local clause (loc: LocalOriginClauseTest's row) and in
    // a converted literal (g905: PLAN G905's f2v, labelled valid before (b); a15 at a call argument). One InvalidOriginBinding_Kd at the
    // written Origin; it stays bound, so independent records stay. The conversion's comparison solves the literal's per-call Origin
    // (SPEC 15.3.6-15.3.7, board D-822) and holds for n8, g905 and a15, so G972's record is their only one (F's G972 ruling 6 (1) (i):
    // their UnprovenOriginContract_Kd came from the old instantiation; DECISIONS supersedes R4-M212's parenthetical on the conversion
    // record). Valid: an outer Origin (v1; v1t, whose clause is a premise of
    // the literal), x's own slot, a body variable (v4), and the per-call Origin where it is visible: the literal's result (r1), its body
    // (r14, v10's inner literal), a named signature (v7); v12b is n11b's twin, h2 a Function Type's sibling input over a set fixed by
    // equality to an outer Origin (8.10).
    [Theory]
    [InlineData("n1", ClauseStruct + LiteralHead + "    let c = func [] (x: ref/i32, y: T during x) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n2", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32, y: U during x) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n3", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32, y: Option<U during x>) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("r6", FreeStruct + LiteralHead + "    let c = func [] (x: ref/U, y: U during x) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n9", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32, y: U during (x and p)) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n10", FreeStruct + LiteralHead + "    let c = func [] (y: U during x, x: ref/i32) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("g216", "func g() -> i32\n    let h = func [] (p: ref/i32, r: ref/(ref/i32 during p)) -> i32 => 1\n    return 0\n", "InvalidOriginBinding_Kd p")]
    [InlineData("n5", LiteralHead + "    let c = func [] (x: ref/i32, y: Option<ref/i32 during x>) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n6", LiteralHead + "    let c = func [] (x: ref/i32, y: ref/i32 during x) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n7", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32, f: (U during x) -> i32) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n7r", LiteralHead + "    let c = func [] (x: ref/i32, f: () -> ref/i32 during x) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n8", FreeStruct + LiteralHead + "    let c: (ref/i32, U) -> i32 = func [] (x: ref/i32, y: U during x) -> i32 => 0\n    return 0\n", "InvalidOriginBinding_Kd x")] // G972's record alone: the comparison holds under the solve (F's G972 ruling 6 (1) (i); board D-822)
    [InlineData("n11b", FreeStruct + StaticApp + LiteralHead + "    let r = app(func [] (x: ref/i32, y: U during x) -> i32 => 0)\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("n15", FreeStruct + "func mk<V>(v: ref/V) -> i32\n    let c = func [] (x: ref/V, y: U during x) -> i32 => 0\n    return 0\n\n" + LiteralHead + "    let a = mk(p)\n    let bv: bool = true\n    let b = mk(bv@ref)\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("loc", "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n\nfunc f() -> i32\n    let g = func (a: ref/i32 during b, b: ref/i32) -> i32\n        let h0 = H.init(a)\n        let h: H{x} = h0@move\n            origin x.s outlives b\n        return h.item@follow\n    return 0\n", "InvalidOriginBinding_Kd b")]
    [InlineData("g905", "func f(c: ref/i32) -> i32\n    let k: (ref/i32 during c, uniq/((ref/i32 during c) -> i32)) -> i32 = func (b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) => 0\n    return 0\n", "InvalidOriginBinding_Kd b")] // G972's record alone: the comparison holds under the solve (F's G972 ruling 6 (1) (i); board D-822)
    [InlineData("a15", FreeStruct + "func apply(q: ref/i32, f: (ref/i32, U during q) -> i32) -> i32 => 0\n\n" + LiteralHead + "    return apply(p, func [] (x: ref/i32, y: U during x) -> i32 => 0)\n", "InvalidOriginBinding_Kd x")] // G972's record alone: the comparison holds under the solve (F's G972 ruling 6 (1) (i); board D-822)
    [InlineData("v1", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32, y: U during p) -> i32 => 0\n    return 0\n", "")]
    [InlineData("v1t", ClauseStruct + LiteralHead + "    let c = func [] (x: ref/i32, y: T during p) -> i32 => 0\n    return 0\n", "")]
    [InlineData("v4", FreeStruct + LiteralHead + "    let c = func [] (x: ref/U, y: U during x.a) -> i32 => 0\n    return 0\n", "")]
    [InlineData("r1", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32) -> ((U during x) -> i32) => $abort(\"no\")\n    return 0\n", "")]
    [InlineData("r14", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32) -> i32\n        let g: Option<(U during x) -> i32> = .None\n        return 0\n    return 0\n", "")]
    [InlineData("v10", FreeStruct + LiteralHead + "    let c = func [] (x: ref/i32) -> i32\n        let d = func [] (y: U during x) -> i32 => 0\n        return 0\n    return 0\n", "")]
    [InlineData("v7", FreeStruct + LiteralHead + "    func inner(x: ref/i32, y: U during x) -> i32 => 0\n    return 0\n", "")]
    [InlineData("v12b", FreeStruct + StaticApp + LiteralHead + "    let r = app(func [] (x: ref/i32, y: U during static) -> i32 => 0)\n    return 0\n", "")]
    [InlineData("h2", FreeStruct + "func h2(x: ref/i32, cb: (U{c}, ref/i32 during c.a) -> ())\n    origin c.a == x\n    return\n\n" + LiteralHead + "    return 0\n", "")]
    public void ANestedSignaturesInputNamesNoPerCallOriginOfItsSignature(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // The G216 (a) acceptance check: the program parses without a record, and its Error records are exactly `records` (each `Code text`,
    // ordinal order, "; " between them; empty for a valid program), one of them labelled `label` when it is given.
    internal static void AssertRecords(string form, string body, string records, string? label)
    {
        var source = body + Main;
        CompilationTestHelper.ParseSuccess(source, "Hello.kimi");
        var errors = ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        var shown = string.Join("; ", errors.Select(static x => $"{x.Code} {(x.Text ?? string.Empty).Split('\n')[0]}").Order(StringComparer.Ordinal));
        var labelled = label is null || errors.Any(x => x.Label == label);
        Assert.True(shown == records && labelled, $"{form}: expected [{records}]{(label is null ? string.Empty : $" labelled '{label}'")}, found [{shown}] (labels: {string.Join("; ", errors.Select(static x => x.Label))})");
    }
}
