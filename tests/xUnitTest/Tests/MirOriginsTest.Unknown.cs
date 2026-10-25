// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// A5's Origin stratum, Unknown part (2a0-D §6.3, §6.4; docs/dev/DECISIONS.md R4-M114): a chain or edge between fixed Origins the body's
// premises do not derive is UnprovenOriginRelation_Kd at its last edge's site, judged with its part's oracle under the borrow binders its
// class's views share; a class's arena is admitted by MirDataflow.Within.
public sealed partial class MirOriginsTest
{
    // audit-origins row 7 (r7-inv): an invariant `==` at a local's annotation whose one direction the clause proves, and the annotation's
    // well-formedness against its omitted outer Origin's declaration-time candidate.
    private const string R7Inv = "func f(x: uniq/(ref/i32 during a), y: uniq/(ref/i32 during b)) -> ()\n    origin b outlives a\n" +
        "    let z: uniq/(ref/i32 during a) = y\n";

    // A Phantom slot is invariant (SPEC 15.3.5, S1015 item 28): passing `Tag{p}` as `Tag{q}` needs `a == b`.
    private const string Phantom = "struct Tag {s}\n    public let n: i32\n    public init(n: i32) => self.n = n\n" +
        "func f(a: ref/i32, b: ref/i32, x: Tag{p}, k: (Tag{q}) -> i32) -> i32\n    origin p.s == a\n    origin q.s == b\n    return k(x@move)\n";

    // The ledger's e3312717bde0: two well-formedness rows of one call's result.
    private const string TwoRows = "func pk(p: ref/i32, q: ref/i32, r: ref/i32, keep: uniq/(ref/i32 during p)) -> " +
        "(Option<ref/(ref/i32 during q) during p>, Option<ref/(ref/i32 during r) during p>)\n    return (.None, .None)\n" +
        "func caller(x: ref/i32, y: ref/i32, h: ref/i32 during s, keep: uniq/(ref/i32 during s)) -> i32\n    let r = pk(h, x, y, keep)\n    return 0\n";

    // A local's Type stored through a callable Field (the ledger's cd1535825466, 597510880fb0 and cab8c776076f).
    private const string Holder = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n";

    private const string Callback = "struct C {s}\n    public let call: (ref/i32 during s) -> i32\n    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\n";

    // A meet at the longer end of a well-formedness row (the ledger's bc801917a239).
    private const string Meet = "func g(a: ref/i32, b: ref/i32, x: ref/i32) -> i32\n    let k: Option<ref/(ref/i32 during (a and b)) during x> = .None\n    return 0\n";

    // A local Closure's parameter stored into its `var` capture of another Closure (the ledger's 8e4cca7290b1).
    private const string Closures = "func use(x: ref/i32) -> ref/i32 during x\n    let start: ref/i32 = x\n" +
        "    var inner = func [var start] (n: ref/i32 during x) -> ref/i32 during x\n        let old = start\n        start = n\n        return old\n" +
        "    var outer = func [var inner] (a: ref/i32) -> i32\n        let got = inner(a)\n        return got@follow\n    return inner(x)\n";

    // A clause bound of a local's contravariant slot (the ledger's cab8c776076f).
    private const string ClauseBound = Callback + "func f(a: ref/i32, b: ref/i32, c: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    origin c outlives a\n" +
        "    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        origin x.s outlives b\n    return h.call(c)\n";

    // A nested function's result naming the enclosing function's fixed Origins (n-pass).
    private const string NestedPass = "func use(source: uniq/i32, bound: ref/i32) -> i32\n    origin source outlives bound\n" +
        "    func pass(value: uniq/i32 during bound) -> uniq/i32 during source\n        return value\n    return 0\n";

    // An Iterator's item Reborrowed through `self` from an exclusive Field (x-skip).
    private const string IteratorItem = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n" +
        "    let value: uniq/i32 during source\n    public func next(self: uniq/Self) -> Option<ref/i32 during source> => .Some(self.value)\n";

    // A call's substituted relation (r7-subst).
    private const string Substituted = "func keep(x: uniq/(ref/i32 during a), y: ref/i32 during b) -> ()\n    origin b outlives a\n    x@follow = y\n" +
        "func use(p: uniq/(ref/i32 during c), q: ref/i32 during d) -> ()\n    keep(p, q)\n";

