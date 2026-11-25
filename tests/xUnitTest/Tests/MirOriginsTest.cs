// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Verification;
using Xunit;

namespace XunitTest;

/// <summary>A5's Origin stratum, Refuted part (2a0-D §6; docs/dev/DECISIONS.md R4-M113): every row of a body's C1-F4 view and every
/// containment edge of its Refs is an edge of one graph per class of views; a finite Origin reaching an edge into a fixed one is
/// <c>UnsatisfiedOriginRelation_Kd</c> at that edge's site with its Reason and facts; an Unclassed end is the located limit (row 9); a
/// Dependent node is never a source or a judged target, only passing on what reaches it (R4-M118).</summary>
public sealed partial class MirOriginsTest
{
    // A local whose clause names `zz`, an unknown Origin: its pending slot `x.s` stays Unbound (a Binding failure), a Dependent node
    // (DECISIONS R4-M118), and the chain local -> x.s -> a passes through it; Binding records only the failed clause, and ValidClause, which
    // names `a` instead, reports the escape at `local@ref`.
    internal const string FailedClause = ClauseHead + "zz" + ClauseTail;

    // A repeated binding set: `{x}` is already bound by the parameter's Type, so the clause names no bound Origin and rests nothing
    // (SPEC 15.3.3, H11b-0 B1); the repeated set rests its uses on the duplicate.
    internal const string RepeatedSet = "struct Holder {a}\n    public let item: ref/i32 during a\n" +
        "    public init(item: ref/i32 during a) => self.item = item\n    public func peek(self: ref/Self) -> i32 => self.item@follow\n" +
        "func run(h: ref/Holder{x}) -> i32\n    let k: ref/Holder{x} = h\n        origin x.a == zz\n    return k.peek()\n";

    private const string Main = "public func main() => ()\n";

    private const string ClauseHead = "struct C {s}\n    public let call: (ref/i32 during s) -> i32\n" +
        "    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\n" +
        "func f(a: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        origin x.s outlives ";

    private const string ClauseTail = "\n    let local: i32 = 5\n    return h.call(local@ref)\n";

    private const string ValidClause = ClauseHead + "a" + ClauseTail;

    // A Closure header with an omitted inner Origin (PLAN G216's form), called in its evaluating body.
    private const string ClosureHeader = "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32)) -> i32 => p@follow@follow\n    let x = 5\n";

    // PLAN G216 (b)'s r1 and r2: a Closure's and a local Function Type's result `ref/(ref/i32 during q)` lies during the per-call Origin of
    // the input the row writes next, well formed only where that input names `q` (SPEC 15.3.4).
    private const string NestedResult = "func k(q: ref/i32) -> i32\n    let n = 1\n    let h = func [q] (p: ";

    private const string NestedFunctionType = "func k(q: ref/i32) -> i32\n    let n = 1\n    var h: (";

    // PLAN G216 (b)'s p4 (p3's Closure twin): a value call whose argument's inner Origin `b` meets the input's `a`, after a head with or
    // without the clause ordering them.
    private const string ValueCall = "    let h = func [] (p: ref/(ref/i32 during a)) -> i32 => p@follow@follow\n    return h(s)\n";

    // A holder of one shared reference and a callee whose result meets its two arguments' Origins (the 2a-1c design review's m7).
    private const string Held = "struct C {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n" +
        "func pick(x: ref/i32 during s, y: ref/i32 during s) -> ref/i32 during s => x\n";

    // Beside Held, a function returning `during a` with a local `l` to borrow; Escape, a local's Borrow held in a C and returned (the 2a-1c
    // final review's T1); Unordered, a parameter's Origin held and returned `during b`; Pair, a holder of two Origins (BU's review probes).
    private const string HeldLocal = Held + "func f(a: ref/i32) -> ref/i32 during a\n    let l = 5\n";

    private const string Escape = HeldLocal + "    let h0 = C.init(l@ref)\n";

    private const string Unordered = Held + "func g(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    let h0 = C.init(a)\n";

    private const string Pair = "struct P {s, t}\n    public let r: ref/i32 during s\n    public let q: ref/i32 during t\n" +
        "    public init(r: ref/i32 during s, q: ref/i32 during t)\n        self.r = r\n        self.q = q\n" +
        "func f(a: ref/i32) -> ref/i32 during a\n    let l = 5\n";

    // The destruction receiver's part and its helper borrow (G161's two forms).
    private const string Destructors = "struct Buffer\n    public var n: i32 = 0\n    public init() => ()\n" +
        "struct Holder {a}\n    public var buffer: Buffer\n    public var view: ref/Buffer during a\n" +
        "    public init(other: ref/Buffer during a)\n        self.buffer = Buffer.init()\n        self.view = other\n" +
        "    drop\n        let old = Kimi.Intrinsics.exchange(self.view@uniq, with: self.buffer@ref)\n" +
        "struct Holder2 {a}\n    public var buffer: Buffer\n    public var view: ref/Buffer during a\n" +
        "    public init(other: ref/Buffer during a)\n        self.buffer = Buffer.init()\n        self.view = other\n" +
        "    drop\n        let whole = self@ref\n        let k = Kimi.Intrinsics.exchange(self.view@uniq, with: whole.buffer@ref)\n";

    // Their valid counterpart (d-g161-ok): the receiver's view exchanged with itself, a value of its own Origin.
    private const string ValidDestructor = "struct Buffer\n    public var n: i32 = 0\n    public init() => ()\n" +
        "struct Holder {a}\n    public var buffer: Buffer\n    public var view: ref/Buffer during a\n" +
        "    public init(other: ref/Buffer during a)\n        self.buffer = Buffer.init()\n        self.view = other\n" +
        "    drop\n        let old = Kimi.Intrinsics.exchange(self.view@uniq, with: self.view)\n";

    // A local region holding a parameter's Origin, beside a local to borrow (MeetNode's programs).
    private const string MeetHead = "func f(x: ref/i32, y: ref/i32) -> ref/i32 during x\n    var p = x\n    var q = y\n    let local = 1\n";

    // j-region: a local's Borrow assigned into a local region the result names.
    private const string Region = "func bad(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n" +
        "    view = local@ref\n    return view\n";

