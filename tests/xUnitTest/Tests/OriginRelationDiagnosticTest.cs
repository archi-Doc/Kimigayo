// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.6.1, 15.6.5, 23.1: a fit that holds structurally but fails in an Origin part is an Origin relation, reported at the
// value that supplies the longer end. A body-local Borrow reaching a fixed Origin is Refuted (UnsatisfiedOriginRelation_Kd, Language)
// and an underivable relation between fixed Origins is Unknown (UnprovenOriginRelation_Kd, Proof); both ends are Origin displays and
// neither is ever a Type mismatch.
public partial class OriginRelationDiagnosticTest
{
    private const string Main = "public func main() => ()\n";

    private const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n";

    private const string Bad = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    let r = local@ref\n    return r\n";

    private const string Refuted = "Return or store an owned value, or a borrow of an input, instead of a borrow of storage that ends with the body";

    private const string Keep = "func keep(view: ref/(Slice<i32> during s)) -> ref/(Slice<i32> during s) during view => view\n";

    private const string Make = "func make(values: Slice<i32> during s) -> Slice<i32> during s => values\n";

    private const string Cursor = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n";

    private const string Entries = "    var entries: Dictionary<i32, uniq/i32 during a> = [1: value@move]\n";

    private const string Captured = "func run() -> ()\n    var n: i32 = 1\n    let value = n@uniq\n";

    private const string Holder2 = "struct Holder2 {a}\n    public var link: uniq/i32 during a\n";

    private const string Discarding = Holder2 + "func f(h: uniq/(Holder2 during a) during s, c: bool) -> ()\n";

    private const string Box = "struct Box<T>\n    public var item: T\n";

    private const string Optional = "struct H {a}\n    public var o: uniq/Option<string> during a\n";

    private const string Lens = "struct Lens<T> {source}\n    internal let at: ref/T during source\n";

    private const string View = "struct P\n    public var x: i32 = 0\n    public init() => ()\nstruct H2 {a}\n    public var o: objuniq/P during a\n";

    // BU's 2c-9 review, B1: a body keeping, past a block, what a call through a Function Type annotation that writes every input returns.
    private const string KeepHead = "func bad() -> i32\n    let long = 1\n    let lr = long@ref\n    let lr2 = long@ref\n    var keep = lr\n";

    private const string WrittenInputs = "(ref/(ref/i32 during sq) during lr, ref/i32 during sq) -> ref/i32 during lr";

    private const string WrittenClosure = "func [] (p: ref/(ref/i32 during sq) during lr, s: ref/i32 during sq) -> ref/i32 during lr => s";

    private const string WrittenParameter = "func k(q: ref/i32, r: ref/i32, t: ref/(ref/i32 during a) during b, " +
        "f: (ref/(ref/i32 during q) during r) -> ref/i32 during r) -> ref/i32 during r\n    origin a outlives q\n    origin b outlives r\n";

    // The same parameter with WrittenClosure's two inputs, passed the Closure by a body that keeps k's result past the block of `short`.
    private const string WrittenParameterCalled = "func k(q: ref/i32, r: ref/i32, t: ref/(ref/i32 during a) during b, " +
        "f: (ref/(ref/i32 during q) during r, ref/i32 during q) -> ref/i32 during r) -> ref/i32 during r\n" +
        "    origin a outlives q\n    origin b outlives r\n    return f(t, q)\n" + KeepHead +
        "    if true\n        let short = 2\n        let sq = short@ref\n" +
        "        let f = " + WrittenClosure + "\n        keep = k(sq, lr, lr2@ref, f)\n    return keep@follow\n";

