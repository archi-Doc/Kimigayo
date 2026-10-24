// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's Origin stratum, Refuted part (2a0-D §6; docs/dev/DECISIONS.md R4-M113): every row of a body's C1-F4 view and every
/// containment edge of its Refs is an edge of one graph per class of views; a finite Origin reaching an edge into a fixed one is
/// <c>UnsatisfiedOriginRelation_Kd</c> at that edge's site with its Reason and facts; an Unclassed end is the located limit (row 9); a
/// Dependent end records nothing (R4-M118).</summary>
public sealed partial class MirOriginsTest
{
    // A local whose clause names `zz`, an unknown Origin: its pending slot stays Unbound (a Binding failure), so its rows have a Dependent
    // end (DECISIONS R4-M118); ValidClause names `a` instead, and the same body is judged.
    internal const string FailedClause = ClauseHead + "zz" + ClauseTail;

    // A repeated binding set: `{x}` is already bound by the parameter's Type, so the local's slots stay Unbound and its row `k` meets two.
    internal const string RepeatedSet = "struct Holder {a}\n    public let item: ref/i32 during a\n" +
        "    public init(item: ref/i32 during a) => self.item = item\n    public func peek(self: ref/Self) -> i32 => self.item@follow\n" +
        "func run(h: ref/Holder{x}) -> i32\n    let k: ref/Holder{x} = h\n        origin x.a == static\n    return k.peek()\n";

    private const string Main = "public func main() => ()\n";

    private const string ClauseHead = "struct C {s}\n    public let call: (ref/i32 during s) -> i32\n" +
        "    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\n" +
        "func f(a: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        origin x.s outlives ";

    private const string ClauseTail = "\n    let local: i32 = 5\n    return h.call(local@ref)\n";

    private const string ValidClause = ClauseHead + "a" + ClauseTail;

    // A Closure header with an omitted inner Origin (PLAN G216's form), called in its evaluating body.
    private const string ClosureHeader = "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32)) -> i32 => p@follow@follow\n    let x = 5\n";

    // A holder of one shared reference and a callee whose result meets its two arguments' Origins (the 2a-1c design review's m7).
    private const string Held = "struct C {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n" +
        "func pick(x: ref/i32 during s, y: ref/i32 during s) -> ref/i32 during s => x\n";

    // The destruction receiver's part and its helper borrow (G161's two forms).
    private const string Destructors = "struct Buffer\n    public var n: i32 = 0\n    public init() => ()\n" +
        "struct Holder {a}\n    public var buffer: Buffer\n    public var view: ref/Buffer during a\n" +
        "    public init(other: ref/Buffer during a)\n        self.buffer = Buffer.init()\n        self.view = other\n" +
        "    drop\n        let old = Kimi.Intrinsics.exchange(self.view@uniq, with: self.buffer@ref)\n" +
        "struct Holder2 {a}\n    public var buffer: Buffer\n    public var view: ref/Buffer during a\n" +
        "    public init(other: ref/Buffer during a)\n        self.buffer = Buffer.init()\n        self.view = other\n" +
        "    drop\n        let whole = self@ref\n        let k = Kimi.Intrinsics.exchange(self.view@uniq, with: whole.buffer@ref)\n";

    // A guard candidate's Borrow stored into a fixed Origin (2a0-D's rv/guard-annot).
    private const string Guard = "func keep(slot: uniq/(ref/i32 during a), value: ref/i32 during a) -> bool\n    slot@follow = value\n    return true\n" +
        "func g(anchor: ref/i32, x: Option<i32>) -> i32\n    var t: ref/i32 during anchor = anchor\n    match x\n" +
        "        .Some(let v) if keep(t@uniq, v@ref) => return 1\n        _ => return 0\n";

    // A Closure storing a Borrow of an environment binding through a captured exclusive reference (2a0-D's rv/env-store, row 4).
    private const string EnvStore = "func store(target: uniq/(ref/i32 during t), value: ref/i32 during t) -> ()\n    target@follow = value\n" +
        "func g(x: i32, target: uniq/(ref/i32 during t)) -> ()\n    var f = func [x, target] () => store(target, value: x@ref)\n    f()\n";