    // A Reborrow through a local's exclusive layers, returned: its chain passes the containment edges of both layers.
    private const string Layers = "func f(p: ref/i32) -> ref/i32 during p\n    var local: i32 = 1\n    var r = local@uniq\n    let rr = r@uniq\n" +
        "    return rr@follow@follow@ref\n";

    // A guard candidate's Borrow stored into a fixed Origin (2a0-D's rv/guard-annot).
    private const string Guard = "func keep(slot: uniq/(ref/i32 during a), value: ref/i32 during a) -> bool\n    slot@follow = value\n    return true\n" +
        "func g(anchor: ref/i32, x: Option<i32>) -> i32\n    var t: ref/i32 during anchor = anchor\n    match x\n" +
        "        .Some(let v) if keep(t@uniq, v@ref) => return 1\n        _ => return 0\n";

    // A Closure storing a Borrow of an environment binding through a captured exclusive reference (2a0-D's rv/env-store): under its
    // borrowed receiver the binding's atom (row 4) resolves to the call receiver Origin ρ (row 5; DECISIONS R4-M126 P5).
    private const string EnvStore = "func store(target: uniq/(ref/i32 during t), value: ref/i32 during t) -> ()\n    target@follow = value\n" +
        "func g(x: i32, target: uniq/(ref/i32 during t)) -> ()\n    var f = func [x, target] () => store(target, value: x@ref)\n    f()\n";

    // A Property's storage passed to a `during static` parameter: an accessor's Borrow of static storage (2a0-D §3 rows 7 and 7').
    private const string Storage = "group Util\n    public func keepStatic(r: ref/i32 during static) -> i32 => r@follow\ngroup Api\n";

    // A callee's meet of its own Origins (2a0-D's rv/meet-trans, I-1): the call's Origins are call regions of mt's body (SPEC 15.6.4
    // step 1; U3-design.md's twelfth amendment), so t's inferred region is bounded by x alone and the program is valid.
    private const string MeetTrans = "func put(slot: uniq/(ref/i32 during (a and b)), v: ref/i32 during (a and b)) -> ()\n    slot@follow = v\n" +
        "func mt(x: ref/i32, y: ref/i32) -> i32\n    var t = x\n    put(t@uniq, y)\n    return 0\n";

    // MeetTrans' call in a constructor and in its struct's Field initializer, one call's regions in each part of the constructor's slice
    // (DECISIONS BU-102; be85b19e2 threw at `t@uniq` when they were the callee's Parameter atoms, dev added an InternalInvariant_Kd).
    private const string MeetTransBothParts = "group Util\n    public func put(slot: uniq/(ref/i32 during (a and b)), v: ref/i32 during (a and b)) -> ()\n" +
        "        slot@follow = v\nstruct S\n    public var n: i32 = if true\n        let x = 1\n        let y = 5\n        var t = x@ref\n" +
        "        Util.put(t@uniq, v: y@ref)\n        yield 0\n    else => 0\n    public init()\n        let x = 1\n        let y = 5\n" +
        "        var t = x@ref\n        Util.put(t@uniq, v: y@ref)\n";

    // Field initializers forming layer rows (PLAN G215's probes): a local's Borrow annotated into the layer's Origin `a` (Refuted), the
    // same through an exclusive layer (a Reborrow), and a written Type well formed only when `b outlives a` (Unknown without the clause).
    private const string LocalInit = "    public var n: i32 = if true\n        let x = 1\n        let t: ref/i32 during a = x@ref\n" +
        "        yield 0\n    else => 0\n";

    private const string ReborrowInit = "    public var n: i32 = if true\n        var x = 1\n        var r = x@uniq\n" +
        "        let t: ref/i32 during a = r@follow@ref\n        yield 0\n    else => 0\n";

    private const string PairInit = "    public let p: ref/i32 during a\n    public var n: i32 = if true\n" +
        "        let t: Option<ref/(ref/i32 during b) during a> = Option.None\n        yield 0\n    else => 0\n";

    private const string HolderP = "struct Holder {a}\n    public let p: ref/i32 during a\n";

    // A comparison's region row in a Field initializer (DECISIONS G492-a (iii); its judged twins are MirBorrowsTest's vfic, vficn).
    private const string ComparisonUtil = "group Util\n    public func idf(w: ref/i64 during a) -> ref/i64 during a => w\n";

    private const string ComparisonInit = "struct Holder {a}\n    public let p: ref/i64 during a\n    public var n: i32 = if true\n" +
        "        let v: i64 = 1\n        var x = v@ref\n        var y = x\n";

    private const string InitP = "    public init(p: ref/i32 during a) => self.p = p\n";

    // A base whose two constructors have every parameter defaulted, so a derived struct's omitted base clause selects none.
    private const string NoBase = "open struct Base\n    protected init(a: i32 = 1) => ()\n    protected init(b: string = \"x\") => ()\n" +
        "struct Leaf {a}: Base\n";

