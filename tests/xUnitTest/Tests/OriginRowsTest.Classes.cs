// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The C1-F4 view's atom classes (2a0-D §3 with DECISIONS R4-M101 E5): one class per precedence row and slot role.
public sealed partial class OriginRowsTest
{
    // The destruction receiver's part and its helper borrow (G161's two forms, with C0c's ReceiverSlot).
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

    // A callee's meet of its own Origins, judged with its Parameter atoms (2a0-D's rv/meet-trans-ok, PLAN G195).
    private const string MeetTrans = "func put(slot: uniq/(ref/i32 during (a and b)), v: ref/i32 during (a and b)) -> ()\n    slot@follow = v\n" +
        "func mt(x: ref/i32, y: ref/i32) -> i32\n    var t = x\n    put(t@uniq, y)\n    return 0\n";

    // Explicit in-group static Borrows, mutable and immutable, and the mutable one inside a Closure (2a0-D §3 rows 6, 7, 7').
    private const string Statics = "group State\n    public var count: i32 = 0\n    public let limit: i32 = 10\n" +
        "    func reads() -> i32\n        let c = count@ref\n        let l = limit@ref\n        let f = func [c] () -> i32\n            let own = count@ref\n" +
        "            return c@follow + own@follow\n        return f() + l@follow\n";