    // A Property's storage passed to a `during static` parameter: an accessor's Borrow of static storage (2a0-D §3 rows 7 and 7').
    private const string Storage = "group Util\n    public func keepStatic(r: ref/i32 during static) -> i32 => r@follow\ngroup Api\n";

    // A callee's meet of its own Origins, whose Parameter atoms appear in its caller's rows (2a0-D's rv/meet-trans, I-1).
    private const string MeetTrans = "func put(slot: uniq/(ref/i32 during (a and b)), v: ref/i32 during (a and b)) -> ()\n    slot@follow = v\n" +
        "func mt(x: ref/i32, y: ref/i32) -> i32\n    var t = x\n    put(t@uniq, y)\n    return 0\n";

    // SPEC 15.6.5: a finite Origin never outlives a fixed one, so each chain from a finite source into a fixed Origin is a problem at its
    // last edge's site (DECISIONS R4-M113's Site rule: a row's Site, or the Ref's statement for a containment edge), whether or not that
    // site is reachable (j-unreach), through a local region (j-region, m-scope), a nested function's own body (n-inner), a guard candidate
    // and the subject it binds (rv/guard-annot), an environment slot (rv/env-store), the destruction receiver's part and its helper borrow
    // (G161), shared and exclusive layers a Ref is reached through (SPEC 15.6.3's containment), an object's own step, a mutable static's
    // storage (row 7), and FailedClause's body with its clause valid (ADependentEndFormsNoEdgeAndNoProblem's counterpart).
    [Theory]
    [InlineData("func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n", "return local@ref")]
    [InlineData("func bad(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n    view = local@ref\n    return view\n", "view")]
    [InlineData("func u(x: ref/i32, c: bool) -> ref/i32 during x\n    if c\n        return x\n    else\n        return x\n    let t = 1\n    return t@ref\n", "return t@ref")]
    [InlineData("func f(x: ref/i32, c: bool) -> ref/i32 during x\n    var r = x\n    if c\n        let local = 1\n        r = local@ref\n    return r\n", "r")]
    [InlineData("func use(bound: ref/i32) -> i32\n    func inner() -> ref/i32 during bound\n        let t = 1\n        return t@ref\n    return 0\n", "return t@ref")]
    [InlineData(Guard, "v@ref", "v@ref")]
    [InlineData(EnvStore, "x@ref")]
    [InlineData(Destructors, "self.buffer@ref", "whole.buffer@ref")]
    [InlineData("func f(p: ref/i32) -> ref/i32 during p\n    let local: i32 = 1\n    let r = local@ref\n    let rr = r@ref\n    return rr@follow@follow@ref\n", "return rr@follow@follow@ref")]
    [InlineData("func f(p: ref/i32) -> ref/i32 during p\n    var local: i32 = 1\n    var r = local@uniq\n    let rr = r@uniq\n    return rr@follow@follow@ref\n", "rr@follow@follow@ref", "rr@follow@follow@ref")]
    [InlineData("struct Cell\n    public var value: i32\n    public init(value: i32) => self.value = value\nfunc own(input: ref/i32) -> ref/i32 during input\n    let h: obj/Cell = Cell.init(value: 5)@obj\n    return h.value@ref\n", "return h.value@ref")]
    [InlineData(Storage + "    public var item: i32 = 0\n        get() -> i32 => Util.keepStatic(storage@ref)\n        set(value: i32) -> () => storage = value\n", "storage@ref")]
    [InlineData(ValidClause, "local@ref")]
    public void AFiniteChainIntoAFixedOriginIsRefutedAtItsLastEdge(string source, params string[] sites)
        => Assert.Equal([.. sites.Select(static x => $"{nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd)} {x}")], Origins(MirBuildTest.Compile(source + Main)));

