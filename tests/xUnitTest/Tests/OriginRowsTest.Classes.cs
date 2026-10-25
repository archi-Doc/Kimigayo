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

    // PLAN G216's h1 and h2: a Closure header omitting its parameter's inner Origin, not called and called; a Function Type annotation
    // omitting it, called; and a named function's Function Type parameter naming it, called with an argument whose own Type proves it.
    private const string HeaderOnly = "func g() -> i32\n    let h = func [] (p: ref/(ref/i32)) -> i32 => 1\n    return 0\n";

    private const string HeaderCalled = "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32)) -> i32 => p@follow@follow\n    let x = 5\n" +
        "    return h(x@ref@ref)\n";

    private const string FunctionTypeCalled = "func f() -> i32\n    let apply: (ref/(ref/i32)) -> i32 = func (value) => value@follow@follow\n" +
        "    let n = 7\n    let view = n@ref\n    return apply(view@ref)\n";

    private const string FunctionTypeParameter = "func g(a: ref/i32, cb: (ref/(ref/i32 during a)) -> i32) -> i32 => cb(a@ref)\n";

    // A Closure header's struct input, whose well-formedness each call discharges (CallBorrows).
    private const string StructInput = "struct S {a}\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n" +
        "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/S) -> i32 => p.x@follow\n    let s = S.init(q)\n    return h(s@ref)\n";

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

    // 2a0-D §3 with DECISIONS R4-M101 E5: one class per precedence row and slot role, on rows the programs form. Row 4, an environment
    // binding's own atom, resolves to the call receiver Origin ρ (row 5, Fixed) under a borrowed receiver and stays a Finite root under
    // `owner/Self` (DECISIONS R4-M126 P5, superseding the interim Finite in every receiver mode).
    [Theory]
    [InlineData(Destructors, "drop", "self.buffer@ref", "Finite", "Receiver")]
    [InlineData(Destructors, "drop", "whole.buffer@ref", "Finite", "Helper")]
    [InlineData(EnvStore, "", "x@ref", "Fixed", "ClosureValue")]
    [InlineData(EnvStoreConsuming, "", "x@ref", "Finite", "Environment")]
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
    // Closure captures is Fixed there while the Closure's own Borrow is Finite (2a0-D's rv/st-both: two atoms, per Borrow site). R4-M90
    // PQ-1 (DECISIONS BU-25): until P42 Binding reports these Borrows as the located Unsupported_Kd, the Closure capturing c resting on c's
    // record, so the source forms no static atom; rows 7 and 7' keep their witnesses through an accessor's storage
    // (AnAccessorsStorageIsClassedByItsProperty), and row 6's Closure case is restored there too
    // (AnAccessorsStorageCapturedByAClosureIsFixedThere).
    [Fact]
    public void AStaticsBorrowFormsNoAtomUntilP42()
    {
        var c = MirBuildTest.Compile(Statics + Main);
        Assert.Equal(["Unsupported_Kd count@ref", "Unsupported_Kd limit@ref"], c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}"));
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

    // Row 6's Closure case through an accessor's storage (DECISIONS BU-25's exception): the getter's Borrow of its Property's storage is
    // classed by mutability there (rows 7 and 7'), and a Closure the getter evaluates sees it, through its capture of that Borrow, as a root
    // of its evaluating body, Fixed (row 6). A Closure's own Borrow of `storage` has a static root, Binding's located Unsupported_Kd
    // (StaticStorageAccess, SPEC 7.7.2), and an accessor's storage is one atom per Property, so rv/st-both's per-Borrow-site pair waits
    // for P42's static Borrows.
    [Theory]
    [InlineData("var", "Finite")]
    [InlineData("let", "Inferred")]
    public void AnAccessorsStorageCapturedByAClosureIsFixedThere(string declaration, string kind)
    {
        var setter = declaration == "var" ? "        set(value: i32) -> () => storage = value\n" : string.Empty;
        var c = MirBuildTest.Compile($"group Api\n    public {declaration} item: i32 = 0\n        get() -> i32\n            let c = storage@ref\n" +
            "            let f = func [c] () -> i32 => c@follow\n            return f()\n" + setter + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var borrow = KotoTree.Walk(c.Kotonoha.RootKoto).Single(static x => x is ConversionKoto && First(x) == "storage@ref");
        var storage = ((BoundType)HirTables.Meaning(borrow)!).Origin!;
        var accessor = Bodies(c).Single(static x => x.Key is { Kind: MirBodyKind.Getter, Decl: { } decl } && InProgram(decl.Declaration));
        var getter = c.Types.OriginAtom(BodyOf(accessor.Key), storage, borrow);
        var closure = c.Types.OriginAtom(BodyOf(Bodies(c).Single(IsProgramClosure).Key), storage, borrow);
        Assert.Equal((kind, ProjectionRole.Storage), (getter.Kind.ToString(), getter.Role));
        Assert.Equal((OriginAtomKind.Fixed, ProjectionRole.Storage), (closure.Kind, closure.Role));
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

    // PLAN G216 (SPEC 15.3.4, 15.6.1; DECISIONS R4-M126 P10, R4-M137): a nested signature's input, an anonymous function's or a Function
    // Type's parameter, is well formed by premise in that signature and by proof at each call, so its well-formedness forms no row of the
    // body it is written in: no row lies at a Type occurrence and no other declaration's Input is in that body's slice. An omitted inner
    // Origin ι is the body's variable, which the call's rows name where the body calls the value (k's argument fits into ι) and no row
    // names otherwise (g). The forms: G216's h1 and h2, a Closure body that needs the premise, a written enclosing Origin, a struct input,
    // two inputs related by a written Origin, a Function Type annotation called, a named function's Function Type parameter called (its
    // row was a declaration's), and a Function Type inside a Closure's parameter.
    [Theory]
    [InlineData(HeaderOnly, "g", true, false)]
    [InlineData(HeaderCalled, "k", true, true)]
    [InlineData("func g() -> i32\n    let h = func [] (p: ref/(ref/i32)) -> ref/i32 => p@follow\n    return 0\n", "g", true, false)]
    [InlineData("func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32 during q)) -> i32 => p@follow@follow\n    return h(q@ref)\n", "k", false, false)]
    [InlineData(StructInput, "k", false, false)]
    [InlineData("func g() -> i32\n    let h = func [] (p: ref/i32, r: ref/(ref/i32 during p)) -> i32 => 1\n    return 0\n", "g", false, false)]
    [InlineData(FunctionTypeCalled, "f", true, true)]
    [InlineData(FunctionTypeParameter, "g", false, false)]
    [InlineData("func k(q: ref/i32) -> i32\n    let h = func [] (cb: (ref/(ref/i32 during q)) -> i32) -> i32 => 0\n    return 0\n", "k", false, false)]
    public void ANestedSignaturesInputWellFormednessIsNoRowOfItsEvaluatingBody(string source, string body, bool omitted, bool namedByCalls)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.DoesNotContain(Rows(c, false), static x => x.Use is TypeKoto);
        var slice = c.Types.OriginRows(BodyOf(Bodies(c).Single(x => x.Key.Decl?.Name == body).Key));
        Assert.DoesNotContain(slice.Atoms, static x => x is { Kind: OriginAtomKind.Unclassed, Atom.Kind: OriginKind.Input });
        Assert.Empty(c.Types.OriginFindings);
        if (omitted)
        {
            var header = KotoTree.Walk(c.Kotonoha.RootKoto).First(static x => x is TypeSemanticsKoto && InProgram(x) && First(x) == "ref/(ref/i32)");
            var inner = header.TypeOf()!.Components[0].Origin!;
            Assert.Equal(OriginKind.Inference, inner.Kind);
            Assert.Equal(namedByCalls, slice.Rows.Any(x => slice.Atoms[x.Shorter].Atom == inner || slice.Atoms[x.Longer].Atom == inner));
        }
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

    // DECISIONS R4-M126 §2.5 (c), D-3: a Closure that is not classified (its capture failed, so no body was bound) has pending receiver and
    // environment atoms, neither Fixed nor Finite: Unclassed (row 9) until R4-M118's Dependent row.
    [Fact]
    public void AnUnclassifiedClosuresAtomsAreUnclassed()
    {
        var c = MirBuildTest.Compile("func g() -> i32\n    let text = \"a\"\n    let f = func [text] () -> i32 => 1\n    return 0\n" + Main);
        var literal = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.IsAnonymous);
        var body = new ExecutableBody(literal.SymbolOf());
        Assert.Equal(OriginAtomKind.Unclassed, c.Types.OriginAtom(body, new BoundOrigin(OriginKind.Projection, literal, Binding.ClosureValueSlot), literal).Kind);
        Assert.Equal(OriginAtomKind.Unclassed, c.Types.OriginAtom(body, new BoundOrigin(OriginKind.Projection, literal, Binding.EnvironmentSlot(0)), literal).Kind);
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
}