    // Row 6 through a Closure in a default: the template's prepared input is Fixed in the Closure, a root of its evaluating body.
    [Fact]
    public void ADefaultsPreparedInputIsFixedInAClosureItEvaluates()
    {
        var c = MirBuildTest.Compile(Pick + "func h(n: i32, f: () -> i32 = func [] () => pick(7@ref, 8@ref)) -> i32 => f()\n" + Main);
        var closure = Bodies(c).Single(IsProgramClosure);
        var h = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "h");
        var input = new BoundOrigin(OriginKind.Projection, h, 0);
        Assert.Equal(OriginAtomKind.Fixed, c.Types.OriginAtom(BodyOf(closure.Key), input, h).Kind);
        Assert.Equal(OriginAtomKind.Finite, c.Types.OriginAtom(new ExecutableBody(h.SymbolOf(), 1), input, h).Kind);
    }

    // 2a0-D §3 with DECISIONS R4-M101 E5: one class per precedence row and slot role, on rows the programs form.
    [Theory]
    [InlineData(Destructors, "drop", "self.buffer@ref", "Finite", "Receiver")]
    [InlineData(Destructors, "drop", "whole.buffer@ref", "Finite", "Helper")]
    [InlineData(EnvStore, "", "x@ref", "Finite", "Environment")]
    [InlineData(Guard, "g", "v@ref", "Finite", "Place")]
    [InlineData(Pick + "func g(n: i32, r: i32 = pick(n@ref, 5@ref)) -> i32 => r\n", "g", "n@ref", "Finite", "Parameter")]
    [InlineData(Pick + "func g(n: i32, r: i32 = pick(n@ref, 5@ref)) -> i32 => r\n", "g", "5@ref", "Finite", "Place")]
    [InlineData("func total(values: Slice<i32>) -> i32\n    var sum = 0\n    for v in values\n        sum = sum + v@follow\n    return sum\n", "total", "$for.iterator", "Finite", "Place")]
    public void TheLongerEndOfARowTakesItsClass(string source, string body, string site, string kind, string role)
    {
        var (slice, row) = RowAt(source, body, site);
        Assert.Equal((kind, role), (slice.Atoms[row.Longer].Kind.ToString(), slice.Atoms[row.Longer].Role.ToString()));
    }

    // Rows 1, 2', 8 and 9 on the shorter ends: B's contract and static are Fixed, a meet with a Finite operand an Inferred node, a local's
    // inference Inferred, a callee's own meet of Parameter atoms forms no edge (Unclassed, PLAN G195's located limit).
    [Theory]
    [InlineData(EnvStore, "", "x@ref", "Fixed")]
    [InlineData(Pick + "func g(a: i32 = pick(3@ref, 4@ref)) -> i32 => a\n", "g", "3@ref", "Inferred")]
    [InlineData(MeetTrans, "mt", "x", "Inferred")]
    [InlineData(MeetTrans, "mt", "t@uniq", "Unclassed")]
    [InlineData("func needsStatic(x: ref/i32 during static) -> i32 => x@follow\nfunc caller() -> i32\n    let local = 3\n    return needsStatic(local@ref)\n", "caller", "local@ref", "Fixed")]
    public void TheShorterEndOfARowTakesItsClass(string source, string body, string site, string kind)
    {
        var (slice, row) = RowAt(source, body, site);
        Assert.Equal(kind, slice.Atoms[row.Shorter].Kind.ToString());
    }

    // Rows 6, 7 and 7': a mutable static's Borrow formed in B is Finite, an immutable one's Inferred, and the enclosing body's Borrow a
    // Closure captures is Fixed there while the Closure's own Borrow is Finite (2a0-D's rv/st-both: two atoms, per Borrow site).
    [Fact]
    public void AStaticsBorrowIsClassedByItsSiteAndMutability()
    {
        var c = MirBuildTest.Compile(Statics + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var program = c.BuildMir();
        var reads = BodyOf(program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "reads").Key);
        var closure = BodyOf(program.Bodies.ToArray().Single(IsProgramClosure).Key);
        var (count, limit, own) = (BorrowAt(c, "count@ref", 0), BorrowAt(c, "limit@ref", 0), BorrowAt(c, "count@ref", 1));
        Assert.Equal(OriginAtomKind.Finite, c.Types.OriginAtom(reads, count.Origin, count.Site).Kind);
        Assert.Equal(OriginAtomKind.Inferred, c.Types.OriginAtom(reads, limit.Origin, limit.Site).Kind);
        Assert.Equal(OriginAtomKind.Fixed, c.Types.OriginAtom(closure, count.Origin, count.Site).Kind);
        Assert.Equal(OriginAtomKind.Finite, c.Types.OriginAtom(closure, own.Origin, own.Site).Kind);
        Assert.NotSame(count.Origin, own.Origin);
    }

    // Rows 3, 7 and 7' for an accessor's `storage` (DECISIONS R4-M101, BU's B1): a root of its Property's accessors, classed as a static
    // Borrow formed there by mutability (SPEC 11.3.1, 11.8), or Inferred for an instance Property (it reads through the receiver).
    [Theory]
    [InlineData("group Api\n    public var item: i32 = 0\n        get() -> i32 => Util.pick(storage@ref, 5@ref)\n        set(value: i32) -> () => storage = value\n", "Finite")]
    [InlineData("group Api\n    public let item: i32 = 0\n        get() -> i32 => Util.pick(storage@ref, 5@ref)\n", "Inferred")]
    [InlineData("struct Api\n    public var item: i32 = 0\n        get() -> i32 => Util.pick(storage@ref, 5@ref)\n        set(value: i32) -> () => storage = value\n    public init() => ()\n", "Inferred")]
    public void AnAccessorsStorageIsClassedByItsProperty(string source, string kind)
    {
        var c = MirBuildTest.Compile(Util + source + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var getter = Bodies(c).Select(x => c.Types.OriginRows(BodyOf(x.Key))).Single(static x => x.Rows.Any(static y => First(y.Site) == "storage@ref"));
        var storage = getter.Atoms[getter.Rows.First(static x => First(x.Site) == "storage@ref").Longer];
        Assert.Equal((kind, ProjectionRole.Storage), (storage.Kind.ToString(), storage.Role));
        Assert.Empty(c.Types.OriginFindings);
    }

    // Row 3's referent clause for a Place: the atom PlaceOrigin names for a Borrow of an expression read as its referent is Inferred.
    [Fact]
    public void APlaceReadThroughAReferenceIsInferred()
    {
        var c = MirBuildTest.Compile("func g(r: ref/i32) -> i32\n    let y: i32 = r\n    return y\n" + Main);
        var g = BodyOf(Bodies(c).Single(static x => x.Key.Decl?.Name == "g").Key);
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x is IdentifierNameKoto && First(x) == "r" && c.Binding.ReadsReferent(x));
        var atom = c.Types.OriginAtom(g, new BoundOrigin(OriginKind.Projection, read, 0) { Body = g }, read);
        Assert.Equal((OriginAtomKind.Inferred, ProjectionRole.Place), (atom.Kind, atom.Role));
    }

    // Row 5 and the receiver's part (E5, DECISIONS R4-M103): a Closure literal's value is Fixed in its own body and a Finite temporary of
    // the body evaluating it; a Closure's call result is Fixed in it; a constructor's receiver is a Finite root of the constructor.
    [Fact]
    public void AClosuresValueAndAReceiverAreClassedByTheirRole()
    {
        var c = MirBuildTest.Compile(EnvStore + Destructors + Main);
        var program = c.BuildMir();
        var bodies = program.Bodies.ToArray();
        var g = BodyOf(bodies.Single(static x => x.Key.Decl?.Name == "g").Key);
        var closure = bodies.Single(IsProgramClosure);
        var literal = (FunctionKoto)closure.Key.Decl!.Declaration;
        var value = new BoundOrigin(OriginKind.Projection, literal, Binding.ClosureValueSlot);
        Assert.Equal(OriginAtomKind.Fixed, c.Types.OriginAtom(BodyOf(closure.Key), value, literal).Kind);
        Assert.Equal(OriginAtomKind.Finite, c.Types.OriginAtom(g, value, literal).Kind);
        Assert.Equal(OriginAtomKind.Fixed, c.Types.OriginAtom(BodyOf(closure.Key), new(OriginKind.Projection, literal, Binding.CallResultSlot), literal).Kind);
        var init = bodies.First(IsProgramConstructor);
        var constructor = (FunctionKoto)init.Key.Decl!.Declaration;
        var receiver = c.Types.OriginAtom(BodyOf(init.Key), new(OriginKind.Projection, constructor, Binding.ReceiverSlot), constructor);
        Assert.Equal((OriginAtomKind.Finite, ProjectionRole.Receiver), (receiver.Kind, receiver.Role));
    }

    // The row of a body (by its definition's name; "" for the Closure) at a site.
    private static (Binding.OriginRowSet Slice, OriginRow Row) RowAt(string source, string body, string site)
    {
        var c = MirBuildTest.Compile(source + Main);
        var key = Bodies(c).First(x => (body.Length == 0 ? IsProgramClosure(x) : x.Key.Decl?.Name == body) && Has(c, x.Key, site)).Key;
        var slice = c.Types.OriginRows(BodyOf(key));
        return (slice, slice.Rows.First(x => First(x.Site) == site));
    }

    private static bool Has(Compilation c, MirDefKey key, string site) => c.Types.OriginRows(BodyOf(key)).Rows.Any(x => First(x.Site) == site);

    // The n-th Borrow at a site text and its Type's Origin, as the HIR publishes it.
    private static (Koto Site, BoundOrigin Origin) BorrowAt(Compilation c, string site, int n)
    {
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).Where(x => x is ConversionKoto && First(x) == site).ElementAt(n);
        return (node, ((BoundType)HirTables.Meaning(node)!).Origin!);
    }
}