    // A Field initializer's written Type argument relates the struct's Origins, which only its constructor's clause orders (SPEC 6.3.3), and
    // the same relation in the constructor's own body.
    private const string LayerHead = "group Util\n    public func count<T>() -> i32 => 0\n    public func none<T>() -> Option<T> => .None\n" +
        "struct S {a, b}\n    public let x: ref/i32 during a\n    public let y: ref/i32 during b\n";

    private const string LayerInit = "    public init(x: ref/i32 during a, y: ref/i32 during b)\n        origin a outlives b\n        self.x = x\n        self.y = y\n";

    private const string LayerCount = LayerHead + "    public let k: i32 = Util.count<ref/(ref/i32 during a) during b>()\n" + LayerInit;

    private const string LayerBody = LayerHead + LayerInit + "        let k: Option<ref/(ref/i32 during a) during b> = Util.none()\n";

    // SPEC 15.6.5: a chain or edge between fixed Origins that the premises do not derive is Unknown at its last edge's site, Reason (fixed
    // source, fixed target): both rows of r7-inv (one direction of its `==`), a chain through a local region (the j-f chain), a meet at the
    // longer end split per operand (bc801917a239), two well-formedness rows of one call (e3312717bde0), a Phantom slot's `==` failing both
    // ways (one problem with Equality set), a local's callable store (cd1535825466, 597510880fb0, cab8c776076f), a Closure's parameter fit
    // (8e4cca7290b1), an environment binding's Borrow stored under a borrowed receiver, which lies in the receiver Origin ρ (rv/env-store,
    // fixed in the Closure's body: Unknown, not Refuted, since 2c-3B; DECISIONS R4-M126 P5, R4-M133), a Reborrow through a local's layers,
    // which Binding names by their meet (p1: one problem at the return; rejected on both pipelines since G226, DECISIONS BU-27, closing PLAN
    // G192), and a Field initializer's relation that only the constructor's clause would prove: the layer's part is judged under the Type's
    // public premises (SPEC 6.3.3; DECISIONS R4-M101 E4, R4-M110, R4-M114).
    [Theory]
    [InlineData(R7Inv, "uniq/(ref/i32 during a): a > in1", "let z: uniq/(ref/i32 during a) = y: a > b")]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 during y\n    var r = x\n    r = y\n    return r\n", "r: in0 > in1")]
    [InlineData(Meet, "ref/(ref/i32 during (a and b)) during x: in0 > in2", "ref/(ref/i32 during (a and b)) during x: in1 > in2")]
    [InlineData(TwoRows, "pk(h, x, y, keep): in0 > s", "pk(h, x, y, keep): in1 > s")]
    [InlineData(Phantom, "x@move: in0 == in1")]
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h: H{x} = H.init(a)\n        origin x.s == b\n    return 0\n", "let h: H{x} = H.init(a): in0 > in1")]
    [InlineData(Holder + "func use(a: ref/i32, b: ref/i32, h0: H{p}, k: (H{q}) -> i32) -> i32\n    origin p.s == a\n    origin q.s == b\n    return k(h0@move)\n", "h0@move: in0 > in1")]
    [InlineData(ClauseBound, "origin x.s outlives b: in2 > in1")]
    [InlineData(Closures, "a: closure.in0 > in0")]
    [InlineData(EnvStore, "x@ref: closure.ρ > t")]
    [InlineData("func f(p: uniq/(uniq/i32 during a) during b) -> uniq/i32 during a\n    var q = p\n    return q@follow@follow@uniq\n", "q@follow@follow@uniq: b > a")]
    [InlineData(LayerCount, "ref/(ref/i32 during a) during b: a > b")]
    public void AChainBetweenFixedOriginsThePremisesDoNotDeriveIsUnknown(string source, params string[] expected)
        => Assert.Equal(expected, Unknowns(MirBuildTest.Compile(source + Main)));