    [Theory]
    [InlineData("func pick(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    return b\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "b", "requires b outlives a, which is not proven", null)]
    [InlineData("func f(x: ref/i32, y: ref/i32, c: bool) -> ref/i32 during x\n    return if c => x else => y\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "if c => x else => y", "requires y outlives x, which is not proven", null)]
    [InlineData(Holder + "func pick(p: ref/Holder, q: ref/i32) -> ref/(ref/i32 during q) during p\n    return p.item@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p.item@ref", "requires p.a outlives q, which is not proven", null)]
    [InlineData("func toStatic(x: ref/i32) -> ref/i32 during static\n    return x\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "x", "requires x outlives static, which is not proven", null)]
    // A local annotation's fit is A5's record at its declaration (R6-check H5, DECISIONS R5-M185; H3b T45: formerly at `b`).
    [InlineData("func f(a: ref/i32, b: ref/i32) -> i32\n    let r: ref/i32 during a = b\n    return r@follow\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "let r: ref/i32 during a = b", "requires b outlives a, which is not proven", null)]
    [InlineData("func pick<length N, T>(values: ref/[N of T] during source) -> ref/T during sorce\n    return values[0]@ref/T\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "values[0]@ref/T", "requires source outlives sorce, which is not proven", null)]
    // SPEC 15.6.3, 22.2.3: an Iterator's item Reborrowed through `self` from an exclusive Field is contained in the receiver's Origin;
    // no effect bound exempts it (the adopted proposal Abstract Calls Reach by Paths removed `preserves results`' exemption, PLAN G193).
    [InlineData(Cursor + "    public func next(self: uniq/Self) -> Option<ref/i32 during source> => .Some(self.value)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "self.value", "requires self outlives self.source, which is not proven", null)]
    // SPEC 15.2.1, 15.3.4, 15.6.5: an anonymous function's omitted parameter slot is a body inference variable of its evaluating body,
    // fixed in the Closure body, and is shown by its projection as a named function's input slot is.
    [InlineData(Lens + "func g() -> i32\n    let f = func (l: ref/Lens<i32>, p: ref/i32 during l.source) -> ref/i32 during static => p\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires l.source outlives static, which is not proven", null)]
    [InlineData(Lens + "func g() -> i32\n    let f = func (l: Lens<i32>, p: ref/i32 during l.source) -> ref/i32 during static => p\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires l.source outlives static, which is not proven", null)]
    public void AnOriginOnlyFailureIsOneRelationRecord(string body, string code, string text, string label, string? origin)
    {
        var source = body + Main;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal(code, error.Code);
        AssertFitReason(error);
        Assert.Equal(text, Text(source, error.Span));
        Assert.Equal(label, error.Label);
        var reason = error.Reason!;
        if (origin is null)
        {
            Assert.Equal("expression", reason[1].Origin);
            Assert.Null(error.Related);
        }
        else
        {
            Assert.Equal(("borrow", origin), (reason[1].Origin, reason[1].Value));
            var related = Assert.Single(error.Related!);
            Assert.Equal(("origin", origin), (related.Role, Text(source, related.Span)));
        }
    }

    // The failures above that A5 judges (R6-check H5: the MIR decides, DECISIONS R5-M185): A5's record is at the statement, initializer
    // or Borrow where it judges the relation (H3b T45), with the same Reason, relates the borrow that created the Loan with the role
    // `borrow` where it finds one and names a borrow reached through a local by its Loan's place (`local`; T49, as A5 publishes them until
    // A6). A relation Binding also records at the value stays Binding's record there, and A5's precedes it as its twin until H11 (H0b §5.4,
    // DECISIONS R4-M108; T50). Each record: its primary, label, longer end and related roles.
    [Theory]
    [InlineData(Bad, "return r: requires the borrow local outlives x, which is false; borrow local; borrow local@ref, origin return r | r: requires the borrow local@ref outlives x, which is false; borrow local@ref; origin local@ref")]
    [InlineData("func store(anchor: ref/i32, target: uniq/(ref/i32 during anchor))\n    let local: i32 = 5\n    target@follow = local@ref\n", "target@follow = local@ref: requires the borrow local@ref outlives anchor, which is false; borrow local@ref; origin local@ref | local@ref: requires the borrow local@ref outlives anchor, which is false; borrow local@ref; origin local@ref")]
    [InlineData("func g(a: ref/i32) -> i32\n    let local = 4\n    let r: ref/i32 during a = local@ref\n    return r@follow\n", "let r: ref/i32 during a = local@ref: requires the borrow local@ref outlives a, which is false; borrow local@ref; borrow local@ref, origin local@ref")]
    [InlineData("func g(b: ref/i32) -> ref/(ref/i32 during b) during b\n    let c = b\n    return c@ref\n", "return c@ref: requires the borrow c@ref outlives b, which is false; borrow c@ref; origin c@ref | c@ref: requires the borrow c@ref outlives b, which is false; borrow c@ref; origin c@ref")]
    [InlineData("func g(b: ref/i32) -> ref/(ref/i32 during b) during b\n    return b@ref\n", "return b@ref: requires the borrow b@ref outlives b, which is false; borrow b@ref; origin b@ref | b@ref: requires the borrow b@ref outlives b, which is false; borrow b@ref; origin b@ref")]
    // SPEC 13.5.6, 4.8.1: a Borrow of a Slice handle borrows the handle's own storage, never the elements' source.
    [InlineData("func leak(values: Slice<i32> during s) -> ref/(Slice<i32> during s) during s\n    return values@ref\n", "return values@ref: requires the borrow values@ref outlives s, which is false; borrow values@ref; origin values@ref | values@ref: requires the borrow values@ref outlives s, which is false; borrow values@ref; origin values@ref")]
    [InlineData("func leak(values: Slice<i32> during s) -> ref/(Slice<i32> during s) during s\n    let local = values\n    return local@ref\n", "return local@ref: requires the borrow local@ref outlives s, which is false; borrow local@ref; origin local@ref | local@ref: requires the borrow local@ref outlives s, which is false; borrow local@ref; origin local@ref")]
    [InlineData("func leak(values: Slice<i32> during s) -> uniq/(Slice<i32> during s) during s\n    var local = values\n    return local@uniq\n", "local@uniq: requires the borrow local@uniq outlives s, which is false; borrow local@uniq; borrow local@uniq, origin local@uniq")]
    [InlineData(Keep + "func leak(values: Slice<i32> during s) -> ref/(Slice<i32> during s) during s\n    return keep(values)\n", "return keep(values): requires the borrow values outlives s, which is false; borrow values; origin values | keep(values): requires the borrow values outlives s, which is false; borrow values; origin values")]
    [InlineData(Make + "func leak(values: Slice<i32> during s) -> ref/(Slice<i32> during s) during s\n    return make(values)@ref\n", "return make(values)@ref: requires the borrow make(values)@ref outlives s, which is false; borrow make(values)@ref; origin make(values)@ref | make(values)@ref: requires the borrow make(values)@ref outlives s, which is false; borrow make(values)@ref; origin make(values)@ref")]
    public void AnOriginOnlyFailureIsA5sRecordBesideAnyBindingTwin(string body, string records)
    {
        var source = body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.All(errors, static x => Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), x.Code));
        Assert.All(errors, AssertFitReason);
        Assert.Equal(records, string.Join(" | ", errors.Select(x => Relation(source, x))));
    }

    // SPEC 15.2.1, 23.1: an omitted slot that no parameter projection names, here a Tuple element's of an anonymous function's input, is
    // shown and related at its Type occurrence.
    [Fact]
    public void AnUnnamedOmittedSlotIsShownAtItsTypeOccurrence()
    {
        var source = Lens + "func g() -> i32\n    let f = func (t: ref/(Lens<i32>, i32)) -> ref/i32 during static => t.0.at\n    return 0\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "t.0.at"), (error.Code, Text(source, error.Span)));
        Assert.Equal("requires the omitted Origin of Lens<i32> outlives static, which is not proven", error.Label);
        Assert.Equal(("omitted", "Lens<i32>"), (error.Reason![1].Origin, error.Reason[1].Value));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("origin", "Lens<i32>"), (related.Role, Text(source, related.Span)));
    }

    // SPEC 15.3.4, 15.6.1 (PLAN G216): an omitted Origin of a borrow layer nested in an anonymous function's or a local Function Type's
    // parameter is a body variable, and that input's well-formedness is a premise of the signature and an obligation of each call, never
    // of the evaluating body: G216's h1 and h2, a Closure body that needs the premise, a Function Type annotation and a named function's
    // Function Type parameter writing the Origin, each called, are valid. In a named signature the omission is MissingOriginBinding_Kd
    // (15.4.2). Beside an unrelated Binding error each keeps only its own record (SPEC 23.1.2). Each call proves the premise also where
    // every input is written (BU's 2c-9 review, B1; SPEC 15.6.4 step 3): a Function Type annotation's call whose result is kept past the
    // block of `short` lets `keep` outlive it (the Loan conflict; valid called in one scope; beside a Binding error A5 publishes it too,
    // direct until A6 2g-1: R6-check H5, DECISIONS R5-M184; H3b T51), and a named function's written Function Type parameter, whose input
    // premise is no premise of the function's body, is Unknown at its call unless the body's clause proves it.
    [Theory]
    [InlineData("func g() -> i32\n    let h = func [] (p: ref/(ref/i32)) -> i32 => 1\n    return 0\n", "")]
    [InlineData("func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32)) -> i32 => p@follow@follow\n    let x = 5\n    return h(x@ref@ref)\n", "")]
    [InlineData("func g() -> i32\n    let h = func [] (p: ref/(ref/i32)) -> ref/i32 => p@follow\n    let x = 5\n    let y = x@ref\n    return h(y@ref)@follow\n", "")]
    [InlineData("func g() -> i32\n    let f: (ref/(ref/i32)) -> i32 = func (value) => value@follow@follow\n    let n = 7\n    let view = n@ref\n    return f(view@ref)\n", "")]
    [InlineData("func g(a: ref/i32, cb: (ref/(ref/i32 during a)) -> i32) -> i32 => cb(a@ref)\n", "")]
    [InlineData("func g(p: ref/(ref/i32)) -> i32 => 0\n", "MissingOriginBinding_Kd ref/i32")]
    [InlineData("func g(p: ref/(ref/i32)) -> i32 => 0\nfunc m() -> i32\n    let b: bool = 3\n    return 0\n", "MissingOriginBinding_Kd ref/i32; TypeMismatch_Kd 3")]
    [InlineData("func g() -> i32\n    let h = func [] (p: ref/(ref/i32)) -> i32 => 1\n    let b: bool = 3\n    return 0\n", "TypeMismatch_Kd 3")]
    [InlineData(KeepHead + "    let short = 2\n    let sq = short@ref\n    let f: " + WrittenInputs + " = " + WrittenClosure + "\n    keep = f(lr2@ref, sq)\n    return keep@follow\n", "")]
    [InlineData(KeepHead + "    if true\n        let short = 2\n        let sq = short@ref\n        let f: " + WrittenInputs + " = " + WrittenClosure + "\n        keep = f(lr2@ref, sq)\n    return keep@follow\n", "ComparisonLoanConflict_Kd let short = 2")]
    [InlineData(KeepHead + "    if true\n        let short = 2\n        let sq = short@ref\n        let f: " + WrittenInputs + " = func [] (p, s) => s\n        keep = f(lr2@ref, sq)\n    return keep@follow\n", "ComparisonLoanConflict_Kd let short = 2")]
    [InlineData(KeepHead + "    if true\n        let short = 2\n        let sq = short@ref\n        let f: " + WrittenInputs + " = " + WrittenClosure + "\n        keep = f(lr2@ref, sq)\n    let b: bool = 3\n    return keep@follow\n", "ComparisonLoanConflict_Kd let short = 2; TypeMismatch_Kd 3")]
    [InlineData(WrittenParameter + "    origin q outlives r\n    return f(t)\n", "")]
    [InlineData(WrittenParameter + "    return f(t)\n", "UnprovenOriginRelation_Kd t")]
    [InlineData(WrittenParameter + "    return f(t)\nfunc m() -> i32\n    let b: bool = 3\n    return 0\n", "UnprovenOriginRelation_Kd t; TypeMismatch_Kd 3")]
    [InlineData(WrittenParameterCalled, "UnprovenOriginRelation_Kd t; ComparisonLoanConflict_Kd let short = 2")]
    public void ANestedSignaturesInputIsWellFormedByPremiseAndAtEachCall(string body, string records)
    {
        var source = body + Main;
        Assert.Equal(records, string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {Text(source, x.Span).Split('\n')[0]}")));
    }

    // SPEC 15.6.3, 4.7.1 (DECISIONS BU-27): a Borrow reached through safe borrow layers is contained in each, from its last dereference
    // outward up to and including the first shared one: an index result's reference to a local Dictionary, Array or UniqSlice, a local's
    // borrow, a Slice view, a stored reference reached through an exclusive parameter, local, object view or receiver. Binding names it by
    // their meet, so returning it as one inner layer's Origin is Refuted (a body-local layer) or Unknown (a fixed one).
    [Theory]
    [InlineData("func leak(view: UniqSlice<uniq/i32 during a> during s) -> uniq/i32 during a\n    var v = view@move\n    return v[0]@follow@uniq\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "v[0]@follow@uniq", "requires the borrow v[0]@follow@uniq outlives a, which is false")]
    [InlineData("func leak(p: uniq/i32 during a) -> uniq/i32 during a\n    var slot = p\n    var rr = slot@uniq\n    return rr@follow@follow@uniq\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "rr@follow@follow@uniq", "requires the borrow slot outlives a, which is false")]
    [InlineData("func leak(values: Slice<uniq/i32 during a> during s) -> ref/i32 during a\n    return values[0]@follow@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "values[0]@follow@ref", "requires s outlives a, which is not proven")]
    [InlineData("func leak(p: uniq/(uniq/i32 during a) during b) -> uniq/i32 during a\n    var q = p\n    return q@follow@follow@uniq\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "q@follow@follow@uniq", "requires b outlives a, which is not proven")]
    [InlineData("func leak(a: uniq/(uniq/i32 during x) during y) -> uniq/i32 during x\n    var cur = a@follow@uniq\n    return cur@follow@follow@uniq\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "cur@follow@follow@uniq", "requires y outlives x, which is not proven")]
    [InlineData(Holder2 + "func leak(holder: uniq/(Holder2 during a) during h) -> uniq/i32 during a\n    return holder.link\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "holder.link", "requires h outlives a, which is not proven")]
    [InlineData(Box + "func leak(h: objref/Box<uniq/i32 during a> during o) -> ref/i32 during a\n    return h.item@follow@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h.item@follow@ref", "requires o outlives a, which is not proven")]
    [InlineData(View + "func leak(h: uniq/(H2 during a) during s) -> uniq/i32 during a\n    return h.o@follow.x@uniq\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h.o@follow.x@uniq", "requires s outlives a, which is not proven")]
    [InlineData(Optional + "    public func get(self: uniq/Self during s) -> uniq/string during a\n        match self.o\n            .Some(let v) => return v\n            .None => $abort(\"none\")\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "v", "requires s outlives self.a, which is not proven")]
    // SPEC 3.5.1, 14.9.2: a bare Reborrow, `uniq/T` or `objuniq/T`, at an initializer or as a join's source without a fixed Target
    // Result Type is named so too, and so is the join.
    [InlineData(Holder2 + "func leak(holder: uniq/(Holder2 during a) during h, c: bool) -> uniq/i32 during a\n    let r = if c => holder.link else => holder.link\n    return r\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "r", "requires h outlives a, which is not proven")]
    [InlineData(View + "func leak(h: uniq/(H2 during a) during s) -> objuniq/P during a\n    var x = h.o\n    return x\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "x", "requires s outlives a, which is not proven")]
    [InlineData(View + "func leak(h: uniq/(H2 during a) during s, c: bool) -> objuniq/P during a\n    let r = if c => h.o else => h.o\n    return r\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "r", "requires s outlives a, which is not proven")]
    public void ABorrowThroughLayersIsContainedInEach(string body, string code, string text, string label)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, text, label), (error.Code, Text(source, error.Span), error.Label));
    }

    // The rows of ABorrowThroughLayersIsContainedInEach that Binding records at the returned value X: A5's record at `return X` precedes
    // Binding's as its twin until H11 (R6-check H5, DECISIONS R5-M185; H0b §5.4, R4-M108; H3b T50), and names a meet reached through a
    // local by its Loan's place (`entries`). Each record: its primary and label.
    [Theory]
    [InlineData("func leak(value: uniq/i32 during a) -> ref/i32 during a\n" + Entries + "    let found = entries[1]@follow@ref\n    return found\n", "return found: requires the borrow entries outlives a, which is false | found: requires the borrow entries[1]@follow@ref outlives a, which is false")]
    [InlineData("func leak(value: uniq/i32 during a) -> ref/i32 during a\n" + Entries + "    return entries.index(1)@follow@follow@ref\n", "return entries.index(1)@follow@follow@ref: requires the borrow entries.index(1)@follow@follow@ref outlives a, which is false | entries.index(1)@follow@follow@ref: requires the borrow entries.index(1)@follow@follow@ref outlives a, which is false")]
    [InlineData("func leak(value: uniq/i32 during a) -> uniq/i32 during a\n" + Entries + "    return entries.indexUniq(1)@follow@follow@uniq\n", "return entries.indexUniq(1)@follow@follow@uniq: requires the borrow entries.indexUniq(1)@follow@follow@uniq outlives a, which is false | entries.indexUniq(1)@follow@follow@uniq: requires the borrow entries.indexUniq(1)@follow@follow@uniq outlives a, which is false")]
    [InlineData("func leak(value: uniq/i32 during a) -> ref/i32 during a\n    var entries: Array<uniq/i32 during a> = [value@move]\n    return entries[0]@follow@ref\n", "return entries[0]@follow@ref: requires the borrow entries[0]@follow@ref outlives a, which is false | entries[0]@follow@ref: requires the borrow entries[0]@follow@ref outlives a, which is false")]
    [InlineData("func leak(value: uniq/i32 during a) -> uniq/i32 during a\n" + Entries + "    return entries[1]\n", "return entries[1]: requires the borrow entries outlives a, which is false | entries[1]: requires the borrow entries outlives a, which is false")]
    [InlineData("func leak(value: uniq/i32 during a) -> uniq/i32 during a\n" + Entries + "    let c = entries[1]\n    return c\n", "return c: requires the borrow entries outlives a, which is false | c: requires the borrow entries outlives a, which is false")]
    [InlineData("func keep(value: uniq/i32 during a) -> uniq/i32 during a => value\nfunc leak(value: uniq/i32 during a) -> uniq/i32 during a\n" + Entries + "    return keep(entries[1])\n", "return keep(entries[1]): requires the borrow entries outlives a, which is false | keep(entries[1]): requires the borrow entries outlives a, which is false")]
    [InlineData("func leak(value: uniq/i32 during a) -> ref/i32 during a\n    let slot = value@move\n    let r = slot@ref\n    return r@follow@follow@ref\n", "return r@follow@follow@ref: requires the borrow r@follow@follow@ref outlives a, which is false | r@follow@follow@ref: requires the borrow r@follow@follow@ref outlives a, which is false")]
    public void ABorrowThroughLayersIsA5sRecordBesideBindingsTwin(string body, string records)
    {
        var source = body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.All(errors, static x => Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), x.Code));
        Assert.Equal(records, string.Join(" | ", errors.Select(x => $"{Text(source, x.Span)}: {x.Label}")));
    }

    // SPEC 15.6.1: a relation that a premise proves is no failure.
    [Theory]
    [InlineData("func f(x: ref/i32, y: ref/i32, c: bool) -> ref/i32 during x\n    origin y outlives x\n    return if c => x else => y\n")]
    [InlineData("func g(x: ref/i32, y: ref/i32) -> ref/i32 during x\n    origin y outlives x\n    return y\n")]
    [InlineData(Holder + "func pick(p: ref/Holder) -> ref/(ref/i32 during p) during p => p.item@ref\n")]
    [InlineData("func ok(x: ref/i32, y: ref/i32) -> ref/i32 during (x and y) => x\n")]
    [InlineData("func viaLocal(x: ref/i32) -> ref/i32 during x\n    let r = x\n    return r\n")]
    [InlineData("func f(b: ref/i32) -> i32\n    let rr = b@ref\n    return rr@follow@follow\n")]
    [InlineData("func f(p: ref/(ref/i32 during a) during b) -> ref/(ref/i32 during a) during b\n    return p@follow@ref\n")]
    // SPEC 4.7.1, 4.8.1: elements and reslices of a Slice depend on its source, also through a local, a loop or a Field; a reslice
    // of an array reached through a stored reference depends on that reference.
    [InlineData("func first(values: Slice<i32> during s) -> ref/i32 during s\n    let local = values\n    return local[0]@ref\n")]
    [InlineData("func tail(values: Slice<i32> during s) -> Slice<i32> during s\n    let local = values\n    return local[1..]\n")]
    [InlineData("func find(values: Slice<i32> during s) -> ref/i32 during s\n    for v in values\n        return v\n    return values[0]@ref\n")]
    [InlineData(Keep + "func f(values: Slice<i32> during s) -> i32\n    let kept = keep(values@ref)\n    return kept@follow[0]\n")]
    [InlineData("struct View {s}\n    public let items: Slice<i32> during s\nfunc tail(h: View during s) -> Slice<i32> during s\n    return h.items[1..]\n")]
    [InlineData("struct Rows {a}\n    public let r: ref/[3 of i32] during a\nfunc tail(h: Rows during a) -> Slice<i32> during a\n    return h.r[1..]\n")]
    // SPEC 15.6.3: a Place reached through a selected shared layer, a borrowed Slice handle or a shared reference, depends only on
    // that layer, its last shared dereference, whatever borrow reaches the layer.
    [InlineData("func first(values: Slice<i32> during s) -> ref/i32 during s\n    let r = values@ref\n    return r@follow[0]@ref\n")]
    [InlineData("func tail(values: Slice<i32> during s) -> Slice<i32> during s\n    let r = values@ref\n    return r@follow[1..]\n")]
    [InlineData("func first(values: Slice<i32> during s) -> ref/i32 during s\n    var local = values\n    let u = local@uniq\n    return u@follow[0]@ref\n")]
    [InlineData(Keep + "func first(values: Slice<i32> during s) -> ref/i32 during s\n    return keep(values)@follow[0]@ref\n")]
    [InlineData("struct Item\n    public let name: string\nfunc name(values: Slice<Item> during s) -> ref/string during s\n    let r = values@ref\n    return r@follow[0].name@ref\n")]
    [InlineData("func first(values: Slice<i32> during s) -> ref/i32 during s\n    match values\n        let v => return v@follow[0]@ref\n")]
    [InlineData("func tail(values: Slice<i32> during s) -> Slice<i32> during s\n    match values\n        let v => return v@follow[1..]\n")]
    [InlineData("func first(values: Slice<i32> during s) -> ref/i32 during s\n    let t = (values, 1)\n    match t\n        (let a, _) => return a@follow[0]@ref\n")]
    [InlineData("func first(view: ref/(Slice<i32> during s)) -> ref/i32 during s => view@follow[0]@ref\n")]
    [InlineData("func tail(view: ref/(Slice<i32> during s)) -> Slice<i32> during s => view@follow[1..]\n")]
    [InlineData("func first(p: ref/(ref/[3 of i32] during a) during b) -> ref/i32 during a => p@follow[0]@ref\n")]
    // SPEC 15.6.3, 15.6.1 (DECISIONS BU-27): the valid twins of ABorrowThroughLayersIsContainedInEach: each layer outlives the declared
    // Origin (a premise of a signature Type's well-formedness when it is an inner one), or the Borrow is not returned.
    [InlineData("func run(value: uniq/i32 during a) -> i32\n" + Entries + "    let found = entries[1]@follow@ref\n    return found@follow\n")]
    [InlineData("func run(left: uniq/i32 during a, right: uniq/i32 during a) -> i32\n    var first: Dictionary<i32, uniq/i32 during a> = [:]\n    var second: Dictionary<i32, uniq/i32 during a> = [:]\n    _ = first.tryInsert(1, left@move)\n    _ = second.tryInsert(1, right@move)\n    let found = first[1]@follow@ref\n    second[1]@follow = 99\n    return found@follow\n")]
    [InlineData("func keep(value: uniq/i32 during a) -> ref/i32 during a\n    let entries: [1 of uniq/i32 during a] = [value@move]\n    return entries[0]@follow@ref\n")]
    [InlineData("func get(entries: ref/Dictionary<i32, uniq/i32 during a> during e) -> ref/i32 during e\n    return entries[1]@follow@ref\n")]
    [InlineData("func elem(values: Slice<uniq/i32 during a> during s) -> ref/i32 during s\n    return values[0]@follow@ref\n")]
    [InlineData("func f(p: uniq/(uniq/i32 during a) during b) -> uniq/i32 during b\n    var q = p\n    return q@follow@follow@uniq\n")]
    [InlineData("func f(a: uniq/(uniq/i32 during x) during y) -> uniq/i32 during y\n    var cur = a@follow@uniq\n    return cur@follow@follow@uniq\n")]
    [InlineData(Holder2 + "func f(holder: uniq/(Holder2 during a) during h) -> uniq/i32 during h\n    return holder.link\n")]
    [InlineData(Box + "func f(h: objref/Box<uniq/i32 during a> during o) -> ref/i32 during o\n    return h.item@follow@ref\n")]
    [InlineData(Optional + "    public func get(self: uniq/Self during s) -> uniq/string during s\n        match self.o\n            .Some(let v) => return v\n            .None => $abort(\"none\")\n")]
    [InlineData("func f(value: uniq/Option<i32> during a) -> i32\n    var entries: Dictionary<i32, uniq/Option<i32> during a> = [1: value@move]\n    match entries[1]\n        .Some(let x) => return x\n        .None => return 0\n")]
    [InlineData("struct Borrowing<I> {source}\n    var iterator: uniq/I during source\n    public func next(self: uniq/Self during step) -> Option<I.(LendingIterator).LentItem(step)>\n        I is LendingIterator\n        return self.iterator.next()\n")]
    [InlineData("func takeFirst<S>(h: uniq/(uniq/S during s, i32)) -> Option<S.Item>\n    S is Iterator\n    var r = h@follow@uniq\n    let first = r.0.next()\n    h.1 = 5\n    return first@move\n")]
    // SPEC 3.5.1, 14.9.2: a stored `uniq/T` or `objuniq/T` acquired bare at an initializer, a selection's or `do`'s expression body or a
    // jump's operand without a fixed Target Result Type is Reborrowed, never read as a Copy value.
    [InlineData(Holder2 + "func f(holder: uniq/(Holder2 during a) during h, c: bool) -> uniq/i32 during h\n    let r = if c => holder.link else => holder.link\n    return r\n")]
    [InlineData(Holder2 + "func f(holder: uniq/(Holder2 during a) during h) -> uniq/i32 during h\n    let r = label l: do\n        exit to l holder.link\n    return r\n")]
    [InlineData(View + "func f(h: uniq/(H2 during a) during s) -> objuniq/P during s\n    var x = h.o\n    return x\n")]
    [InlineData(View + "func f(h: uniq/(H2 during a) during s, c: bool) -> objuniq/P during s\n    let r = if c => h.o else => h.o\n    return r\n")]
    [InlineData(View + "struct H3 {a}\n    public var o: objuniq/P during a\n    public var l: uniq/i32 during a\nfunc f(h: uniq/(H3 during a) during s, c: bool) -> i32\n    var x = h.o\n    let y = if c => h.o else => h.o\n    var u = h.l\n    let w = if c => h.l else => h.l\n    return 0\n")]
    // SPEC 3.5.1 (PLAN G256): a Dictionary literal's key and value are such positions too, so an unannotated literal takes the Reborrow's
    // Type, which a Closure's call receiver contains, as an Array element and an annotated literal do, inside a Closure and outside.
    [InlineData(Captured + "    var h = func [value] () -> ()\n        let d = [1: value]\n        _ = d@move\n    h()\n")]
    [InlineData(Captured + "    var h = func [value] () -> ()\n        let a = [value]\n        _ = a@move\n    h()\n")]
    [InlineData(Captured + "    var h = func [value] () -> ()\n        let d: Dictionary<i64, uniq/i32> = [1: value]\n        _ = d@move\n    h()\n")]
    [InlineData(Captured + "    let d = [1: value]\n    _ = d@move\n")]
    // A stored `objuniq/P` or `uniq/i32` reached through an exclusive parameter's Field is Reborrowed into a Dictionary literal as into an
    // array literal (formerly TransferRequired_Kd and UnprovenOriginRelation_Kd in the Dictionary).
    [InlineData(View + "func f(h: uniq/(H2 during a) during s) -> i32\n    let d = [1: h.o]\n    _ = d@move\n    return 0\n")]
    [InlineData(View + "func f(h: uniq/(H2 during a) during s) -> i32\n    let d = [h.o]\n    _ = d@move\n    return 0\n")]
    [InlineData(Holder2 + "func f(h: uniq/(Holder2 during a) during s) -> i32\n    let d = [1: h.link]\n    _ = d@move\n    return 0\n")]
    [InlineData(Holder2 + "func f(h: uniq/(Holder2 during a) during s) -> i32\n    let d = [h.link]\n    _ = d@move\n    return 0\n")]
    // SPEC 15.6.3, 22.2.1: a LendingIterator's item may name the receiver borrow `step` that contains the Reborrow.
    [InlineData("struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    let value: uniq/i32 during source\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step> => .Some(self.value)\n")]
    public void AProvenRelationIsAccepted(string body)
    {
        var output = DiagnosticCorpus.Check(body + Main);
        Assert.True(output.Accepted, string.Join("\n", output.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 14.2.2, 14.2.4, 15.6.3 (DECISIONS BU-98, PLAN G233): a discarded stored `uniq` or `objuniq` Place is Reborrowed by Binding's
    // name, the meet of the layers reaching it, which each contains, so a discard needs no Origin relation in any form a container or an
    // explicit discard takes it, with or without parentheses (their twins), through a Field, a follow, a Tuple position or an element; a
    // relation a fixed Type or a destination needs, and a conflict, keep their records (the invalid twins), and a false record beside a
    // kept one is gone. Each row: the body, its records' codes in order (empty when accepted).
    [Theory]
    [InlineData(Discarding + "    _ = h.link\n    h.link\n", "")]
    [InlineData(Discarding + "    (h.link)\n    _ = (h.link)\n", "")]
    [InlineData(Discarding + "    if c\n        h.link\n", "")]
    [InlineData(Discarding + "    if c => () else => h.link\n    let z = 0\n", "")]
    [InlineData(Discarding + "    match c\n        true => h.link\n        false => ()\n    let z = 0\n", "")]
    [InlineData(Discarding + "    while c\n        h.link\n", "")]
    [InlineData(Discarding + "    loop\n        h.link\n        exit\n", "")]
    [InlineData(Discarding + "    label w: do\n        h.link\n    label v: if c => h.link else => ()\n    let z = 0\n", "")]
    [InlineData(Discarding + "    do => h.link\n    do\n        (h.link)\n    let z = 0\n", "")]
    [InlineData(Discarding + "    defer\n        h.link\n    let z = 0\n", "")]
    [InlineData(Discarding + "    var g = func [h] () -> ()\n        h.link\n    g()\n", "")]
    [InlineData(Holder2 + "func f(h: uniq/(Holder2 during a) during s) -> () => h.link\n", "")]
    [InlineData("struct Holder4 {a}\n    public var link: uniq/i32 during a\n    public func touch(self: uniq/Self during b) -> () => self.link\n", "")]
    [InlineData("func f(p: uniq/(uniq/i32 during a) during s) -> ()\n    _ = p@follow\n    p@follow\n    (p@follow)\n", "")]
    [InlineData(View + "func f(h: uniq/(H2 during a) during s) -> ()\n    _ = h.o\n    h.o\n    (h.o)\n", "")]
    [InlineData("func f(p: uniq/(uniq/i32 during a, i32) during s) -> ()\n    p.0\n    _ = p.0\n", "")]
    [InlineData("struct Holder3 {a}\n    public var t: (uniq/i32 during a, i32)\nfunc f(h: uniq/(Holder3 during a) during s) -> ()\n    h.t.0\n    _ = h.t.0\n", "")]
    [InlineData("func f(p: uniq/(Array<uniq/i32 during a>) during s, q: uniq/([2 of uniq/i32 during a]) during s) -> ()\n    p[0]\n    _ = p[0]\n    (p[0])\n    q[0]\n    (q[0])\n", "")]
    [InlineData("func f(d: uniq/(Dictionary<i32, uniq/i32 during a>) during s) -> ()\n    _ = d[1]\n", "")]
    [InlineData(Discarding + "    origin s outlives a\n    _ = h.link\n    h.link\n", "")]
    [InlineData(Discarding + "    let x = h.link\n    _ = x\n", "")]
    [InlineData(Holder2 + "func id(x: uniq/i32 during b) -> uniq/i32 during b => x\nfunc f(h: uniq/(Holder2 during a) during s) -> ()\n    id(h.link)\n    _ = id(h.link)\n", "")]
    [InlineData("func f<s/T>(p: uniq/(s/T during a) during b) -> ()\n    s is uniq\n    p@follow\n    _ = p@follow\n    (p@follow)\n", "")]
    [InlineData(Discarding + "    unsafe\n        h.link\n    let z = 0\n", "UnnecessaryUnsafeBlock_Kd")]
    [InlineData(Discarding + "    let r = h.link\n    _ = h.link\n    r@follow = 1\n", "ComparisonLoanConflict_Kd")]
    [InlineData(Discarding + "    let r = h.link\n    h.link\n    r@follow = 1\n", "ComparisonLoanConflict_Kd")]
    [InlineData(Discarding + "    let r = h\n    _ = h.link\n    h.link\n    r.link@follow = 1\n", "ComparisonLoanConflict_Kd ComparisonLoanConflict_Kd")]
    [InlineData(Discarding + "    let g = h@move\n    _ = h.link\n    h.link\n", "MovedPlace_Kd MovedPlace_Kd")]
    [InlineData(Discarding + "    let x: uniq/i32 during a = h.link\n    _ = x\n", "UnprovenOriginRelation_Kd UnprovenOriginRelation_Kd")]
    [InlineData(Holder2 + "func f(h: uniq/(Holder2 during a) during s) -> uniq/i32 during a => h.link\n", "UnprovenOriginRelation_Kd")]
    [InlineData(Holder2 + "func f(h: uniq/(Holder2 during a) during s, g: uniq/(Holder2 during a) during t) -> ()\n    g.link = h.link\n", "UnprovenOriginRelation_Kd")]
    [InlineData("struct Holder5 {a}\n    public var link: uniq/i32 during a\n    public func store(self: uniq/Self during b, x: uniq/i32 during a) -> () => self.link = x\nfunc f(h: uniq/(Holder5 during a) during s, g: uniq/(Holder5 during a) during t) -> ()\n    g.store(h.link)\n    _ = g.store(h.link)\n", "UnprovenOriginRelation_Kd UnprovenOriginRelation_Kd")]
    [InlineData("func f(p: uniq/(uniq/i32 during a, i32) during s) -> uniq/i32 during a => p.0\n", "UnprovenOriginRelation_Kd")]
    [InlineData(Holder2 + "func f(h: uniq/(Holder2 during a) during s) -> uniq/i32 during a\n    return label w: do => h.link\n", "UnprovenOriginRelation_Kd")]
    [InlineData(Holder2 + "func f(h: ref/(Holder2 during a) during s) -> ()\n    _ = h.link\n    h.link\n", "TransferRequired_Kd TransferRequired_Kd")]
    public void ADiscardedReborrowNeedsNoRelation(string body, string codes)
    {
        var output = DiagnosticCorpus.Check(body + Main);
        Assert.Equal(codes, string.Join(" ", output.Diagnostics.Select(static x => x.Code).Order(StringComparer.Ordinal)));
    }

    // SPEC 15.6.1, 23.1: a Type occurrence whose nested Origin is not proven to outlive the outer one is a wellFormed record at
    // that occurrence (U-W, DECISIONS BU's entry), independent of the fit of the initializer, which is its own record: since the MIR
    // decides acceptance (H5, DECISIONS R5-M185) the fit is at the declaration (H3b T45: at `slot` before), so it is published first.
    [Fact]
    public void AWellFormednessRelationIsRecordedAtItsTypeOccurrence()
    {
        const string occurrence = "ref/(ref/i32 during b) during a";
        var source = "func f(a: ref/i32, b: ref/i32, slot: ref/(ref/i32 during a) during b) -> i32\n    let r: " + occurrence + " = slot\n    return 0\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.All(errors, static x => Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "requires b outlives a, which is not proven"), (x.Code, x.Label)));
        var wellFormed = Assert.Single(errors, x => x.Span!.Value.Start == source.IndexOf(occurrence, StringComparison.Ordinal));
        Assert.Equal((occurrence, "relation=outlives longer=b shorter=a source=wellFormed"), (Text(source, wellFormed.Span), Reason(wellFormed)));
        // SPEC 23.1: only the fit names its destination Type.
        Assert.Equal(
            [("let r: " + occurrence + " = slot", "relation=outlives longer=b shorter=a source=fit destination=ref/ref/i32 during a"), (occurrence, Reason(wellFormed))],
            errors.Select(x => (Text(source, x.Span), Reason(x))));

        static string Reason(CheckDiagnostic error) => string.Join(' ', error.Reason!.Select(static x => $"{x.Name}={x.Value}"));
    }

    // SPEC 15.3.2, 15.3.4, 15.6.1 (DECISIONS BU-26): a declaration's Type occurrence (a Field's, payload's or base's declared Type, a nested
    // Function Type part of a signature, which no premise covers) is judged by Binding from the premises visible at it: an unproven
    // relation is Binding's wellFormed record there, one per failing operand of a meet. The valid twin writes the premise (a clause or a
    // Semantics bound of the declaration) or, in a nested Function Type part, the Origin.
    [Theory]
    [InlineData("struct Pair {a, b}\n    public let x: ref/(ref/i32 during b) during a\n    public init(x: ref/(ref/i32 during b) during a) => self.x = x\n", "    public let", "    origin b outlives a\n    public let", "ref/(ref/i32 during b) during a", "self.b outlives self.a")]
    [InlineData("struct Pair {a, b, c}\n    public let x: ref/(ref/i32 during (b and c)) during a\n    public init(x: ref/(ref/i32 during (b and c)) during a) => self.x = x\n", "    public let", "    origin b outlives a\n    origin c outlives a\n    public let", "ref/(ref/i32 during (b and c)) during a", "self.b outlives self.a", "self.c outlives self.a")]
    [InlineData("enum E {a, b}\n    One(ref/(ref/i32 during b) during a)\n    Two\n", "    One", "    origin b outlives a\n    One", "ref/(ref/i32 during b) during a", "self.b outlives self.a")]
    [InlineData("open struct B<T>\n    public init() => ()\nstruct D {a, b} : B<ref/(ref/i32 during b) during a>\n    public init() : base() => ()\n", "    public init() :", "    origin b outlives a\n    public init() :", "ref/(ref/i32 during b) during a", "self.b outlives self.a")]
    [InlineData("open struct Base<T>\n    public init() => ()\nstruct Holder {a, b}\n    public let item: objref/Base<ref/i32 during a> during b\n", "    public let", "    origin a outlives b\n    public let", "objref/Base<ref/i32 during a> during b", "self.a outlives self.b")]
    [InlineData("public struct Box<E>\n    public let item: E\n    public init(item: E) => self.item = item@move\nstruct View<s/T> {a}\n    s is ref\n    public let b: ref/Box<s/T> during a\n", "s is ref", "s is owner", "ref/Box<s/T> during a", "the omitted Origin of s/T outlives self.a")]
    [InlineData("contract C\n    func f() -> (ref/i32) -> Option<ref/(ref/i32) during static>\n", "(ref/i32) during", "(ref/i32 during static) during", "ref/(ref/i32) during static", "the omitted Origin of ref/i32 outlives static")]
    [InlineData("func g0(q: ref/i32) -> Option<ref/(ref/i32 during static) during static>\n    return .None\nfunc get() -> (ref/i32) -> Option<ref/(ref/i32) during static>\n    return g0\n", "(ref/i32) during", "(ref/i32 during static) during", "ref/(ref/i32) during static", "the omitted Origin of ref/i32 outlives static")]
    public void ADeclarationsRelationIsBindingsRecordAtItsTypeOccurrence(string body, string written, string premise, string text, params string[] relations)
    {
        var source = body + Main;
        var record = Assert.Single(LegacyRecords.Of(MinimalEmissionTest.Analyze(source))); // A meet's further operands are published with it.
        Assert.Equal(DiagnosticRequirement.Binding(BindingFailure.OriginRelation), record.Requirement);
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(relations.Select(static x => $"requires {x}, which is not proven"), errors.Select(static x => x.Label));
        Assert.All(errors, x => Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), text, "wellFormed"), (x.Code, Text(source, x.Span), x.Reason![3].Value)));
        var twin = source.Replace(written, premise, StringComparison.Ordinal);
        Assert.True(DiagnosticCorpus.Check(twin).Accepted, twin);
    }

    // SPEC 23.1.2 (DECISIONS BU-26): a declaration's failed relation is an independent record beside a body's failed fit, and a declared
    // relation whose end its declaration left unbound rests on that end's MissingOriginBinding_Kd alone. The body's fit is Binding's record
    // at `r` and A5's twin at `return r` until H11 (R6-check H5, DECISIONS R5-M185; H0b §5.4, R4-M108; H3b T50).
    [Fact]
    public void ADeclarationsRelationStaysIndependentOfOtherRecords()
    {
        var source = "struct Pair {a, b}\n    public let x: ref/(ref/i32 during b) during a\n    public init(x: ref/(ref/i32 during b) during a) => self.x = x\n" + Bad + Main;
        Assert.Equal(
            [(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "ref/(ref/i32 during b) during a"), (nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "return r"), (nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "r")],
            DiagnosticCorpus.Check(source).Diagnostics.Select(x => (x.Code, Text(source, x.Span))));

        var unbound = "struct V<T> {source}\n    public let item: ref/T during source\nstruct S {a}\n    public let item: V<i32>{v}\n        origin v.source outlives a\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(unbound).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.MissingOriginBinding_Kd), "V<i32>{v}"), (error.Code, Text(unbound, error.Span)));
    }

    // Finite region bounds keep the actual Loan through annotations, scope exits and shared views of exclusive inputs.
    [Theory]
    [InlineData("struct H {s}\n    public let item: ref/string during s\n    public init(item: ref/string during s) => self.item = item\nfunc eat(n: string) -> () => ()\nfunc peek(n: ref/string) -> () => ()\nfunc f() -> ()\n    let v = \"v\"\n    let h = H.init(v@ref)\n    let w = \"w\"\n    let r: ref/string during h.s = w@ref\n    eat(w@move)\n    peek(r)\n", "w")]
    // A5 records w's destruction at its declaration (R6-check H5, DECISIONS R5-M185; H3b T44: formerly the `if` block's body) and
    // relates `keep`, whose next use lies past the block, at its declaration (the U-T2 amendment of DECISIONS R4-M57; DIAGNOSTICS A6).
    [InlineData("struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\nfunc f() -> ()\n    let v = 7\n    let h = H.init(v@ref)\n    var keep = h.item\n    if true\n        let w = 9\n        let r: ref/i32 during h.s = w@ref\n        keep = r\n    Console.writeLine(\"\\(keep@follow)\")\n", "let w = 9")]
    [InlineData("func f() -> ()\n    var x: i32 = 4\n    var y: i32 = 6\n    let r: ref/i32 = x@ref\n    let s: ref/i32 during r = y@ref\n    y = 7\n    Console.writeLine(\"\\(s@follow) \\(r@follow)\")\n", "y = 7")]
    // A fixed input over a local Borrow also loses the input's Loan: the write through `x` was accepted while `h` lived. A5 records the
    // write at its statement (R6-check H5, DECISIONS R5-M185; H3b T44: formerly at `x`).
    [InlineData("func g(x: uniq/i32) -> i32\n    let local: i32 = 1\n    let r = local@ref\n    let h: ref/i32 during r = x@follow@ref\n    x@follow = 5\n    return h@follow\n", "x@follow = 5")]
    public void AChainBetweenBodyOriginsRetainsTheSourceLoan(string body, string at)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        Assert.Equal(at, Text(source, error.Span));
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Contains(error.Related!, static x => x.Role == "loan");
    }

    // SPEC 15.4.4: the local's initializer completes its omitted Origin before a later anonymous header reads it.
    // Eager declaration-header binding used to retain the unresolved local slot and report UnsupportedOwnership.
    [Fact]
    public void AnAnnotatedLocalOriginCompletesBeforeTheClosureHeader()
    {
        var source = "public func main() -> ()\n    let x: i32 = 4\n    let r: ref/i32 = x@ref\n    let f = func (n: ref/i32 during r) -> i32 => n@follow\n    require f(x@ref) == 4 else => $abort(\"v5\")\n";
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Empty(errors);
        ScalarEmissionTest.EmitFixture("ClosureCompletedLocalOrigin", source, string.Empty);
    }

    [Fact]
    public void ACompletedLocalOriginStillKeepsTheArgumentLoan()
    {
        const string Source = "func change(n: uniq/i32) -> i32\n    n@follow = 7\n    return 0\npublic func main() -> ()\n    var x: i32 = 4\n    let r: ref/i32 = x@ref\n    let f = func (n: ref/i32 during r, ignored: i32) -> i32 => n@follow\n    let value = f(x@ref, change(x@uniq))\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.CallActivationConflict_Kd), error.Code);
    }

    // SPEC 7.7.1, 15.6.1: an anonymous function has no origin clauses, so only an Origin written on its input bounds that input.
    [Fact]
    public void AnAnonymousInputIsBoundByItsWrittenOrigin()
    {
        var source = "func use(x: ref/i32) -> ref/i32 during x\n    let start: ref/i32 = x\n    var inner = func [var start] (n: ref/i32 during x) -> ref/i32 during x\n" +
            "        let old = start\n        start = n\n        return old\n    var outer = func [var inner] (a: ref/i32) -> i32\n        let got = inner(a)\n" +
            "        return got@follow\n    return inner(x)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "a"), (error.Code, Text(source, error.Span)));
    }

    // DIAGNOSTICS.md rule 1: a failed relation leaves the Loan checks of the same body to proceed without it, so an independent Loan
    // conflict stays visible beside it. A5 records the local annotation's fit at its declaration (R6-check H5, DECISIONS R5-M185; H3b
    // T45: formerly at `y`).
    [Fact]
    public void AnIndependentLoanConflictStaysVisible()
    {
        var source = "func bad(x: ref/i32, y: ref/i32) -> i32\n    var n: i32 = 1\n    let s = n@ref\n    n = 2\n    Console.writeLine(\"\\(s@follow)\")\n    let r: ref/i32 during x = y\n    return r@follow\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.ComparisonLoanConflict_Kd), nameof(DiagnosticCode.UnprovenOriginRelation_Kd)], errors.Select(static x => x.Code));
        Assert.Equal(("n = 2", "let r: ref/i32 during x = y"), (Text(source, errors[0].Span), Text(source, errors[1].Span)));
    }

    // SPEC 13.5.6, 3.6: a Borrow of a Slice handle Loans the handle's storage: a `var` handle reassigned, or a call's temporary
    // ending, while the Borrow is live conflicts; A5 records a temporary's end at the temporary (R6-check H5, DECISIONS R5-M185; H3b T44:
    // formerly at its statement).
    [Theory]
    [InlineData("func f(a: Slice<i32> during s, b: Slice<i32> during s) -> i32\n    var values = a\n    let h = values@ref\n    values = b\n    return h@follow[0]\n", "values = b")]
    [InlineData(Make + "func f(values: Slice<i32> during s) -> i32\n    let r = make(values)@ref\n    return r@follow[0]\n", "make(values)")]
    public void ABorrowOfASliceHandleLoansItsStorage(string body, string at)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), at), (error.Code, Text(source, error.Span)));
    }

    // SPEC 23.3: the console names the borrow and relates it, the language server sends the record with its related location,
    // and the JSON form carries the display kind beside each Origin value. Binding's record at `r` has A5's twin at `return r`, published
    // before it, until H11 (R6-check H5, DECISIONS R5-M185; H0b §5.4, R4-M108; H3b T50).
    [Fact]
    public void EveryOutputCarriesTheOriginDisplay()
    {
        var output = DiagnosticCorpus.Check(Bad + Main);
        Assert.Equal(["return r", "r"], output.Diagnostics.Select(static x => Text(Bad + Main, x.Span)));
        var record = output.Diagnostics[1];
        var result = new DiagnosticResult(output.Diagnostics, output.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("UnsatisfiedOriginRelation_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("requires the borrow local@ref outlives x, which is false", console.Text, StringComparison.Ordinal);
        Assert.Contains("requires the borrow local outlives x, which is false", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = origin: ", console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(output.Sources[record.Source].Path);
        var plain = WorkspaceCheck.Place(output, [identity], identity, false)[identity];
        Assert.Equal(output.Diagnostics.Select(static x => (x.Code, x.Display!.Range)), plain.Select(static x => (x.Code, (SourceRange?)x.Range)));
        Assert.All(plain, static x => Assert.Null(x.RelatedInformation));
        var related = WorkspaceCheck.Place(output, [identity], identity, true)[identity];
        Assert.Equal(
            output.Diagnostics.Select(static x => string.Join(", ", x.Related!.Select(static y => y.Range))),
            related.Select(static x => string.Join(", ", x.RelatedInformation!.Select(static y => y.Location.Range))));

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"longer\",\"kind\":\"Origin\",\"value\":\"local@ref\",\"elided\":false,\"origin\":\"borrow\"}", json, StringComparison.Ordinal);
        Assert.Contains("{\"name\":\"shorter\",\"kind\":\"Origin\",\"value\":\"x\",\"elided\":false,\"origin\":\"expression\"}", json, StringComparison.Ordinal);
        Assert.Contains("{\"name\":\"relation\",\"kind\":\"Text\",\"value\":\"outlives\",\"elided\":false}", json, StringComparison.Ordinal);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;

    // An Origin-only failure's category by its code and its fit's Reason: the relation, its two Origin ends, the source and the destination.
    private static void AssertFitReason(CheckDiagnostic error)
    {
        Assert.Equal(error.Code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) ? DiagnosticCategory.Language : DiagnosticCategory.Proof, error.Category);
        var reason = error.Reason!;
        Assert.Equal(["relation", "longer", "shorter", "source", "destination"], reason.Select(static x => x.Name));
        Assert.Equal(("outlives", "fit"), (reason[0].Value, reason[3].Value));
        Assert.All(reason[1..3], static x => Assert.Equal(DiagnosticValueKind.Origin, x.Kind));
        Assert.All(reason.Where(static x => x.Kind != DiagnosticValueKind.Origin), static x => Assert.Null(x.Origin));
    }

    // A relation record as its primary, label, longer end (display kind and value) and related roles.
    private static string Relation(string source, CheckDiagnostic error)
        => $"{Text(source, error.Span)}: {error.Label}; {error.Reason![1].Origin} {error.Reason[1].Value}; " +
            (error.Related is null ? "none" : string.Join(", ", error.Related.Select(x => $"{x.Role} {Text(source, x.Span)}")));
}