    // SPEC 15.6.5: a finite Origin never outlives a fixed one, so each chain from a finite source into a fixed Origin is a problem at its
    // last edge's site (DECISIONS R4-M113's Site rule: a row's Site, or the Ref's statement for a containment edge), whether or not that
    // site is reachable (j-unreach), through a local region (j-region, m-scope), a nested function's own body (n-inner), a guard candidate
    // and the subject it binds (rv/guard-annot), the destruction receiver's part and its helper borrow (G161), shared and exclusive layers
    // a Ref is reached through (SPEC 15.6.3's containment), an object's own step, a mutable static's storage (row 7), and FailedClause's
    // body with its clause valid (AChainThroughADependentNodeIsJudgedBetweenItsEnds' counterpart), and a Field initializer's layer rows in a
    // declared or synthesized constructor's slice (AConstructorlessLayersRowsAreTheLocatedLimit's twins).
    [Theory]
    [InlineData("func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n", "local@ref")]
    [InlineData(Region, "view")]
    [InlineData("func u(x: ref/i32, c: bool) -> ref/i32 during x\n    if c\n        return x\n    else\n        return x\n    let t = 1\n    return t@ref\n", "t@ref")]
    [InlineData("func f(x: ref/i32, c: bool) -> ref/i32 during x\n    var r = x\n    if c\n        let local = 1\n        r = local@ref\n    return r\n", "r")]
    [InlineData("func use(bound: ref/i32) -> i32\n    func inner() -> ref/i32 during bound\n        let t = 1\n        return t@ref\n    return 0\n", "t@ref")]
    [InlineData(Guard, "v@ref", "v@ref")]
    [InlineData(Destructors, "self.buffer@ref", "whole.buffer@ref")]
    [InlineData("func f(p: ref/i32) -> ref/i32 during p\n    let local: i32 = 1\n    let r = local@ref\n    let rr = r@ref\n    return rr@follow@follow@ref\n", "rr@follow@follow@ref")]
    [InlineData(Layers, "rr@follow@follow@ref", "rr@follow@follow@ref")]
    [InlineData("struct Cell\n    public var value: i32\n    public init(value: i32) => self.value = value\nfunc own(input: ref/i32) -> ref/i32 during input\n    let h: obj/Cell = Cell.init(value: 5)@obj\n    return h.value@ref\n", "h.value@ref")]
    [InlineData(Storage + "    public var item: i32 = 0\n        get() -> i32 => Util.keepStatic(storage@ref)\n        set(value: i32) -> () => storage = value\n", "storage@ref")]
    [InlineData(ValidClause, "local@ref")]
    [InlineData(HolderP + LocalInit + InitP, "x@ref")]
    [InlineData("struct Leaf {a}\n" + LocalInit, "x@ref")]
    [InlineData(HolderP + ReborrowInit + InitP, "r@follow@ref")]
    public void AFiniteChainIntoAFixedOriginIsRefutedAtItsLastEdge(string source, params string[] sites)
        => Assert.Equal([.. sites.Select(static x => $"{nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd)} {x}")], Origins(MirBuildTest.Compile(source + Main)));

    // SPEC 15.6.5 and MeetNode's rule (2a0-D D15): a non-fixed meet at a row's longer end is a node of its own beside an edge per operand,
    // so what reaches the meet itself is judged at the shorter end: a local's Borrow annotated `during (p and x)` and returned `during x`,
    // and the meet of two local regions read into a `during x` local (where the operand y is also Unknown); the valid counterpart names x.
    // The meet is also at the initializer's shorter end: the meet law's derived edge from the finite initializer into the meet's bound is
    // Refuted at that in-edge's site too (SPEC 15.3.6; one record per edge into a Fixed node and failing source, as at the use). A local
    // annotation's fit is recorded at its initializer (R7-O1 H's D5), and the meet stays one row for MeetNode (R7-O1 H's F-H-13).
    [Theory]
    [InlineData(
        MeetHead + "    let m: ref/i32 during (p and x) = local@ref\n    return m\n",
        "UnsatisfiedOriginRelation_Kd local@ref",
        "UnsatisfiedOriginRelation_Kd m")]
    [InlineData(
        MeetHead + "    let m: ref/i32 during (p and q) = local@ref\n    let n: ref/i32 during x = m\n    return n\n",
        "UnsatisfiedOriginRelation_Kd local@ref",
        "UnsatisfiedOriginRelation_Kd m",
        "UnprovenOriginRelation_Kd m")]
    [InlineData(MeetHead + "    let m: ref/i32 during (p and x) = x\n    return m\n")]
    public void AMeetAtTheLongerEndIsAlsoItsOwnNode(string source, params string[] expected)
        => Assert.Equal(expected, Origins(MirBuildTest.Compile(source + Main)));

    // DIAGNOSTICS 4.3, 2a0-D D18: two finite sources into one fixed Origin are two problems at one subject, told apart by their Reasons,
    // each with its last edge (Relation: the row's index in the body's view) and its Anchor (the first Ref whose Origin is the source).
    [Fact]
    public void TwoFiniteSourcesIntoOneFixedOriginAreTwoProblems()
    {
        var c = MirBuildTest.Compile("func two(x: ref/i32, c: bool) -> ref/i32 during x\n    let l1 = 1\n    let l2 = 2\n    var r = x\n" +
            "    if c => r = l1@ref else => r = l2@ref\n    return r\n" + Main);
        var found = Found(c.BuildMir()).ToArray();
        Assert.Equal(2, found.Length);
        Assert.NotSame(found[0].Problem.Reason.Longer, found[1].Problem.Reason.Longer);
        Assert.Same(found[0].Problem.Reason.Shorter, found[1].Problem.Reason.Shorter);
        Assert.Equal(["r: Relation r 3, Anchor l1@ref -1", "r: Relation r 3, Anchor l2@ref -1"], Facts(c));
    }

    // PD31 (2a0-D D18): an Origin problem's facts are its last edge (Relation) and its Anchor: `bad`'s returned Borrow, built and anchoring
    // the record at the value (R7-O1 H: Binding judges no body relation; it was an Opaque, its twin's row); j-region; and a chain through two exclusive
    // layers' containment edges, one problem per source, each anchored at its own Borrow.
    [Theory]
    [InlineData("func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n", "local@ref: Relation local@ref 0, Anchor local@ref -1")]
    [InlineData(Region, "view: Relation view 2, Anchor local@ref -1")]
    [InlineData(
        Layers,
        "rr@follow@follow@ref: Relation rr@follow@follow@ref 1, Anchor r@uniq -1",
        "rr@follow@follow@ref: Relation rr@follow@follow@ref 1, Anchor local@uniq -1")]
    public void AnOriginProblemCarriesItsLastEdgeAndAnchor(string source, params string[] expected)
        => Assert.Equal(expected, Facts(MirBuildTest.Compile(source + Main)));

