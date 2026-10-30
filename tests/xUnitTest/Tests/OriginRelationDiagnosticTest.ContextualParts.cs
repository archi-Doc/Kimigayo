// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests that the problem statement d3 adds (see OriginRelationDiagnosticTest.NestedSignatures.cs for d2's): each
// call proves the well-formedness of every instantiated parameter Type (SPEC 15.6.1, 15.6.4 step 3), also where a part of the argument
// takes its Type from the call: a nested generic call, a part of a Tuple, array or empty literal, a part inside a Closure body, a joined
// argument, a Case and a value call's argument. A record of an argument's well-formedness is at that argument; under D-1 (the owner,
// 2026-10-29, 「選択肢 (a)：引数に記録し、payload を関連位置に添える」) a call records a relation that a contextual Case's payload supplies at
// the argument it judges, with the payload as a related location. Each row names its form, states every Error record on both pipelines as
// `Code text` (AssertRecords) and has its twin.
public partial class OriginRelationDiagnosticTest
{
    private const string OwnH = "func h(q: ref/i32, r: ref/i32, x: Option<ref/(ref/i32 during q) during r>) -> ref/i32 during r\n    return q\n";

    private const string LocalUse = "func bad(a: ref/i32, b: ref/i32) -> i32\n";

    private const string MkNone = "func mkNone<T>() -> Option<T>\n    return .None\n";

    private const string IdFunc = "func id<T>(x: T) -> T => x@move\n";

    private const string WrapFunc = "func wrap<T>(x: Option<T>) -> Option<T> => x@move\n";

    private const string PairFunc = "func pair<T>(x: T) -> (T, i32) => (x@move, 1)\n";

    private const string PassStruct = "struct S\n    public init() => ()\n    public func pass<T>(self: ref/Self, x: T) -> T => x@move\n";

    private const string EmptyStruct = "struct E<T>\n    public init() => ()\n";

    private const string InnerE = "    func inner(p: E<Option<ref/(ref/i32 during q) during r>>, x: ref/i32 during q) -> ref/i32 during r => x\n";

    private const string InnerPair = "    func inner(p: (Option<ref/(ref/i32 during q) during r>, i32), x: ref/i32 during q) -> ref/i32 during r => x\n";

    private const string InnerArray = "    func inner(p: Array<Option<ref/(ref/i32 during q) during r>>, x: ref/i32 during q) -> ref/i32 during r => x\n";

    private const string InnerNested = "    func inner(p: Option<Option<ref/(ref/i32 during q) during r>>, x: ref/i32 during q) -> ref/i32 during r => x\n";

    private const string InnerTrailing = "    func inner(p: ref/i32 during q, o: Option<ref/(ref/i32 during q) during r>) -> ref/i32 during r\n        return p\n";

    private const string Put3 = "func put3(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (Option<ref/(ref/i32 during b) during a>, i32))\n    slot@follow = v\n";

    private const string Put4 = "func put4(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: [1 of Option<ref/(ref/i32 during b) during a>])\n    slot@follow = v\n";

    private const string Put5 = "func put5(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Array<ref/(ref/i32 during b) during a>)\n    slot@follow = v\n";

    private const string Put6 = "func put6(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: ((Option<ref/(ref/i32 during b) during a>, i32), i32))\n    slot@follow = v\n";

    private const string PutBox = "func putBox(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Box<Option<ref/(ref/i32 during b) during a>>)\n    slot@follow = v\n";

    // A body whose `short` ends before `keep` is read, which put3 may store into only through a relation b outlives a.
    private const string KeepShort = "func outer() -> i32\n    let long = 1\n    var keep = long@ref\n    if true\n        let short = 2\n        put3(keep@uniq, short@ref, (Option.None, 1))\n";

    private const string TwoCopies = "func use(p: ref/i32) -> i32\n    let a = p\n    let b = p\n";

    private const string CaseStore = "func g(x: Option<ref/i32 during q>, s: uniq/(Option<ref/i32 during q>)) -> ()\n    s@follow = x@move\n";

    private const string ClosureStore = "func use(p: uniq/(Option<ref/i32 during q>)) -> i32\n    let f = func [] (o: uniq/(Option<ref/i32 during q>)) -> ()\n        let short = 2\n";

    private const string JoinHead = "func k(q: ref/i32, r: ref/i32, c: bool) -> ref/i32 during r\n";

    private const string MatchNone = "match c\n        true => Option.None\n        false => Option.None\n";

    private const string WithPremise = "    origin q outlives r\n";

    private const string PayloadHead = "func k(q: ref/i32, r: ref/i32, v: ref/(ref/i32 during q) during r) -> ref/i32 during r\n";

    private const string ClauseCall = "struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n" +
        "func g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n";

    private const string WeakPayloadHead = "func k(q: ref/i32, r: ref/i32, v: ref/(ref/i32 during q) during q) -> ref/i32 during r\n";