    // SPEC 15.6.5's valid counterparts: the clause proves both directions of the `==` (r7-inv-eq), the result names the meet of the
    // region's sources, a meet's operands outlive the target, a Reborrow's containment in the layers its name meets (c-cut, SPEC 15.6.3's
    // cut), a pair layer's well-formedness under its premise, and the constructor's own body judged under its clause.
    [Theory]
    [InlineData("func f(x: uniq/(ref/i32 during a), y: uniq/(ref/i32 during b)) -> ()\n    origin a == b\n    let z: uniq/(ref/i32 during a) = y\n")]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 during (x and y)\n    var r = x\n    r = y\n    return r\n")]
    [InlineData("func g(a: ref/i32, b: ref/i32, x: ref/i32) -> i32\n    origin a outlives x\n    origin b outlives x\n" +
        "    let k: Option<ref/(ref/i32 during (a and b)) during x> = .None\n    return 0\n")]
    [InlineData("func cut(r: uniq/(ref/(uniq/i32 during a) during b) during c) -> ref/i32 during b\n    return r@follow@follow@follow@ref\n")]
    [InlineData("func f<s/T>(x: ref/i32, y: ref/i32) -> i32\n    s is owner or ref\n    origin x outlives y\n    let k: Option<s/(ref/i32 during x) during y> = .None\n    return 0\n")]
    [InlineData(LayerBody)]
    public void ARelationThePremisesDeriveHasNoUnknown(string source)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.Empty(Origins(c));
    }

    // D1: a relation whose ends are fixed where Binding forms it is Binding's judgment until R7 and forms no row, and its failed node is
    // Opaque on the MIR, so the stratum forms no problem for it: j-f, j-pick (each case), c-detach (SPEC 15.5), c-box, c-peekU, c-pair (each
    // case), n-pass, x-skip (an Iterator's item Reborrowed through `self`), p1's direct form and r7-subst's call.
    [Theory]
    [InlineData("func f(x: ref/i32, y: ref/i32) -> ref/i32 during y\n    let r = x\n    return r\n", "return r")]
    [InlineData("struct Box<E>\n    public let item: E\nfunc pick<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c\n    s is owner or ref\n    return x\n", "return x")]
    [InlineData("struct UniqView<T> {source}\n    let data: uniq/T during source\n    func detach(self: uniq/Self) -> uniq/T during self.source\n        return self.data\n", "return self.data")]
    [InlineData("struct Box<E>\n    public let item: E\nfunc f(b: uniq/Box<uniq/i32 during a> during c) -> uniq/i32 during a\n    return b.item@follow@uniq\n", "return b.item@follow@uniq")]
    [InlineData("func peekU(r: ref/(uniq/i32 during a) during b) -> ref/i32 during a\n    return r@follow@follow@ref\n", "return r@follow@follow@ref")]
    [InlineData("func pd<s/T>(p: s/(uniq/i32 during a) during b) -> uniq/i32 during a\n    s is owner or uniq\n    return p@follow@follow@uniq\n", "return p@follow@follow@uniq")]
    [InlineData(NestedPass, "return value")]
    [InlineData(IteratorItem, "self.value")]
    [InlineData("func f(p: uniq/(uniq/i32 during a) during b) -> uniq/i32 during a\n    return p@follow@follow@uniq\n", "return p@follow@follow@uniq")]
    [InlineData(Substituted, "q")]
    public void ARelationBindingJudgesDirectlyIsNotTheStratums(string source, string site)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenOriginRelation_Kd && x.Node.ToString()!.Split('\n')[0].Trim() == site);
        Assert.Empty(Found(c.BuildMir()));
    }

    // SPEC 15.6.5 (with pairs, S1015 item 25): a pair borrow layer's well-formedness is required only in the case with an outer Origin, so
    // its Unknown carries that view alone; views with equal rows form one class (2 here).
    [Fact]
    public void APairLayersWellFormednessIsUnknownOnlyInItsCase()
    {
        var (origins, _, c) = Judged("func f<s/T>(x: ref/i32, y: ref/i32) -> i32\n    s is owner or ref\n    let k: Option<s/(ref/i32 during x) during y> = .None\n    return 0\n", "f");
        var found = Assert.Single(Found(c.BuildMir()));
        Assert.Equal((DiagnosticCode.UnprovenOriginRelation_Kd, 2UL), (found.Problem.Code, found.Problem.Views));
        Assert.Equal(2, origins.Classes);
    }

    // SPEC 15.6.3 and 13.5.5 (a pair layer is absent for `s = owner`): a Reborrow through a local's pair layer is contained in the
    // layer's Origin only in the case that makes it a borrow, so returning it `during x` needs the parameter's outer Origin to outlive x
    // in the `uniq` case alone; in the owner case the layer forms no Deref step and the Reborrow is contained in x only.
    [Fact]
    public void APairLayersContainmentIsItsBorrowCasesOnly()
    {
        var c = MirBuildTest.Compile("func pd<s/T>(x: ref/i32, v: s/(uniq/i32 during x)) -> ref/i32 during x\n    s is owner or uniq\n    var w = v\n" +
            "    let q = w@follow@follow@ref\n    return q\n" + Main);
        var found = Assert.Single(Found(c.BuildMir()));
        Assert.Equal((DiagnosticCode.UnprovenOriginRelation_Kd, "q", 2UL), (found.Problem.Code, Line(c, found.Problem.Subject), found.Problem.Views));
    }

    // j-bits: 70 fixed sources through one local region into one target take two bitset words; each source the premises do not order
    // is its own problem at the one subject, the target's own source none.
    [Fact]
    public void SeventyFixedSourcesAreSixtyNineProblems()
    {
        var text = new StringBuilder("func bits(").AppendJoin(", ", Enumerable.Range(0, 70).Select(static i => $"p{i}: ref/i32")).Append(") -> ref/i32 during p0\n    var v = p0\n");
        for (var i = 1; i < 70; i++)
        {
            text.Append($"    v = p{i}\n");
        }

        var unknowns = Unknowns(MirBuildTest.Compile(text.Append("    return v\n").Append(Main).ToString()));
        Assert.Equal(Enumerable.Range(1, 69).Select(static i => $"v: in{i} > in0").Order(StringComparer.Ordinal), unknowns.Order(StringComparer.Ordinal));
    }

    // R4-M114 (E4 for MIR atoms): a Field initializer's Ref, evaluated in the constructor's body, is the layer's part, its constructor
    // body's own Ref is not, so containment edges are judged with their part's oracle.
    [Fact]
    public void AFieldInitializersRefIsTheLayersPart()
    {
        var c = MirBuildTest.Compile("group Util\n    public func first(r: ref/i32) -> i32 => r@follow\nstruct S {a}\n    public let r: ref/i32 during a\n" +
            "    public let n: i32 = Util.first((5@ref@ref)@follow@ref)\n    public init(r: ref/i32 during a)\n        self.r = r\n" +
            "        let m = (6@ref@ref)@follow@ref\n        _ = m\n" + Main);
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(x => x.Key.Decl?.Name == "init" && x.Source.Module == 0);
        var key = body.Key.Body;
        var parts = new List<string>();
        foreach (var statement in body.Statements)
        {
            if (statement is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Ref, Type.Origin: { } origin } } && body.Subject(statement.Source) is var site)
            {
                parts.Add($"{Line(c, site)} {c.Types.OriginRows(key).InLayer(c.Types.OriginAtom(key, origin, site))}");
            }
        }

        Assert.Contains("(5@ref@ref)@follow@ref True", parts);
        Assert.DoesNotContain(parts, static x => x.StartsWith("(6@ref", StringComparison.Ordinal) && x.EndsWith("True", StringComparison.Ordinal));
    }

    // D20 (R4-M15a): the arena's words are admitted by the analyses' one byte limit, 64 MiB; at the counts of the smallest body over it
    // (65,536 inner components and 8,192 fixed sources) the stratum would skip the body with OwnershipStorageLimit_Kd. The largest test body
    // here needs about a thousand words (V16 (9) at N = 128), so no body binds within the test budget (2a0-D §10's shortfall).
    [Fact]
    public void TheArenaIsAdmittedByTheAnalysesLimit()
    {
        Assert.True(MirDataflow.Within(8L * 1024 * 1024));
        Assert.False(MirDataflow.Within((8L * 1024 * 1024) + 1));
        Assert.True(MirDataflow.Within(MirOrigins.ArenaWords(65_535, 0, 8_192)));
        Assert.False(MirDataflow.Within(MirOrigins.ArenaWords(65_536, 0, 8_192)));
        Assert.Equal(4, MirOrigins.ArenaWords(1, 3, 70));
    }

    // V16 A3 (9) (2a0-D §6.4): N chained locals and N signature Origins into one fixed target, with premises ordering them, keep the work
    // within c·(N_o + E_o·⌈F/64⌉ + Ft·(Pm + ⌈F/64⌉)) at N, 2N and 4N.
    [Fact]
    public void TheUnknownWorkScalesWithTheChain()
    {
        var sizes = new[] { 32, 64, 128 };
        var ratios = new double[sizes.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            var (origins, _, c) = Judged(Chained(sizes[i], string.Empty), "f");
            Assert.Empty(Origins(c));
            var n = sizes[i];
            ratios[i] = origins.Work / (double)((4 * n) + (2 * n * ((n + 63) / 64)));
        }

        Assert.All(ratios, static x => Assert.InRange(x, 0.1, 8));
    }

    // V16 "a 64-view pair body": six pair binders and one row masked by the first form two classes, and the work stays within c of the
    // body with one view.
    [Fact]
    public void SixtyFourViewsFormTwoClasses()
    {
        var binders = string.Join(", ", Enumerable.Range(0, 6).Select(static i => $"s{i}/T{i}"));
        var (one, _, _) = Judged(Chained(32, "    let w: Option<s0/(ref/i32 during p1) during p2> = .None\n", "<s0/T0>", "    s0 is ref\n"), "f");
        var cases = string.Concat(Enumerable.Range(0, 6).Select(static i => $"    s{i} is owner or ref\n"));
        var (many, _, _) = Judged(Chained(32, "    let w: Option<s0/(ref/i32 during p1) during p2> = .None\n", $"<{binders}>", cases), "f");
        Assert.Equal((1, 2), (one.Classes, many.Classes));
        Assert.True(many.Work <= (4 * one.Work) + 1024, $"work {one.Work}, {many.Work}");
    }

    // The Unknown problems of a program's own bodies: subject, ends and whether an Equality row failed both ways.
    private static List<string> Unknowns(Compilation c)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return [.. Found(program).Where(static x => x.Problem.Code == DiagnosticCode.UnprovenOriginRelation_Kd).Select(x =>
            $"{Line(c, x.Problem.Subject)}: {Name(x.Problem.Reason.Longer!)} {(x.Problem.Reason.Equality ? "==" : ">")} {Name(x.Problem.Reason.Shorter!)}")];
    }

    // An end as the tests write it: a named Origin by its name, an omitted input Origin by its index (a Closure's prefixed), a Closure's
    // call receiver Origin as closure.ρ, a meet by its operands.
    private static string Name(BoundOrigin origin) => origin.Kind switch
    {
        OriginKind.Intersection => "(" + string.Join(" and ", origin.Operands.Select(Name)) + ")",
        OriginKind.Input => (origin.Binder is FunctionKoto { IsAnonymous: true } ? "closure.in" : "in") + origin.InputIndex,
        OriginKind.Projection when origin.Slot == Binding.ClosureValueSlot => "closure.ρ",
        _ => origin.Name,
    };

    // N parameters whose Origins outlive p0, chained through N locals into the result during p0.
    private static string Chained(int n, string extra, string generics = "", string clauses = "")
    {
        var text = new StringBuilder($"func f{generics}(").AppendJoin(", ", Enumerable.Range(0, n + 1).Select(static i => $"p{i}: ref/i32")).Append(") -> ref/i32 during p0\n");
        text.Append(clauses);
        for (var i = 1; i <= n; i++)
        {
            text.Append($"    origin p{i} outlives p0\n");
        }

        text.Append(extra).Append("    var v1 = p0\n    v1 = p1\n");
        for (var i = 2; i <= n; i++)
        {
            text.Append($"    var v{i} = v{i - 1}\n    v{i} = p{i}\n");
        }

        return text.Append($"    return v{n}\n").ToString();
    }
}