    // 2a0-D §3's identity between MIR Types and rows: a Ref's own Origin is its Type's, resolved at its statement, so each builder site
    // whose Ref has an Origin forms the node its rows and containment edges name, and the chain from it is Refuted, anchored at that Ref:
    // an argument's adapting Borrow, a stored exclusive reference Reborrowed into the result, a guard candidate, an explicit Borrow of a
    // Slice element (an Index, a shared layer, E6), and a Slice handle's Borrow, named by its root (`leak3`, `leak4` and the `var` form;
    // SPEC 4.8.1), anchored at the Borrow itself (R7-O1 H: Binding judges no body relation, so the return's Borrow is built; C6).
    [Theory]
    [InlineData(
        "func id(r: ref/i32) -> ref/i32 during r => r\nfunc f(x: ref/i32) -> ref/i32 during x\n    var view = x\n    let local = 1\n" +
        "    view = id(local)\n    return view\n",
        "UnsatisfiedOriginRelation_Kd view <- local")]
    [InlineData(
        "func f(x: uniq/i32) -> uniq/i32 during x\n    var local: i32 = 1\n    var r = local@uniq\n    return r\n",
        "UnsatisfiedOriginRelation_Kd r <- local@uniq")]
    [InlineData(Guard, "UnsatisfiedOriginRelation_Kd v@ref <- let v", "UnsatisfiedOriginRelation_Kd v@ref <- x")]
    [InlineData(
        "func f(x: ref/i32) -> ref/i32 during x\n    let values = [1, 2, 3]\n    let s = values[0..2]\n    var view = x\n    view = s[0]@ref\n" +
        "    return view\n",
        "UnsatisfiedOriginRelation_Kd view <- s[0]@ref")]
    [InlineData(
        "func leak3(values: Slice<i32> during s) -> ref/(Slice<i32> during s) during s\n    return values@ref\n",
        "UnsatisfiedOriginRelation_Kd values@ref <- values@ref")]
    [InlineData(
        "func leak4(values: Slice<i32> during s) -> ref/(Slice<i32> during s) during s\n    let local = values\n    return local@ref\n",
        "UnsatisfiedOriginRelation_Kd local@ref <- local@ref")]
    [InlineData(
        "func leak4(values: Slice<i32> during s) -> uniq/(Slice<i32> during s) during s\n    var local = values\n    return local@uniq\n",
        "UnsatisfiedOriginRelation_Kd local@uniq <- local@uniq")]
    public void EachBuilderRefFormsTheNodeItsRowsName(string source, params string[] expected)
    {
        var c = MirBuildTest.Compile(source + Main);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(expected, Found(program).Select(x => $"{x.Problem.Code} {Line(c, x.Problem.Subject)}" +
            string.Concat(
                program.Problems.Facts(x.Index).ToArray().Where(static f => f.Kind == ProblemFactKind.Anchor).Select(f => $" <- {Line(c, f.Site)}"))));
    }