    // Nested generic calls (d3 §1): a generic call nested in an argument takes its Type from the outer call, which proves the instantiated
    // well-formedness for its own solution, at the outer argument. h's Origins are its own (fresh at the call), so k3, d01, d04, i01, i02,
    // q10b, q14 and j01 are valid; put2's `a` is fixed by `p` and `b` by the Borrow of `short`, so each `_n` twin, k4 and j01n (whose nested
    // signature names k's fixed Origins) err once at the argument. ea1 has two records (D-3, the owner, relayed 2026-10-29: 「2 記録でよい」):
    // the inner call's at its contextual argument and the annotation's at its written Type. q10b's and q14's twins are q10 and q15 (the
    // aggregate theory).
    [Theory]
    [InlineData("k3", MkNone + OwnH + LocalUse + "    let x = h(a, b, mkNone())\n    return 0\n", "")]
    [InlineData("k4", MkNone + ImpliedStore + ShortBody + "    put2(p, sr, mkNone())\n    return 0\n", "UnsatisfiedOriginRelation_Kd mkNone()")]
    [InlineData("d01", IdFunc + OwnH + LocalUse + "    let x = h(a, b, id(Option.None))\n    return 0\n", "")]
    [InlineData("d01_n", ImpliedStore + IdFunc + ShortBody + "    put2(p, sr, id(Option.None))\n    return 0\n", "UnsatisfiedOriginRelation_Kd id(Option.None)")]
    [InlineData("d04", WrapFunc + OwnH + LocalUse + "    let x = h(a, b, wrap(Option.None))\n    return 0\n", "")]
    [InlineData("d04_n", ImpliedStore + WrapFunc + ShortBody + "    put2(p, sr, wrap(Option.None))\n    return 0\n", "UnsatisfiedOriginRelation_Kd wrap(Option.None)")]
    [InlineData("i01", LayerBox + "func h3(q: ref/i32, r: ref/i32, x: Box<Option<ref/(ref/i32 during q) during r>>) -> ref/i32 during r\n    return q\n" + LocalUse + "    let x = h3(a, b, Box.init(item: Option.None))\n    return 0\n", "")]
    [InlineData("i01_n", LayerBox + PutBox + ShortBody + "    putBox(p, sr, Box.init(item: Option.None))\n    return 0\n", "UnsatisfiedOriginRelation_Kd Box.init(item: Option.None)")]
    [InlineData("i02", PassStruct + OwnH + LocalUse + "    let s = S.init()\n    let x = h(a, b, s.pass(Option.None))\n    return 0\n", "")]
    [InlineData("i02_n", PassStruct + ImpliedStore + ShortBody + "    let s = S.init()\n    put2(p, sr, s.pass(Option.None))\n    return 0\n", "UnsatisfiedOriginRelation_Kd s.pass(Option.None)")]
    [InlineData("q10b", "func h(q: ref/i32, r: ref/i32, x: (Option<ref/(ref/i32 during q) during r>, i32)) -> ref/i32 during r\n    return q\n" + PairFunc + LocalUse + "    let x = h(a, b, pair(Option.None))\n    return 0\n", "")]
    [InlineData("q14", IdFunc + OwnH + LocalUse + "    let x = h(a, b, id(id(Option.None)))\n    return 0\n", "")]
    [InlineData("j01", EmptyStruct + "func h4(q: ref/i32, r: ref/i32, x: E<Option<ref/(ref/i32 during q) during r>>) -> ref/i32 during r\n    return q\n" + LocalUse + "    let x = h4(a, b, E.init())\n    return 0\n", "")]
    [InlineData("j01n", EmptyStruct + NestedHead + InnerE + "    return inner(E.init(), q)\n", "UnprovenOriginRelation_Kd E.init()")]
    [InlineData("j01n_ok", EmptyStruct + NestedHead + WithPremise + InnerE + "    return inner(E.init(), q)\n", "")]
    [InlineData("ea1", IdFunc + "func k(a: ref/i32, b: ref/i32) -> i32\n    let y: Option<ref/(ref/i32 during a) during b> = id(Option.None)\n    return 0\n", "UnprovenOriginRelation_Kd Option.None; UnprovenOriginRelation_Kd ref/(ref/i32 during a) during b")]
    [InlineData("ea2", IdFunc + "func k(a: ref/i32, b: ref/i32) -> i32\n    origin a outlives b\n    let y: Option<ref/(ref/i32 during a) during b> = id(Option.None)\n    return 0\n", "")]
    public void EachCallProvesItsInputsThroughANestedGenericCall(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // Contextual parts in aggregates (d3 §2; dev defect (i): dev never judges a named call's aggregate argument, so it accepts b01, b02 and
    // d09): a part of a Tuple, array or empty literal argument, nested or named, takes its Type from the call, which judges the whole
    // argument's instantiated well-formedness at that argument. d09's call adds `short`'s Loan to `keep`, read after `short` ends, so its
    // record is the Loan conflict at `short`. Each twin passes a value that outlives `q`, states the relation or reads before the end; the
    // same shape without a contextual part (t03) stays valid inside a Closure, as s04 does (ClosureEnvironmentOriginTest). cs2 is b11 and
    // q12 is vc2 (not repeated).
    [Theory]
    [InlineData("b01", Put3 + ShortBody + "    put3(p, sr, (Option.None, 1))\n    return 0\n", "UnsatisfiedOriginRelation_Kd (Option.None, 1)")]
    [InlineData("b01_ok", Put3 + LongBody + "    put3(p, v, (Option.None, 1))\n    return 0\n", "")]
    [InlineData("b02", Put4 + ShortBody + "    put4(p, sr, [Option.None])\n    return 0\n", "UnsatisfiedOriginRelation_Kd [Option.None]")]
    [InlineData("b02_ok", Put4 + LongBody + "    put4(p, v, [Option.None])\n    return 0\n", "")]
    [InlineData("b03", Put5 + ShortBody + "    put5(p, sr, [])\n    return 0\n", "UnsatisfiedOriginRelation_Kd []")]
    [InlineData("b03_ok", Put5 + LongBody + "    put5(p, v, [])\n    return 0\n", "")]
    [InlineData("b11", NestedHead + InnerPair + "    return inner((Option.None, 1), q)\n", "UnprovenOriginRelation_Kd (Option.None, 1)")]
    [InlineData("b11_ok", NestedHead + WithPremise + InnerPair + "    return inner((Option.None, 1), q)\n", "")]
    [InlineData("d09", Put3 + KeepShort + "    return keep@follow\n", "ComparisonLoanConflict_Kd let short = 2")]
    [InlineData("d09_ok", Put3 + KeepShort + "        let seen = keep@follow\n    return 0\n", "")]
    [InlineData("q06", Put6 + ShortBody + "    put6(p, sr, ((Option.None, 1), 2))\n    return 0\n", "UnsatisfiedOriginRelation_Kd ((Option.None, 1), 2)")]
    [InlineData("q06_ok", Put6 + LongBody + "    put6(p, v, ((Option.None, 1), 2))\n    return 0\n", "")]
    [InlineData("t03", "func same11<T>(x: [2 of (ref/T during c, i32)]) => ()\n" + TwoCopies + "    let f = func [a, b] () -> ()\n        same11([(a@ref, 1), (b@ref, 1)])\n    f()\n    return 0\n", "")]
    [InlineData("q05", ImpliedStore + ShortBody + "    put2(slot: p, v: sr, w: Option.None)\n    return 0\n", "UnsatisfiedOriginRelation_Kd Option.None")]
    [InlineData("q05_ok", ImpliedStore + LongBody + "    put2(slot: p, v: v, w: Option.None)\n    return 0\n", "")]
    [InlineData("q10", Put3 + PairFunc + ShortBody + "    put3(p, sr, pair(Option.None))\n    return 0\n", "UnsatisfiedOriginRelation_Kd pair(Option.None)")]
    [InlineData("q15", ImpliedStore + IdFunc + ShortBody + "    put2(p, sr, id(id(Option.None)))\n    return 0\n", "UnsatisfiedOriginRelation_Kd id(id(Option.None))")]
    public void AContextualPartOfAnAggregateArgumentIsJudged(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // Inside Closures (d3 §3): r03 to r06 (`.Some(a@ref)`, `swap3(.Some(a@uniq), b@uniq)`, `(a@ref, .None)`, `[.Some(a@ref), .None]`) are
    // valid and stay ClosureEnvironmentOriginTest's rows (not repeated). Their twins store a Closure local's Borrow toward the enclosing `q`
    // through the same shapes: a Refuted relation. A Case whose expected Type the parameter fixes (r03_n, r04_n, r06_n) is judged by its own
    // fit, at the payload; only a position the call opens records at the argument (r05_n's `.None`; D-1's scope, decided 2026-10-29).
    [Theory]
    [InlineData("r03_n", CaseStore + ClosureStore + "        g(.Some(short@ref), o)\n    f(p)\n    return 0\n", "UnsatisfiedOriginRelation_Kd short@ref", null)]
    [InlineData("r04_n", "func g4(x: Option<uniq/i32 during q>, s: uniq/(Option<uniq/i32 during q>)) -> ()\n    s@follow = x@move\nfunc use(p: uniq/(Option<uniq/i32 during q>)) -> i32\n    let f = func [] (o: uniq/(Option<uniq/i32 during q>)) -> ()\n        var short = 2\n        g4(.Some(short@uniq), o)\n    f(p)\n    return 0\n", "UnsatisfiedOriginRelation_Kd short@uniq", null)]
    [InlineData("r05_n", "func put7(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (ref/i32 during b, Option<ref/(ref/i32 during b) during a>))\n    slot@follow = v\nfunc use(p: uniq/(ref/i32 during q)) -> i32\n    let f = func [] (s: uniq/(ref/i32 during q)) -> ()\n        let short = 2\n        put7(s, short@ref, (short@ref, .None))\n    f(p)\n    return 0\n", "UnsatisfiedOriginRelation_Kd (short@ref, .None)", null)]
    [InlineData("r06_n", "func put8(x: [2 of Option<ref/i32 during q>], s: uniq/(Option<ref/i32 during q>)) => ()\n" + ClosureStore + "        put8([.Some(short@ref), .None], o)\n    f(p)\n    return 0\n", "UnsatisfiedOriginRelation_Kd short@ref", null)]
    public void AContextualPartInsideAClosureIsJudged(string form, string body, string records, string? payload)
        => AssertRecordsRelating(form, body, records, payload);

    // Joins (d3 §4): the sources of an `if` or `match` argument or initializer form one value, judged once. jt1_ok, mj4 and cv4 (whose `t`
    // gives the relation by its signature's well-formedness) are valid; jt1 errs at the argument and mj4_n at its annotation only. A `match`
    // argument whose sources are contextual Cases waits for its call as an `if` one does (R4-M155, B5's join rule): mj1 errs at the
    // argument as jt1 does and mj1_ok is valid (both were dev's located Unsupported_Kd with the call's dependent record).
    [Theory]
    [InlineData("jt1_ok", JoinHead + WithPremise + InnerPair + "    return inner((if c => Option.None else => Option.None, 1), q)\n", "")]
    [InlineData("jt1", JoinHead + InnerPair + "    return inner((if c => Option.None else => Option.None, 1), q)\n", "UnprovenOriginRelation_Kd (if c => Option.None else => Option.None, 1)")]
    [InlineData("mj4", JoinHead + WithPremise + "    let t: Option<ref/(ref/i32 during q) during r> = " + MatchNone + NestedInner + "    return inner(t, q)\n", "")]
    [InlineData("mj4_n", JoinHead + "    let t: Option<ref/(ref/i32 during q) during r> = " + MatchNone + NestedInner + "    return inner(t, q)\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("cv4", "func k(q: ref/i32, r: ref/i32, c: bool, t: (Option<ref/(ref/i32 during q) during r>, i32)) -> ref/i32 during r\n" + InnerPair + "    return inner(if c => t else => (Option.None, 1), q)\n", "")]
    [InlineData("mj1", JoinHead + NestedInner + "    return inner((" + MatchNone + "    ), q)\n", "UnprovenOriginRelation_Kd (match c")]
    [InlineData("mj1_ok", JoinHead + WithPremise + NestedInner + "    return inner((" + MatchNone + "    ), q)\n", "")]
    public void AJoinedArgumentIsJudgedAsOneValue(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // Explicit qualifiers and Cases with payload (d3 §6): `Option.Some(v)` types itself from `v`, whose signature Type gives q outlives r
    // (cv1 to cv3 valid); with `v` during q the argument's fit fails once at the argument. cs1's contextual inner `Option.None` is judged at
    // the argument; when an annotation settles the construction (cs1_ty), its contextual payload and the annotation each have a record (D-3's
    // analogue). cr5's Case takes a Type its parameter fixes, so the relation is the Case's own fit, recorded at the payload as cr6's
    // assignment and dev do; D-1 covers only relations the call judges at a position it opens (decided 2026-10-29). cp1 is cs1 (not repeated).
    [Theory]
    [InlineData("cv1", PayloadHead + NestedInner + "    return inner(Option.Some(v), q)\n", "", null)]
    [InlineData("cv1_n", WeakPayloadHead + NestedInner + "    return inner(Option.Some(v), q)\n", "UnprovenOriginRelation_Kd Option.Some(v)", null)]
    [InlineData("cv2", PayloadHead + InnerPair + "    return inner((Option.Some(v), 1), q)\n", "", null)]
    [InlineData("cv2_n", WeakPayloadHead + InnerPair + "    return inner((Option.Some(v), 1), q)\n", "UnprovenOriginRelation_Kd (Option.Some(v), 1)", null)]
    [InlineData("cv3", PayloadHead + InnerArray + "    return inner([Option.Some(v), Option.None], q)\n", "", null)]
    [InlineData("cv3_n", WeakPayloadHead + InnerArray + "    return inner([Option.Some(v), Option.None], q)\n", "UnprovenOriginRelation_Kd [Option.Some(v), Option.None]", null)]
    [InlineData("cs1", NestedHead + InnerNested + "    return inner(Option.Some(Option.None), q)\n", "UnprovenOriginRelation_Kd Option.Some(Option.None)", null)]
    [InlineData("cs1_ok", NestedHead + WithPremise + InnerNested + "    return inner(Option.Some(Option.None), q)\n", "", null)]
    [InlineData("cs1_ty", NestedHead + "    let t: Option<Option<ref/(ref/i32 during q) during r>> = Option.Some(Option.None)\n" + InnerNested + "    return inner(t, q)\n", "UnprovenOriginRelation_Kd Option.None; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r", null)]
    [InlineData("cr5", CaseStore + "func bad(p: uniq/(Option<ref/i32 during q>)) -> i32\n    let short = 2\n    g(.Some(short@ref), p)\n    return 0\n", "UnsatisfiedOriginRelation_Kd short@ref", null)]
    [InlineData("cr5_ok", CaseStore + "func bad(p: uniq/(Option<ref/i32 during q>), v: ref/i32 during q) -> i32\n    g(.Some(v), p)\n    return 0\n", "", null)]
    [InlineData("cr6", "func bad(p: uniq/(Option<ref/i32 during q>)) -> i32\n    let short = 2\n    p@follow = .Some(short@ref)\n    return 0\n", "UnsatisfiedOriginRelation_Kd short@ref", null)]
    [InlineData("cr6_ok", "func good(p: uniq/(Option<ref/i32 during q>), v: ref/i32 during q) -> i32\n    p@follow = .Some(v)\n    return 0\n", "", null)]
    public void AnArgumentsCaseIsJudgedAtTheArgument(string form, string body, string records, string? payload)
        => AssertRecordsRelating(form, body, records, payload);

    // Contextual parts of value calls (d3 §7; the owner's (A)): a value call proves its callee's nested input well-formedness for the
    // instantiated Types at the argument, as a named call does; each twin states the relation. k6 is vc1 (not repeated).
    [Theory]
    [InlineData("vc1", "func k(q: ref/i32, r: ref/i32, g: (Option<ref/(ref/i32 during q) during r>) -> i32) -> i32\n    return g(Option.None)\n", "UnprovenOriginRelation_Kd Option.None")]
    [InlineData("vc1_ok", "func k(q: ref/i32, r: ref/i32, g: (Option<ref/(ref/i32 during q) during r>) -> i32) -> i32\n" + WithPremise + "    return g(Option.None)\n", "")]
    [InlineData("vc2", "func k(q: ref/i32, r: ref/i32, g: ((Option<ref/(ref/i32 during q) during r>, i32)) -> i32) -> i32\n    return g((Option.None, 1))\n", "UnprovenOriginRelation_Kd (Option.None, 1)")]
    [InlineData("vc2_ok", "func k(q: ref/i32, r: ref/i32, g: ((Option<ref/(ref/i32 during q) during r>, i32)) -> i32) -> i32\n" + WithPremise + "    return g((Option.None, 1))\n", "")]
    public void AValueCallProvesItsCalleesNestedInputs(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // One-record pins (d3 §8): a contextual `.None` after another argument is one record at it, never a second one at the call or at `q`;
    // n7h2, p1b and n7g2's Loan conflict are ALocatedRecordThatDoesNotFailBindingIsReported's rows.
    [Theory]
    [InlineData("nn1t", NestedHead + WithPremise + InnerTrailing + "    return inner(q, Option.None)\n", "")]
    [InlineData("nn1t_n", NestedHead + InnerTrailing + "    return inner(q, Option.None)\n", "UnprovenOriginRelation_Kd Option.None")]
    public void AOneRecordFormKeepsOneRecord(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // Probes 2 and 3 (d3 §4.9): j02 wraps put2's position in E's Type argument (as j01n does k's) and errs at the argument (dev accepts it);
    // a Case construction's payload Type that another payload instantiates must be well-formed (cx2 errs at `.None`, dev accepts it), while
    // cx1's slot `s` takes the meet of its upper bounds, the third payload's well-formedness among them (SPEC 15.3.6), so cx1 is valid, as
    // are cx5 (whose clause `a outlives s` holds by that meet), its call analogue cxc and cxc's concrete twin cxc2 (probe 4), and cxc's
    // written twins r4i, r4a, r4b, r4e and r4f, where a written argument's parameter well-formedness, slots substituted, is a bound too
    // (design revision 2 §2): each `_n` twin returns `x` at `during p`, which the meet's `short@ref` operand cannot outlive; a part's
    // record lies at the argument of the call whose solution determines its Type (DECISIONS R4-M155, §3): `.None` whose Type mk's own
    // parameter fixes, also inside take's argument (lift1, lift2, lift3), a payload whose position mk fixes completely at the payload
    // (open1, open2), a deferred call's part at the outer argument for a value call as for a named one (bv3/bn3, and as a Tuple's element
    // bv4/bn4), and where the waiting call takes the position from its own evidence, at its own argument (mk4, whose position mk4 opens);
    // t7c_ok's clause follows from k's premise; fn0's Origins without evidence are local regions; dl1's contextual payload supplies two
    // failing positions, each at the argument with the payload related (D-1). j01f is j01 (not repeated); t7c has its own theory below.
    [Theory]
    [InlineData("j02", EmptyStruct + "func put8(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: E<Option<ref/(ref/i32 during b) during a>>)\n    slot@follow = v\n" + ShortBody + "    put8(p, sr, E.init())\n    return 0\n", "UnsatisfiedOriginRelation_Kd E.init()", null)]
    [InlineData("j02_ok", EmptyStruct + "func put8(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: E<Option<ref/(ref/i32 during b) during a>>)\n    slot@follow = v\n" + LongBody + "    put8(p, v, E.init())\n    return 0\n", "", null)]
    [InlineData("t7c_ok", ClauseCall + "    origin p outlives static\n    return g(.None, p)\n", "", null)]
    [InlineData("cx1", "enum W<T> {s}\n    Make(T, ref/i32 during s, Option<ref/T during s>)\n    Empty\nfunc bad(p: ref/i32) -> i32\n    let short = 2\n    let w = W.Make(short@ref, p, .None)\n    return 0\n", "", null)]
    [InlineData("cx1_ok", "enum W<T> {s}\n    Make(T, ref/i32 during s, Option<ref/T during s>)\n    Empty\nfunc ok(p: ref/i32) -> i32\n    let short = 2\n    let sr = short@ref\n    let w = W.Make(p, sr, .None)\n    return 0\n", "", null)]
    [InlineData("cx5", "enum W3 {a, s}\n    origin a outlives s\n    Make(ref/i32 during a, ref/i32 during s, Option<ref/(ref/i32 during a) during s>)\n    Empty\nfunc ok(p: ref/i32) -> i32\n    let short = 2\n    let w = W3.Make(short@ref, p, .None)\n    return 0\n", "", null)]
    [InlineData("cxc", "func mk<T>(t: T, x: ref/i32 during s, w: Option<ref/T during s>) -> i32\n    return 0\nfunc ok(p: ref/i32) -> i32\n    let short = 2\n    return mk(short@ref, p, .None)\n", "", null)]
    [InlineData("cxc2", "func mk(t: ref/i32 during a, x: ref/i32 during s, w: Option<ref/(ref/i32 during a) during s>) -> i32\n    return 0\nfunc ok(p: ref/i32) -> i32\n    let short = 2\n    return mk(short@ref, p, .None)\n", "", null)]
    [InlineData("r4i", "func mk<T>(t: T, x: ref/i32 during s, w: Option<ref/T during s>) -> i32\n    return 0\nfunc ok(p: ref/i32, w: Option<ref/(ref/i32 during p) during p>) -> i32\n    let short = 2\n    return mk(short@ref, p, w)\n", "", null)]
    [InlineData("r4i_n", "func mk<T>(t: T, x: ref/i32 during s, w: Option<ref/T during s>) -> ref/i32 during s\n    return x\nfunc bad(p: ref/i32, w: Option<ref/(ref/i32 during p) during p>) -> ref/i32 during p\n    let short = 2\n    return mk(short@ref, p, w)\n", "UnsatisfiedOriginRelation_Kd mk(short@ref, p, w)", null)]
    [InlineData("r4a", "func mk<T>(w: Option<ref/T during s>, t: T, x: ref/i32 during s) -> i32\n    return 0\nfunc ok(p: ref/i32, w: Option<ref/(ref/i32 during p) during p>) -> i32\n    let short = 2\n    return mk(w, short@ref, p)\n", "", null)]
    [InlineData("r4a_n", "func mk<T>(w: Option<ref/T during s>, t: T, x: ref/i32 during s) -> ref/i32 during s\n    return x\nfunc bad(p: ref/i32, w: Option<ref/(ref/i32 during p) during p>) -> ref/i32 during p\n    let short = 2\n    return mk(w, short@ref, p)\n", "UnsatisfiedOriginRelation_Kd mk(w, short@ref, p)", null)]
    [InlineData("r4b", "func mk<T>(t: T, w: Option<ref/T during s>, x: ref/i32 during s) -> i32\n    return 0\nfunc ok(p: ref/i32, w: Option<ref/(ref/i32 during p) during p>) -> i32\n    let short = 2\n    return mk(short@ref, w, p)\n", "", null)]
    [InlineData("r4b_n", "func mk<T>(t: T, w: Option<ref/T during s>, x: ref/i32 during s) -> ref/i32 during s\n    return x\nfunc bad(p: ref/i32, w: Option<ref/(ref/i32 during p) during p>) -> ref/i32 during p\n    let short = 2\n    return mk(short@ref, w, p)\n", "UnsatisfiedOriginRelation_Kd mk(short@ref, w, p)", null)]
    [InlineData("r4e", "func mk<T>(w: ref/T during s, t: T, x: ref/i32 during s) -> i32\n    return 0\nfunc ok(p: ref/i32, w: ref/(ref/i32 during p) during p) -> i32\n    let short = 2\n    return mk(w, short@ref, p)\n", "", null)]
    [InlineData("r4e_n", "func mk<T>(w: ref/T during s, t: T, x: ref/i32 during s) -> ref/i32 during s\n    return x\nfunc bad(p: ref/i32, w: ref/(ref/i32 during p) during p) -> ref/i32 during p\n    let short = 2\n    return mk(w, short@ref, p)\n", "UnsatisfiedOriginRelation_Kd mk(w, short@ref, p)", null)]
    [InlineData("r4f", "func mk<T>(t: T, w: ref/T during s, x: ref/i32 during s) -> i32\n    return 0\nfunc ok(p: ref/i32, w: ref/(ref/i32 during p) during p) -> i32\n    let short = 2\n    return mk(short@ref, w, p)\n", "", null)]
    [InlineData("r4f_n", "func mk<T>(t: T, w: ref/T during s, x: ref/i32 during s) -> ref/i32 during s\n    return x\nfunc bad(p: ref/i32, w: ref/(ref/i32 during p) during p) -> ref/i32 during p\n    let short = 2\n    return mk(short@ref, w, p)\n", "UnsatisfiedOriginRelation_Kd mk(short@ref, w, p)", null)]
    [InlineData("lift1", "func k(q: ref/i32, r: ref/i32) -> i32\n    func mk<T>(p: Option<ref/(ref/i32 during q) during r>) -> Option<T>\n        return .None\n    func take(v: Option<i32>) -> i32\n        return 0\n    return take(mk(.None))\n", "UnprovenOriginRelation_Kd .None", null)]
    [InlineData("lift2", "func k(q: ref/i32, r: ref/i32) -> i32\n    func mk<T>(p: Option<ref/(ref/i32 during q) during r>) -> Option<T>\n        return .None\n    let v: Option<i32> = mk(.None)\n    return 0\n", "UnprovenOriginRelation_Kd .None", null)]
    [InlineData("lift3", "func k(q: ref/i32, r: ref/i32) -> i32\n    func mk(p: Option<ref/(ref/i32 during q) during r>) -> Option<i32>\n        return .None\n    func take(v: Option<i32>) -> i32\n        return 0\n    return take(mk(.None))\n", "UnprovenOriginRelation_Kd .None", null)]
    [InlineData("open1", "func k(q: ref/i32) -> i32\n    func mk<T>(p: Option<ref/i32 during q>) -> Option<T>\n        return .None\n    func take(v: Option<ref/i32 during c>) -> i32\n        return 0\n    let short = 2\n    return take(mk(.Some(short@ref)))\n", "UnsatisfiedOriginRelation_Kd short@ref", null)]
    [InlineData("open2", "func k(q: ref/i32) -> i32\n    func mk<T>(p: Option<ref/i32 during q>) -> Option<T>\n        return .None\n    let short = 2\n    let v: Option<ref/i32> = mk(.Some(short@ref))\n    return 0\n", "UnsatisfiedOriginRelation_Kd short@ref", null)]
    [InlineData("bv3", "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a>)\n    slot@follow = v\nfunc put4(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (Option<ref/(ref/i32 during b) during a>, i32))\n    slot@follow = v\nfunc id<T>(x: T) -> T => x@move\nfunc bad(p: uniq/(ref/i32 during a), v: ref/i32 during b, g: (uniq/(ref/i32 during a), ref/i32 during b, Option<ref/(ref/i32 during b) during a>) -> ()) -> i32\n    g(p, v, id(Option.None))\n    return 0\n", "UnprovenOriginRelation_Kd id(Option.None)", null)]
    [InlineData("bn3", "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a>)\n    slot@follow = v\nfunc put4(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (Option<ref/(ref/i32 during b) during a>, i32))\n    slot@follow = v\nfunc id<T>(x: T) -> T => x@move\nfunc bad(p: uniq/(ref/i32 during a), v: ref/i32 during b) -> i32\n    put2(p, v, id(Option.None))\n    return 0\n", "UnprovenOriginRelation_Kd id(Option.None)", null)]
    [InlineData("bv4", "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a>)\n    slot@follow = v\nfunc put4(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (Option<ref/(ref/i32 during b) during a>, i32))\n    slot@follow = v\nfunc id<T>(x: T) -> T => x@move\nfunc bad(p: uniq/(ref/i32 during a), v: ref/i32 during b, g: (uniq/(ref/i32 during a), ref/i32 during b, (Option<ref/(ref/i32 during b) during a>, i32)) -> ()) -> i32\n    g(p, v, (id(Option.None), 1))\n    return 0\n", "UnprovenOriginRelation_Kd (id(Option.None), 1)", null)]
    [InlineData("bn4", "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a>)\n    slot@follow = v\nfunc put4(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (Option<ref/(ref/i32 during b) during a>, i32))\n    slot@follow = v\nfunc id<T>(x: T) -> T => x@move\nfunc bad(p: uniq/(ref/i32 during a), v: ref/i32 during b) -> i32\n    put4(p, v, (id(Option.None), 1))\n    return 0\n", "UnprovenOriginRelation_Kd (id(Option.None), 1)", null)]
    [InlineData("mk4", "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a>)\n    slot@follow = v\nfunc put4(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (Option<ref/(ref/i32 during b) during a>, i32))\n    slot@follow = v\nfunc id<T>(x: T) -> T => x@move\nfunc mk4<T, U>(a: T, b: Option<T>, c: Option<U>) -> Option<U> => c@move\nfunc bad(p: uniq/(ref/i32 during q), x: ref/i32 during q) -> i32\n    let short = 2\n    let sr = short@ref\n    put2(p, sr, mk4(x, .Some(short@ref), .None))\n    return 0\n", "UnsatisfiedOriginRelation_Kd .Some(short@ref)", "short@ref")]
    [InlineData("mk4_c", "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a>)\n    slot@follow = v\nfunc put4(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: (Option<ref/(ref/i32 during b) during a>, i32))\n    slot@follow = v\nfunc id<T>(x: T) -> T => x@move\nfunc mk4<T, U>(a: T, b: Option<T>, c: Option<U>) -> Option<U> => c@move\nfunc bad(p: uniq/(ref/i32 during q), x: ref/i32 during q) -> i32\n    let short = 2\n    let sr = short@ref\n    put2(p, x, mk4(x, .Some(x), .None))\n    return 0\n", "", null)]
    [InlineData("cx2", "enum W<T>\n    Make(T, Option<ref/T during static>)\n    Empty\nfunc bad() -> i32\n    let short = 2\n    let w = W.Make(short@ref, .None)\n    return 0\n", "UnsatisfiedOriginRelation_Kd .None", null)]
    [InlineData("cx2_ok", "enum W<T>\n    Make(T, Option<ref/T during static>)\n    Empty\nfunc ok(p: ref/i32) -> i32\n    origin p outlives static\n    let w = W.Make(p, .None)\n    return 0\n", "", null)]
    [InlineData("fn0", "func f(w: Option<ref/(ref/i32 during c) during d>) -> i32\n    return 0\nfunc g() -> i32\n    return f(.None)\n", "", null)]
    [InlineData("dl1", ImpliedStore + "func bad(p: uniq/(ref/i32 during q), v: ref/i32 during q) -> i32\n    let short = 2\n    let sr = short@ref\n    let ys = sr@ref\n    put2(p, v, .Some(ys))\n    return 0\n", "UnsatisfiedOriginRelation_Kd .Some(ys); UnsatisfiedOriginRelation_Kd .Some(ys)", "ys")]
    [InlineData("dl1_ok", ImpliedStore + "func good(p: uniq/(ref/i32 during q), v: ref/i32 during q, ys: ref/(ref/i32 during q) during q) -> i32\n    put2(p, v, .Some(ys))\n    return 0\n", "", null)]
    public void EachCallAndConstructionProvesItsInstantiatedTypes(string form, string body, string records, string? payload)
        => AssertRecordsRelating(form, body, records, payload);

    // t7c (d3 §6; the planning session's decision, by D-3's reasoning): T's own clause in x's instantiated Type fails (`p outlives static`
    // is Unknown) and is required as an Origin relation record at the argument `.None` (DIAGNOSTICS: source `declared`); SPEC 23.1.2 leaves
    // grouping implementation-defined, so at most one further record is allowed and none beyond. Its twin is t7c_ok above.
    [Theory]
    [InlineData("t7c", ClauseCall + "    return g(.None, p)\n")]
    public void AnArgumentsTypeClauseIsRecordedAtTheArgument(string form, string body)
    {
        var source = body + Main;
        CompilationTestHelper.ParseSuccess(source, "Hello.kimi");
        foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
        {
            var errors = ContextualCaptureTest.PublishedRecords(source, pipeline).Where(static x => x.Severity == DiagnosticSeverity.Error)
                .Select(static x => $"{x.Code} {(x.Text ?? string.Empty).Split('\n')[0]}").ToArray();
            Assert.True(errors.Contains("UnprovenOriginRelation_Kd .None") && errors.Length <= 2, $"{form} ({pipeline}): expected UnprovenOriginRelation_Kd .None and at most one further record, found [{string.Join("; ", errors)}]");
        }
    }

    // AssertRecords, and for a contextual payload (`payload` not null) D-1's related location: every Error record relates the payload's
    // text.
    // SPEC 10.5.1, 14.9.2 (DECISIONS R4-M155, BU-43; BU review 2 B5): a join with a result source whose kind is Waiting (a contextual
    // Case, a function reference, an anonymous function) waits for its call, by the one classifier's recorded kinds, on a value call (jv)
    // and a named call (jn) alike; the call completes it at the selected parameter Type (BU's three forms and their named twins, which
    // failed on dev). The _n twins have a source that fits no parameter: its own record, or at a named call the call's record (d3).
    [Theory]
    [InlineData("jv1", "func k(c: bool, g: (Option<i32>) -> i32) -> i32\n    return g(if c => .None else => .Some(1))\n", "")]
    [InlineData("jv2", "func k(c: bool, g: (Option<i32>) -> i32) -> i32\n    return g(if c => Option.None else => .Some(1))\n", "")]
    [InlineData("jv3", "func one(x: i32) -> i32\n    return x\nfunc two(x: i32) -> i32\n    return x + 1\nfunc k(c: bool, g: ((i32) -> i32) -> i32) -> i32\n    return g(if c => one else => two)\n", "")]
    [InlineData("jv1_n", "func k(c: bool, g: (Option<i32>) -> i32) -> i32\n    return g(if c => .None else => .Some(true))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("jv3_n", "func one(x: i32) -> i32\n    return x\nfunc two(x: i32) -> i32\n    return x + 1\nfunc three(x: i64) -> i32\n    return 0\nfunc k(c: bool, g: ((i32) -> i32) -> i32) -> i32\n    return g(if c => one else => three)\n", "TypeMismatch_Kd three")]
    [InlineData("jn1", "func h(o: Option<i32>) -> i32\n    return 0\nfunc k(c: bool) -> i32\n    return h(if c => .None else => .Some(1))\n", "")]
    [InlineData("jn2", "func h(o: Option<i32>) -> i32\n    return 0\nfunc k(c: bool) -> i32\n    return h(if c => Option.None else => .Some(1))\n", "")]
    [InlineData("jn3", "func one(x: i32) -> i32\n    return x\nfunc two(x: i32) -> i32\n    return x + 1\nfunc ap(f: (i32) -> i32) -> i32\n    return f(1)\nfunc k(c: bool) -> i32\n    return ap(if c => one else => two)\n", "")]
    [InlineData("jn1_n", "func h(o: Option<i32>) -> i32\n    return 0\nfunc k(c: bool) -> i32\n    return h(if c => .None else => .Some(true))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("jn3_n", "func one(x: i32) -> i32\n    return x\nfunc two(x: i32) -> i32\n    return x + 1\nfunc three(x: i64) -> i32\n    return 0\nfunc ap(f: (i32) -> i32) -> i32\n    return f(1)\nfunc k(c: bool) -> i32\n    return ap(if c => one else => three)\n", "NoApplicableOverload_Kd ap(if c => one else => three)")]
    public void AJoinWithAWaitingSourceWaitsForItsCall(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    private static void AssertRecordsRelating(string form, string body, string records, string? payload)
    {
        AssertRecords(form, body, records, null);
        if (payload is not null)
        {
            var source = body + Main;
            foreach (var record in DiagnosticCorpus.Check(source).Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error))
            {
                var related = (record.Related ?? []).Select(x => x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty).ToArray();
                Assert.True(related.Contains(payload), $"{form}: expected the payload {payload} related, found [{string.Join(", ", related)}]");
            }
        }
    }
}