    // DIAGNOSTICS 4.3, 2a0-D D18: two finite sources into one fixed Origin are two problems at one subject, told apart by their Reasons,
    // each with its last edge (Relation: the row's index in the body's view) and its Anchor (the first Ref whose Origin is the source).
    [Fact]
    public void TwoFiniteSourcesIntoOneFixedOriginAreTwoProblems()
    {
        var c = MirBuildTest.Compile("func two(x: ref/i32, c: bool) -> ref/i32 during x\n    let l1 = 1\n    let l2 = 2\n    var r = x\n" +
            "    if c => r = l1@ref else => r = l2@ref\n    return r\n" + Main);
        var program = c.BuildMir();
        var found = Found(program).ToArray();
        Assert.Equal(2, found.Length);
        Assert.NotSame(found[0].Problem.Reason.Longer, found[1].Problem.Reason.Longer);
        Assert.Same(found[0].Problem.Reason.Shorter, found[1].Problem.Reason.Shorter);
        Assert.Equal(
            ["r: Relation r 3, Anchor l1@ref -1", "r: Relation r 3, Anchor l2@ref -1"],
            found.Select(x => $"{Line(c, x.Problem.Subject)}: {string.Join(", ", program.Problems.Facts(x.Index).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)} {f.Row}"))}"));
    }

    // SPEC 15.6.5 and 15.6.3: valid counterparts form no Refuted chain: a parameter's Borrow passed on, a Field's Borrow read through its
    // binder, a Reborrow through shared and exclusive layers whose Origins the result names, a Slice element's Borrow (E6), a raw Deref,
    // which stops containment (a-wf), a local region holding only a local's Borrow, and an immutable static's storage passed to a
    // `during static` parameter (row 7', Inferred; SPEC 11.8), which the default pipeline rejects (PLAN G197's residual, ledger (a)).
    [Theory]
    [InlineData("func pass(x: ref/i32) -> ref/i32 during x\n    let y = x\n    return y\n")]
    [InlineData("struct P {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\nfunc get(p: ref/P) -> ref/i32 during p.a\n    return p.r\n")]
    [InlineData("func shallow(x: ref/(ref/i32 during a)) -> ref/i32 during a\n    return x@follow\n")]
    [InlineData("func f(p: uniq/(uniq/i32 during a) during b) -> ref/i32 during b\n    return p@follow@follow@ref\n")]
    [InlineData("func first(values: Slice<i32> during s) -> ref/i32 during s\n    return values[0]@ref\n")]
    [InlineData("func viaAnchor(p: raw/(ref/i32 during a), anchor: ref/i32 during r) -> ref/(ref/i32 during a) during r\n    unsafe => return (*p)@ref\n")]
    [InlineData("func ok() -> i32\n    let a = 1\n    var r = a@ref\n    return r@follow\n")]
    [InlineData(Storage + "    public let item: i32 = 0\n        get() -> i32 => Util.keepStatic(storage@ref)\n")]
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

    // 2a0-D §3 row 9 and D19: a row with an Unclassed end forms no edge and is the located limit at its Site, whatever Binding's
    // completeness (DECISIONS R4-M113): a callee's own Parameter atoms in a caller's rows (I-1, PLAN G195; meet-trans, which Binding rejects
    // by its own relation record), and a Closure header's omitted inner Origin (PLAN G216, decided by 2c-D), also beside an unrelated
    // Binding error, which leaves the limit independent. An end a Binding failure left Unbound is Dependent instead (R4-M118).
    [Theory]
    [InlineData(MeetTrans, "t@uniq")]
    [InlineData(ClosureHeader + "    return h(x@ref@ref)\n", "ref/(ref/i32)")]
    [InlineData(ClosureHeader + "    let m: bool = 3\n    return h(x@ref@ref)\n", "ref/(ref/i32)")]
    public void AnUnclassedEndIsTheLocatedLimit(string source, params string[] sites)
        => Assert.Equal([.. sites.Select(static x => $"{nameof(DiagnosticCode.Unsupported_Kd)} {x}")], Origins(MirBuildTest.Compile(source + Main)));

    // SPEC 23.1.2 (DECISIONS R4-M118): an end a Binding failure left Unbound is Dependent, and an edge with a Dependent end forms nothing and
    // records nothing, Binding's own record explaining the rejection: a local clause naming an unknown Origin, a repeated binding set and
    // the meet of its slots, a `var`'s binding set with no initializer, a containment edge through a Dependent layer, and a Dependent meet
    // at the shorter end whose slot's Type occurrence a later pass resolves.
    [Theory]
    [InlineData(FailedClause)]
    [InlineData(RepeatedSet)]
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n" +
        "func f(x: ref/i32)\n    var pending: V<i32>{p}\n        origin p.source outlives x\n")]
    [InlineData(Held + "func f(a: ref/i32) -> i32\n    let h0 = C.init(a)\n    let h: C{x} = h0@move\n        origin x.s outlives zz\n" +
        "    let q = h.r@follow@ref\n    return q@follow\n")]
    [InlineData(Held + "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    let h0 = C.init(b)\n    let h: C{x} = h0@move\n" +
        "        origin x.s outlives zz\n    let m: ref/i32 during (x.s and b) = b\n    return m\n")]
    public void ADependentEndFormsNoEdgeAndNoProblem(string source)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Empty(Origins(c));
    }

    // SPEC 23.1.2 and MeetNode's rule (2a0-D D15): at the longer end each operand of a meet must outlive the shorter end, so a classed
    // operand's edge beside a Dependent one is an independent judgment: a local's Borrow returned `during a` through `pick`'s meet with the
    // failed local's Field, directly (m7) or through an inferred local (m8), is the Refuted chain it is with the clause valid.
    [Theory]
    [InlineData("zz", "    return q\n", "return q")]
    [InlineData("a", "    return q\n", "return q")]
    [InlineData("zz", "    let w = q\n    return w\n", "return w")]
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
    // the view pools): Refuted chains, Unknown chains and edges, an Equality row failing both ways, a pair layer's two classes, a Field
    // initializer's layer oracle and Dependent ends (FailedClause).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmJudgmentAllocatesNothing()
    {
        var c = MirBuildTest.Compile("func region(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n" +
            "    view = local@ref\n    return view\nfunc two(x: ref/i32, c: bool) -> ref/i32 during x\n    let l1 = 1\n    let l2 = 2\n" +
            "    var r = x\n    if c => r = l1@ref else => r = l2@ref\n    return r\nfunc layers(p: ref/i32) -> ref/i32 during p\n" +
            "    var local: i32 = 1\n    var r = local@uniq\n    let rr = r@uniq\n    return rr@follow@follow@ref\n" + R7Inv + Phantom +
            "func pair<s/T>(x: ref/i32, y: ref/i32) -> i32\n    s is owner or ref\n    let k: Option<s/(ref/i32 during x) during y> = .None\n    return 0\n" +
            "func chain(x: ref/i32, y: ref/i32) -> ref/i32 during y\n    var r = x\n    r = y\n    return r\n" + LayerCount + FailedClause + Main);
        var program = c.BuildMir();
        var origins = new MirOrigins(c.Types, new MirCases(c.Types));
        var bodies = program.Bodies.ToArray();
        void Run()
        {
            foreach (var body in bodies)
            {
                origins.Judge(body, MirCases.Count(body));
            }
        }

        Run();
        Assert.Equal(0, AllocationMeasurement.Measure(Run));
    }

    // The Origin stratum's problems of a program's own bodies, as code and subject.
    private static List<string> Origins(Compilation c)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return [.. Found(program).Select(x => $"{x.Problem.Code} {Line(c, x.Problem.Subject)}")];
    }

    // The problems of a program's own bodies with an Origin stratum's code, and their indices.
    private static IEnumerable<(MirProblem Problem, int Index)> Found(MirProgram program)
        => program.Problems.All.ToArray().Select((x, i) => (x, i))
            .Where(static x => x.x.Subject.Module == 0 && x.x.Code is DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd
                or DiagnosticCode.Unsupported_Kd or DiagnosticCode.OwnershipStorageLimit_Kd);

    // A program's body judged by a stratum of its own, for its work and containment edges.
    private static (MirOrigins Origins, MirBody Body, Compilation C) Judged(string source, string name)
    {
        var c = MirBuildTest.Compile(source + Main);
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
        var origins = new MirOrigins(c.Types, new MirCases(c.Types));
        origins.Judge(body, MirCases.Count(body));
        return (origins, body, c);
    }

    private static string Line(Compilation c, SourceRef site) => c.SyntaxOf(site).ToString()!.Split('\n')[0].Trim();
}