    // 2a0-D §3: the builder's own Refs, a synthesized input's borrow, an update's writeback reservation and a selection's raw pointer,
    // carry no Origin, so they form no node; and a stored exclusive reference whose value a statement discards is Reborrowed by its case
    // into a node that reaches no fixed Origin.
    [Theory]
    [InlineData("func f() -> i32\n    var values = [1, 2, 3]\n    values[0] += 1\n    let s = values[0..2]\n    return s[0]\n", false)]
    [InlineData("func f(x: uniq/i32) -> i32\n    var local: i32 = 1\n    var r = local@uniq\n    r\n    return 0\n", true)]
    public void ABuildersOwnRefFormsNoChain(string source, bool origins)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var refs = body.Statements.ToArray().Where(static x => x.Rvalue.Kind == MirRvalueKind.Ref).Select(static x => x.Rvalue.Type!.Origin is not null);
        Assert.Equal(origins, Assert.Single(refs.Distinct()));
        Assert.Empty(Origins(c));
    }

    // audit-origins rows 7, 9c and 19 (2a0-D §10): the MIR's Origin problems of a body chain, at the sites where the old reporter recorded
    // their twins: an invariant `==` one direction of which the clause proves (row 7), a Reborrow through two exclusive layers and a stored
    // exclusive reference's Reborrow into a fixed layer (row 9c's containment), and a local's Borrow through locals with no candidate, a
    // local region (row 19), returned. With the old reporter gone (R6-check H8a D4) the rows pin the MIR's lines.
    [Theory]
    // R7Inv's local annotation fit is recorded at its initializer `y` (R7-O1 H's D5).
    [InlineData(R7Inv, "UnprovenOriginRelation_Kd uniq/(ref/i32 during a); UnprovenOriginRelation_Kd y")]
    [InlineData(Layers, "UnsatisfiedOriginRelation_Kd rr@follow@follow@ref; UnsatisfiedOriginRelation_Kd rr@follow@follow@ref")]
    [InlineData("func f(p: uniq/(uniq/i32 during a)) -> ()\n    var local: i32 = 1\n    var r = local@uniq\n    p@follow = r\n", "UnsatisfiedOriginRelation_Kd r")]
    [InlineData("func f(y: ref/i32) -> ref/i32 during y\n    let l = 1\n    var a: ref/i32 = y\n    a = l@ref\n    let b: ref/i32 = a\n    return b\n", "UnsatisfiedOriginRelation_Kd b")]
    public void AnAuditRowsChainHasItsOriginProblems(string source, string origins)
        => Assert.Equal(origins, string.Join("; ", Origins(MirBuildTest.Compile(source + Main)).Order(StringComparer.Ordinal)));

    // audit-origins row 19 read into a local Option: the MIR's Origin problem is the chain's one record; Binding judges no body relation,
    // so it has no twin (R7-O1 H; DECISIONS R4-M108's twins until R7 superseded).
    [Fact]
    public void AnAuditRowsChainIsA5sOneRecord()
    {
        var c = MirBuildTest.Compile("func f(y: ref/i32) -> Option<ref/i32 during y>\n    let l = 1\n    let a: Option<ref/i32> = .Some(l@ref)\n" +
            "    let b: Option<ref/i32> = a\n    return b\n" + Main);
        var codes = new[] { DiagnosticCode.UnsatisfiedOriginRelation_Kd, DiagnosticCode.UnprovenOriginRelation_Kd };
        Assert.DoesNotContain(c.Binding.Issues, x => codes.Contains(x.Code));
        Assert.Equal(["UnsatisfiedOriginRelation_Kd b"], Origins(c));
    }

    // SPEC 15.6.5 and 15.6.3: valid counterparts form no Refuted chain: a parameter's Borrow passed on, a Field's Borrow read through its
    // binder, a Reborrow through shared and exclusive layers whose Origins the result names, a Slice element's Borrow (E6), a raw Deref,
    // which stops containment (a-wf), a local region holding only a local's Borrow, an immutable static's storage passed to a
    // `during static` parameter (row 7', Inferred; SPEC 11.8), which check rejects (PLAN G197's residual, ledger (a)),
    // the destruction receiver's view exchanged with itself (d-g161-ok), and a Closure header's omitted inner Origin, whose
    // well-formedness is its signature's premise, proven at the call, so no row of the evaluating body (PLAN G216, DECISIONS R4-M137); a
    // Field initializer's written Type under the Type's clause in its constructor's slice, and a constructorless layer with no row (G215);
    // a value call's fresh per-call Origin, shortened by the input's well-formedness (p4, SPEC 15.6.4 steps 1 and 3); and a `$tryWrite`
    // whose writer operand is Never, a transfer or `$abort`: it supplies no value, so its writer names no Origin of Utf8Writer's own
    // declaration (SPEC 14.10.3, 15.3.4; PLAN G410); and MeetTrans in a function and in both parts of a constructor's slice, whose call
    // regions satisfy every row (U3-design.md's twelfth amendment: valid, the located limit before); and a Closure's and a local Function
    // Type's result `ref/(ref/i32 during q)` during the per-call Origin of an input naming `q` (PLAN G216 (b)'s r1 and r2, SPEC 15.3.4): the
    // nested signature's declaration row, judged by Binding from its inputs (R7-O3 U-O3; the located limit before, an Unclassed end).
    [Theory]
    [InlineData(NestedResult + "ref/(ref/i32 during q)) -> Option<ref/(ref/i32 during q)> => Option.Some(p)\n    let o = h(q@ref)\n    return 0\n")]
    [InlineData(NestedFunctionType + "ref/(ref/i32 during q)) -> Option<ref/(ref/i32 during q)> = func (p) => Option.Some(p)\n    let o = h(q@ref)\n    return 0\n")]
    [InlineData(MeetTrans)]
    [InlineData(MeetTransBothParts)]
    [InlineData("func pass(x: ref/i32) -> ref/i32 during x\n    let y = x\n    return y\n")]
    [InlineData("struct P {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\nfunc get(p: ref/P) -> ref/i32 during p.a\n    return p.r\n")]
    [InlineData("func shallow(x: ref/(ref/i32 during a)) -> ref/i32 during a\n    return x@follow\n")]
    [InlineData("func f(p: uniq/(uniq/i32 during a) during b) -> ref/i32 during b\n    return p@follow@follow@ref\n")]
    [InlineData("func first(values: Slice<i32> during s) -> ref/i32 during s\n    return values[0]@ref\n")]
    [InlineData("func viaAnchor(p: raw/(ref/i32 during a), anchor: ref/i32 during r) -> ref/(ref/i32 during a) during r\n    unsafe => return (*p)@ref\n")]
    [InlineData("func ok() -> i32\n    let a = 1\n    var r = a@ref\n    return r@follow\n")]
    [InlineData(Storage + "    public let item: i32 = 0\n        get() -> i32 => Util.keepStatic(storage@ref)\n")]
    [InlineData(ValidDestructor)]
    [InlineData(ClosureHeader + "    return h(x@ref@ref)\n")]
    [InlineData("struct Pair {a, b}\n    origin b outlives a\n" + PairInit + InitP)]
    [InlineData(HolderP + "    public var n: i32 = if true\n        let x = 1\n        let t: ref/i32 = x@ref\n        yield t@follow\n    else => 0\n")]
    [InlineData("func call(a: ref/i32, s: ref/(ref/i32 during b)) -> i32\n    origin b outlives a\n" + ValueCall)]
    [InlineData("func run() -> i32\n    _ = $tryWrite(do => return 7, \"\\(42)\")\n    return 0\n")]
    [InlineData("func run() -> i32\n    _ = $tryWrite($abort(\"w\"), \"x\")\n    return 0\n")]
    public void AValidChainHasNoOriginProblem(string source)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.Empty(Origins(c));
    }

    // DECISIONS R4-M101 (b), by construction: a default's rows are its template's, judged once in its DefaultTemplate body, and a caller's
    // Ref inside the inline copy adds no containment, so no caller body forms an Unclassed node from a template's atom (R4-M69, R4-M97).
    [Theory]
    [InlineData("func pick(x: ref/i32 during s, y: ref/i32 during s) -> i32 => x@follow\nfunc h(a: i32 = pick(3@ref, 4@ref)) -> i32 => a\nfunc caller() -> i32 => h()\n")]
    [InlineData("func pick(x: ref/i32 during s, y: ref/i32 during s) -> i32 => x@follow\nfunc d(n: i32, k: i32 = pick(n@ref, n@ref)) -> i32 => k\nfunc caller() -> i32 => d(3)\n")]
    [InlineData("func pick(x: ref/i32 during s, y: ref/i32 during s) -> i32 => x@follow\nfunc h(n: i32, f: () -> i32 = func [] () => pick(7@ref, 8@ref)) -> i32 => f()\nfunc caller() -> i32 => h(1)\n")]
    public void AnInlineDefaultCopyFormsNoNodeInItsCaller(string source)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.Empty(Origins(c));
    }

    // PLAN G215 (DECISIONS R4-M168): a layer whose rows no constructor body's slice includes is judged by no MIR body, so each of its rows
    // is the located limit until the layer has a body of its own, valid or not: an omitted base clause that selects no constructor (the
    // pick call; a local's Borrow into `a`), and a Field without an initializer and no declared constructor (the written Type with and
    // without its clause; the local's Borrow). The twins with a constructor are judged (the Refuted, Unknown and valid theories).
    [Theory]
    [InlineData(
        "group Util\n    public func pick(x: ref/i32 during s, y: ref/i32 during s) -> i32 => x@follow\n" + NoBase +
            "    public var n: i32 = Util.pick(3@ref, 4@ref)\n",
        "3@ref",
        "4@ref")]
    [InlineData(NoBase + LocalInit, "x@ref")]
    [InlineData("struct Pair {a, b}\n    origin b outlives a\n" + PairInit, "ref/(ref/i32 during b) during a")]
    [InlineData("struct Pair {a, b}\n" + PairInit, "ref/(ref/i32 during b) during a")]
    [InlineData(HolderP + LocalInit, "x@ref")]
    [InlineData(ComparisonUtil + ComparisonInit + "        let f: (ref/i64 during y) -> ref/i64 during x = Util.idf\n        yield 0\n    else => 0\n", "v@ref", "x", "Util.idf")] // G492-a (iii)'s vfil: valid
    [InlineData(ComparisonUtil + ComparisonInit + "        let f: (ref/i64 during x) -> ref/i64 during a = Util.idf\n        yield 0\n    else => 0\n", "v@ref", "x", "Util.idf")] // vfiln: invalid (Refuted)
    public void AConstructorlessLayersRowsAreTheLocatedLimit(string source, params string[] sites)
        => Assert.Equal([.. sites.Select(static x => $"{nameof(DiagnosticCode.Unsupported_Kd)} {x}")], Origins(MirBuildTest.Compile(source + Main)));

    // PLAN G951 E1 (DECISIONS R5-M168's input rule): a layer row outside the root range is the library check's, so the constructorless
    // layer above (NoBase + LocalInit) adds its limit only under a root range that includes its module.
    [Fact]
    public void AConstructorlessLayerOutsideTheRootRangeIsNotJudged()
    {
        var c = MirBuildTest.Compile(NoBase + LocalInit + Main);
        var program = c.BuildMir();
        var (origins, outside, inside) = (new MirOrigins(c.Types, new MirCases(c.Types)), new MirReport(), new MirReport());
        origins.Findings(program, outside, 0);
        origins.Findings(program, inside, c.SourceModules.Length);
        Assert.Empty(outside.All.ToArray());
        Assert.Equal([DiagnosticCode.Unsupported_Kd], inside.All.ToArray().Select(static x => x.Code));
    }

    // SPEC 23.1.2 (DECISIONS R4-M118): an end a Binding failure left Unbound is a Dependent node, never a source or a judged target, so a
    // chain that starts or ends at it needs the unresolved Origin and records nothing, Binding's failure explaining the rejection: a
    // `var`'s binding set with no initializer and a containment edge into a Dependent layer. RepeatedSet stays as the control: since H11b-0 B1 its local's failed clause rests
    // nothing (SPEC 15.3.3), so it has no Dependent end, and its chain still records nothing.
    [Theory]
    [InlineData(RepeatedSet)]
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n" +
        "func f(x: ref/i32)\n    var pending: V<i32>{p}\n        origin p.source outlives x\n")]
    [InlineData(Held + "func f(a: ref/i32) -> i32\n    let h0 = C.init(a)\n    let h: C{x} = h0@move\n        origin x.s outlives zz\n" +
        "    let q = h.r@follow@ref\n    return q@follow\n")]
    public void AChainThatStartsOrEndsAtADependentNodeRecordsNothing(string source)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Empty(Origins(c));
    }

    // SPEC 23.1.2 and 15.6.5 (DECISIONS R4-M118): a chain through a Dependent node holds whatever the unresolved Origin is, so it is judged
    // between its ends at its last edge, the returned value (R7-O1 H: the row is formed at the value): T1's local Borrow returned `during
    // a` (also with the clause omitted, and with a plain local), a fixed source the premises do not order, FailedClause's escape (one
    // record at `h0@move`: the failed `let h`'s fit is recorded at its initializer since R7-O1 H's D5, the same relation as the row its
    // value fit leaves there, PLAN G739), and BU's forms
    // through a slot no failed clause names (one local's, a Tuple's second set, an assignment's). Such a slot completes (DECISIONS BU-68): the records
    // stay, the assignment's at `p.q`. A meet whose operand `b` rests on no failed input is a conjunct of its own (SPEC 15.3, 23.1.2;
    // R4-M118 as amended by R7-O1 H's F-H1-4): `b outlives a` is Unproven at the returned `m` (it was AChainThatStartsOrEnds...'s row).
    [Theory]
    [InlineData(Escape + "    let h: C{x} = h0@move\n        origin x.s outlives zz\n    return h.r\n", "Unsatisfied", "h.r")]
    [InlineData(Escape + "    let h: C{x} = h0@move\n    return h.r\n", "Unsatisfied", "h.r")]
    [InlineData(Escape + "    let h = h0@move\n    return h.r\n", "Unsatisfied", "h.r")]
    [InlineData(Unordered + "    let h: C{x} = h0@move\n        origin x.s outlives zz\n    return h.r\n", "Unproven", "h.r")]
    [InlineData(FailedClause, "Unsatisfied", "h0@move")]
    [InlineData(Pair + "    let p: P{x} = P.init(a, l@ref)\n        origin x.s outlives zz\n    return p.q\n", "Unsatisfied", "p.q")]
    [InlineData(
        HeldLocal + "    let h: (C{x}, C{y}) = (C.init(a), C.init(l@ref))\n        origin x.s == zz\n    return h.1.r\n",
        "Unsatisfied",
        "h.1.r")]
    [InlineData(
        Pair + "    var p: P{x} = P.init(a, a)\n        origin x.s outlives zz\n    p = P.init(a, l@ref)\n    return p.q\n",
        "Unsatisfied",
        "p.q")]
    [InlineData(
        Held + "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    let h0 = C.init(b)\n    let h: C{x} = h0@move\n" +
        "        origin x.s outlives zz\n    let m: ref/i32 during (x.s and b) = b\n    return m\n",
        "Unproven",
        "m")]
    public void AChainThroughADependentNodeIsJudgedBetweenItsEnds(string source, string verdict, params string[] sites)
        => Assert.Equal([.. sites.Select(x => $"{verdict}OriginRelation_Kd {x}")], Origins(MirBuildTest.Compile(source + Main)));

    // SPEC 23.1.2 and MeetNode's rule (2a0-D D15): at the longer end each operand of a meet must outlive the shorter end, so a classed
    // operand's edge beside a Dependent one is an independent judgment: a local's Borrow returned `during a` through `pick`'s meet with the
    // failed local's Field, directly (m7) or through an inferred local (m8), is the Refuted chain it is with the clause valid.
    [Theory]
    [InlineData("zz", "    return q\n", "q")]
    [InlineData("a", "    return q\n", "q")]
    [InlineData("zz", "    let w = q\n    return w\n", "w")]
    public void AnOperandEdgeBesideADependentOperandIsJudged(string clause, string tail, string site)
    {
        var c = MirBuildTest.Compile(Held + "func f(a: ref/i32) -> ref/i32 during a\n    let h0 = C.init(a)\n    let h: C{x} = h0@move\n" +
            "        origin x.s outlives " + clause + "\n    let l = 5\n    let q = pick(h.r, l@ref)\n" + tail + Main);
        Assert.Equal([$"{nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd)} {site}"], Origins(c));
    }

    // Pinned (DECISIONS BU-25, PLAN P42): an explicit Borrow of static storage is Binding's located Unsupported_Kd until P42, so the MIR
    // forms no Origin problem for it.
    [Fact]
    public void AnExplicitStaticBorrowIsBindingsLimitUntilP42()
    {
        var c = MirBuildTest.Compile("group State\n    public var count: i32 = 0\n    public let limit: i32 = 10\n    func reads() -> i32\n" +
            "        let c = count@ref\n        let l = limit@ref\n        return c@follow + l@follow\n" + Main);
        Assert.Equal([DiagnosticCode.Unsupported_Kd, DiagnosticCode.Unsupported_Kd], c.Binding.Issues.Select(static x => x.Code));
        Assert.Empty(Origins(c));
    }

    // DECISIONS R4-M154 (Revision 4; MirOrigins.Outlives' contract): the walk from an end stops at a Fixed node, its own included, so
    // the Fixed nodes at or above c are c alone; c outlives b by z's implied premise (SPEC 15.3.3), so the pair holds; the body's Refuted
    // store of `local@ref` into p.* (a Finite node above c, Judge's own problem) lies past c. b need not outlive c. Fail-before: a walk
    // reaching a Fixed node read every edge of the nodes up to its index, so the Finite node joined c's bounds and the pair failed.
    [Fact]
    public void AnOutlivesWalkStopsAtAFixedNode()
    {
        const string Source = "func f(p: uniq/(ref/i32 during c) during u, z: ref/(ref/i32 during c) during b) -> i32\n    let local = 1\n" +
            "    p@follow = local@ref\n    return 0\n";
        var (origins, body, _) = Judged(Source, "f");
        var z = body.Locals.ToArray().Single(static x => x.Name == "z");
        var (b, c, site) = (z.Type!.Origin!, z.Type!.Components[0].Origin!, body.Subject(z.Source));
        Assert.Equal((MirRelation.Holds, MirRelation.Fails), (origins.Outlives(body, 0, c, b, site), origins.Outlives(body, 0, b, c, site)));
    }

    // The Z2 DECISIONS draft (M3c (i)): Judge classifies every view it is given and the Loan stratum queries only those, so a query of a
    // view Judge did not classify is a broken contract and throws, as one of another body does (before Z2: an undecided pair, the call's
    // located Unsupported_Kd).
    [Fact]
    public void AnOutlivesQueryOfAnUnclassifiedViewIsABrokenContract()
    {
        var (origins, body, _) = Judged("func f(x: ref/i32 during a, y: ref/i32 during b) -> i32 => 0\n", "f");
        var (x, y) = (body.Locals.ToArray().Single(static l => l.Name == "x"), body.Locals.ToArray().Single(static l => l.Name == "y"));
        var thrown = Assert.Throws<InvalidOperationException>(() => origins.Outlives(body, MirCases.Count(body), x.Type!.Origin!, y.Type!.Origin!, body.Subject(x.Source)));
        Assert.Contains("(InternalInvariant)", thrown.Message, StringComparison.Ordinal);
    }

    // PLAN G824 (SPEC 15.6.1, 15.6.5; MirOrigins.Fixed's contract): an Inferred Origin is Fixed under a run's views where its class's
    // edges reach one Fixed node from it and back, neither path through another Fixed node. r's referent slot equals a (uniq's invariance)
    // and b reaches it too: Fixed, but not under no view. s and t lie on one cycle through two Fixed nodes (a -> s -> b -> t -> a), each
    // between a and b: not Fixed. a reaches w, which reaches no Fixed node: not Fixed. Each is a reassignable local's Inferred atom (a
    // `let` local takes its initializer's Type).
    [Fact]
    public void AnInferredOriginIsFixedOnACycleThroughOneFixedNode()
    {
        const string Source = "func f(p: uniq/(ref/i32 during a) during u, o: uniq/(ref/i32 during b) during v, q: ref/i32 during a, " +
            "y: ref/i32 during b) -> i32\n    var s = q\n    o@follow = s\n    var t = y\n    p@follow = t\n    var w = q\n    let lb = 1\n" +
            "    var r = p@move\n    r@follow = o@follow\n    r@follow = lb@ref\n    return w@follow\n";
        var (origins, body, c) = Judged(Source, "f");
        var (rows, views, locals) = (c.Types.OriginRows(body.Key.Body), MirCases.Every(body), body.Locals.ToArray());
        bool Fixed(string name, bool referent, ulong under)
        {
            var local = locals.Single(x => x.Name == name);
            var (type, site) = (referent ? local.Type!.Components[0] : local.Type!, body.Subject(local.Source));
            Assert.Equal(OriginAtomKind.Inferred, rows.Atoms[c.Types.OriginAtom(body.Key.Body, type.Origin!, site)].Kind);
            return origins.Fixed(type, site, under);
        }

        Assert.Equal(
            (true, false, false, false, false),
            (Fixed("r", true, views), Fixed("r", true, 0), Fixed("s", false, views), Fixed("t", false, views), Fixed("w", false, views)));
    }

    // V16 "F4 Refuted" (2a0-D §6.4): N finite Borrows into one chain into one fixed Origin keep the work within c·(N_o + E_o·⌈Fin/64⌉), one
    // problem per source.
    [Fact]
    public void TheRefutedWorkScalesWithTheChain()
    {
        var work = new long[3];
        var sizes = new[] { 16, 32, 64 };
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("func f(x: ref/i32, c: i32) -> ref/i32 during x\n    var r = x\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append($"    let l{n}: i32 = {n}\n    if c == {n} => r = l{n}@ref\n");
            }

            var (origins, body, c) = Judged(text.Append("    return r\n").ToString(), "f");
            work[i] = origins.Work;
            Assert.Equal(sizes[i], Origins(c).Count);
        }

        Assert.True(work[2] - work[1] <= (3 * (work[1] - work[0])) + 64, $"work {string.Join(", ", work)}");
    }

    // V16 "F4" (2a0-D §6.4) under MeetNode's rule (D15): a non-Fixed meet at the shorter end gets its operands' edges once per class, so on
    // fixed-roots, a class of N + 1 rows into one meet of N + 1 Origins, the stratum's work over the program's bodies stays within
    // c·(N_o + E_o·⌈Fin/64⌉) (the factor 3 of TheRefutedWorkScalesWithTheChain; quadratic before), without an Origin problem.
    [Fact]
    public void AShorterMeetsOperandEdgesFormOncePerClass()
    {
        long[] work = [.. new[] { 32, 64, 128 }.Select(static n =>
        {
            var c = MirBuildTest.Compile(VerificationWorkloads.CallableRegions("fixed-roots", n));
            var origins = new MirOrigins(c.Types, new MirCases(c.Types));
            var total = 0L;
            foreach (var body in c.BuildMir().Bodies.ToArray())
            {
                origins.Judge(body, MirCases.Count(body), new MirReport());
                total += origins.Work;
            }

            Assert.Empty(Origins(c));
            return total;
        })];
        Assert.True(MirBorrowsTest.Grows(work, 3), $"work {string.Join(", ", work)}");
    }

    // V16 "F4 containment" (SPEC 15.6.3): a Reborrow through N exclusive layers and then M shared ones is contained in each exclusive layer
    // and the first shared one only: N + 1 edges, whatever M.
    [Theory]
    [InlineData(4, 4)]
    [InlineData(4, 8)]
    [InlineData(8, 8)]
    public void ContainmentStopsAtTheFirstSharedLayer(int exclusive, int shared)
    {
        var text = new StringBuilder("func f() -> i32\n    var l0: i32 = 1\n");
        for (var n = 1; n <= exclusive; n++)
        {
            text.Append($"    var l{n} = l{n - 1}@uniq\n");
        }

        text.Append($"    let s1 = l{exclusive}@ref\n");
        for (var m = 2; m <= shared; m++)
        {
            text.Append($"    let s{m} = s{m - 1}@ref\n");
        }

        text.Append($"    let x = s{shared}").Append(string.Concat(Enumerable.Repeat("@follow", shared + exclusive))).Append("@ref\n    return x@follow\n");
        var (origins, _, c) = Judged(text.ToString(), "f");
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.Equal(exclusive + 1, origins.ContainmentEdges);
    }

    // +warm: the stratum on a body it judged before allocates nothing (pooled frames, edges, components, bitsets and the arena; the oracles
    // the view pools; the cleared report it adds to): Refuted chains, Unknown chains and edges, an Equality row failing both ways, a pair layer's two classes, a Field
    // initializer's layer oracle and Dependent ends (FailedClause); and Findings, a constructorless layer's limit.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmJudgmentAllocatesNothing()
    {
        var c = MirBuildTest.Compile("func region(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n" +
            "    view = local@ref\n    return view\nfunc two(x: ref/i32, c: bool) -> ref/i32 during x\n    let l1 = 1\n    let l2 = 2\n" +
            "    var r = x\n    if c => r = l1@ref else => r = l2@ref\n    return r\nfunc layers(p: ref/i32) -> ref/i32 during p\n" +
            "    var local: i32 = 1\n    var r = local@uniq\n    let rr = r@uniq\n    return rr@follow@follow@ref\n" + R7Inv + Phantom +
            "func pair<s/T>(x: ref/i32, y: ref/i32) -> i32\n    s is owner or ref\n    let k: Option<s/(ref/i32 during x) during y> = .None\n    return 0\n" +
            "func chain(x: ref/i32, y: ref/i32) -> ref/i32 during y\n    var r = x\n    r = y\n    return r\n" + LayerCount + FailedClause +
            NoBase + LocalInit + Main);
        var program = c.BuildMir();
        var origins = new MirOrigins(c.Types, new MirCases(c.Types));
        var bodies = program.Bodies.ToArray();
        var report = new MirReport();
        void Run()
        {
            report.Clear();
            foreach (var body in bodies)
            {
                origins.Judge(body, MirCases.Count(body), report);
            }

            origins.Findings(program, report, c.SourceModules.Length);
        }

        Run();
        Assert.Contains(report.All.ToArray(), static x => x.Code == DiagnosticCode.Unsupported_Kd && x.Body == MirProblem.NoBody);
        Assert.Equal(0, AllocationMeasurement.Measure(Run));
    }

    // The Origin stratum's problems of a program's own bodies, as code and subject.
    private static List<string> Origins(Compilation c)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return [.. Found(program).Select(x => $"{x.Problem.Code} {Line(c, x.Problem.Subject)}")];
    }

    // The Origin stratum's problems of a program's own bodies with their facts: the subject, then each fact's kind, site and value.
    private static List<string> Facts(Compilation c)
    {
        var program = c.BuildMir();
        return [.. Found(program).Select(x => $"{Line(c, x.Problem.Subject)}: " +
            string.Join(", ", program.Problems.Facts(x.Index).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)} {f.Value}")))];
    }

    // The problems of a program's own bodies with an Origin stratum's code, and their indices; none of those bodies' problems is an
    // InternalInvariant_Kd (a broken view), which no row expects and the code filter would hide.
    private static IEnumerable<(MirProblem Problem, int Index)> Found(MirProgram program)
    {
        var all = program.Problems.All.ToArray();
        Assert.DoesNotContain(all, static x => x.Code == DiagnosticCode.InternalInvariant_Kd && x.Subject.Module == 0);
        return all.Select((x, i) => (x, i))
            .Where(static x => !x.x.IsRecord && x.x.Subject.Module == 0 && x.x.Code is DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd
                or DiagnosticCode.Unsupported_Kd or DiagnosticCode.OwnershipStorageLimit_Kd);
    }

    // A program's body judged by a stratum of its own, for its work and containment edges.
    private static (MirOrigins Origins, MirBody Body, Compilation C) Judged(string source, string name)
    {
        var c = MirBuildTest.Compile(source + Main);
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
        var origins = new MirOrigins(c.Types, new MirCases(c.Types));
        origins.Judge(body, MirCases.Count(body), new MirReport());
        return (origins, body, c);
    }

    private static string Line(Compilation c, SourceRef site) => c.SyntaxOf(site).ToString()!.Split('\n')[0].Trim();
}
