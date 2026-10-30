// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of the G216 (a) redesign, written before either design from its problem statement: SPEC 15.6.1, 15.6.4,
// 15.3.6, 15.3.7, 23.1.2, the owner's (A) (2026-10-26: a nested signature's well-formedness, its inputs and its result, is its own; each
// call of it proves it for the instantiated Types), the owner's (a) (2026-10-27: a Function Item converted to a Function Type may assume
// the target's own well-formedness, as a Function value does), the owner's G56 answer and PLAN G269 (C). Each row names its form and states
// every Error record of the program on both pipelines, as `Code text` (the first line of the primary span) in ordinal order.
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
    [InlineData("n7g2", FixedHead + FixedClosure + IndependentConflict + "    return c(Option.None)\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginRelation_Kd Option.None")]
    [InlineData("n7g2_c", FixedHead + "    origin q outlives r\n" + FixedClosure + IndependentConflict + "    return c(Option.None)\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("n7h2", FixedHead + FixedClosure + FixedNone + IndependentConflict + "    return c(none)\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("n7h2_c", FixedHead + "    origin q outlives r\n" + FixedClosure + FixedNone + IndependentConflict + "    return c(none)\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("p1b", TypedOwn + "    let none: Option<ref/(ref/i32 during a) during b> = Option.None\n    return h(a, b, none)\n", "UnprovenOriginRelation_Kd h(a, b, none); UnprovenOriginRelation_Kd ref/(ref/i32 during a) during b")]
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
