// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 11.6, 15.2.3 (DECISIONS BU-73, PLAN G353; the owner accepted the support reduction): a stored Property whose inferred Type holds,
// at any depth, a Closure whose call signature names an enclosing container's Origin is one located Unsupported_Kd at the first such
// Closure literal, until imaging (PLAN, after R6-check): directly, in a Tuple, in a generic holder or Case (o1, a Type mismatch before
// G381), as a Closure's result, inherited.
// Every use of the Field rests on it (a read, a call, an erasure, a move of the container, a narrowing), so it is the only record:
// L2j, L2k, N1, N3, N3c (f4b), N3e (f4c), R2, R3 and h2 were accepted (valid, now Unsupported_Kd), L2g, m2 and the f4m rows were
// accepted unsoundly, L2i, N1b, N3d, f4d, R1, h1, m1, v1 and v2 had other records. A generic holder's Closure (N4, N4b) is Binding's
// located limit at the literal (before, the collector's at the whole literal). The guards keep dev's verdicts: a Closure naming no
// container Origin (O1a, O1c, O3, O4) and an unresolved initializer (x1). Since the MIR decides (R6-check H5; H3b T51, class 4: records
// added on a program Binding rejects, published directly until A6 2g-1, R5-M184), `mir` is the MIR's record beside Binding's: A5's
// UnprovenOriginRelation_Kd at m1's, v1's and v2's container fit, and an UninitializedPlace_Kd at x1's constructor.
public partial class PropertyBindingTest
{
    private const string ViewHead = "struct View {source}\n    public let data: ref/i32 during source\n    public let ";
    private const string ViewInit = "\n    public init(data: ref/i32 during source) => self.data = data\n\n";
    private const string CbView = ViewHead + "cb = func (y: ref/i32 during source) => ()" + ViewInit;
    private const string TupleView = ViewHead + "cb = (func (y: ref/i32 during source) => (), 1)" + ViewInit;
    private const string MkView = ViewHead + "mk = func () => func (y: ref/i32 during source) => ()" + ViewInit;
    private const string CbOnlyView = "struct View {source}\n    public let cb = func (y: ref/i32 during source) => ()\n    public init(data: ref/i32 during source) => ()\n\n";
    private const string HolderView = "struct Holder<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n\n" +
        ViewHead + "h = Holder.init(func (y: ref/i32 during source) => ())" + ViewInit;

    private const string BoxOf = "struct Box<T>\n    public let v: T\n    public let cb = func (x: T) => ()\n    public init(v: T) => self.v = v@move\n\n";
    private const string Local = "func f(r: ref/i32) -> i32\n    let view = View.init(r)\n";
    private const string Static = "func f(r: ref/i32 during static) -> i32\n    let view = View.init(r)\n";
    private const string Widen = "func f(p: ref/i32, q: ref/i32, w: View during q) -> i32\n    origin p outlives q\n    let a: View during p = w@move\n    return 1";
    private const string Narrow = "func f(p: ref/i32, q: ref/i32, v: View during p) -> i32\n    origin p outlives q\n    let a: View during q = v@move\n    return 1";
    private const string AtLiteral = "Unsupported_Kd: (y: ref/i32 during source)";

    [Theory]
    [InlineData("L2g", CbView + Local + "    return need(view.cb)", AtLiteral)]
    [InlineData("L2h", CbView + Local + "    return 1", AtLiteral)]
    [InlineData("L2i", CbView + Local + "    view.cb(r)\n    return 1", AtLiteral)]
    [InlineData("L2j", CbView + Static + "    return need(view.cb)", AtLiteral)]
    [InlineData("L2k", CbView + Static + "    let k = need(view.data)\n    return need(view@move)", AtLiteral)]
    [InlineData("R1", CbView + Local + "    let g: (ref/i32 during r) -> () = view.cb\n    return 1", AtLiteral)]
    [InlineData("R2", CbView + "func take(v: View) -> i32 => 1\n\n" + Local + "    return take(view@move)", AtLiteral)]
    [InlineData("R3", CbView + Local + "    let c = view.cb\n    return 1", AtLiteral)]
    [InlineData("m1", CbView + Widen, AtLiteral, "UnprovenOriginRelation_Kd: let a: View during p = w@move")]
    [InlineData("m2", CbView + Narrow, AtLiteral)]
    [InlineData("v1", CbOnlyView + Widen, AtLiteral, "UnprovenOriginRelation_Kd: let a: View during p = w@move")]
    [InlineData("v2", CbOnlyView + Narrow, AtLiteral, "UnprovenOriginRelation_Kd: let a: View during q = v@move")]
    [InlineData("N1", MkView + Static + "    return need(view.mk)", "Unsupported_Kd: ()")]
    [InlineData("N1b", MkView + Local + "    let c = view.mk()\n    c(r)\n    return 1", "Unsupported_Kd: ()")]
    [InlineData("f4m-mk", MkView + Narrow, "Unsupported_Kd: ()")]
    [InlineData("N3", TupleView + Static + "    return need(view@move)", AtLiteral)]
    [InlineData("N3c", TupleView + Local + "    return need(view.cb.0)", AtLiteral)]
    [InlineData("N3d", TupleView + Local + "    let c = view.cb.0\n    c(r)\n    return 1", AtLiteral)]
    [InlineData("N3e", TupleView + Static + "    return need(view.cb.0)", AtLiteral)]
    [InlineData("f4d", TupleView + Local + "    view.cb.0(r)\n    return 1", AtLiteral)]
    [InlineData("f4m", TupleView + Narrow, AtLiteral)]
    [InlineData("h1", HolderView + Static + "    return need(view.h)", AtLiteral)]
    [InlineData("h2", HolderView + Static + "    return need(view@move)", AtLiteral)]
    [InlineData("o1", ViewHead + "cb = Option.Some(func (y: ref/i32 during source) => ())" + ViewInit + Static + "    return need(view.cb)", AtLiteral)]
    [InlineData("N4", BoxOf + "func f(r: ref/i32 during static) -> i32\n    let b = Box.init(r)\n    return need(b.cb)", "Unsupported_Kd: (x: T)")]
    [InlineData("N4b", BoxOf + "func f(r: ref/i32) -> i32\n    let b = Box.init(r)\n    return need(b.cb)", "Unsupported_Kd: (x: T)")]
    [InlineData("O5", "struct Outer {source}\n    public let data: ref/i32 during source\n    public init(data: ref/i32 during source) => self.data = data\n    struct Inner\n        public let cb = func (y: ref/i32 during source) => ()\n", AtLiteral)]
    [InlineData("O1a", ViewHead + "t = (func () => 1, Option<ref/i32 during source>.None)" + ViewInit + Static + "    return need(view@move)", null)]
    [InlineData("O1c", ViewHead + "t = Option<ref/i32 during source>.None" + ViewInit + Static + "    return need(view@move)", null)]
    [InlineData("O3", ViewHead + "cb = func (y: ref/i32 during static) => ()" + ViewInit + Local + "    return need(view.cb)", null)]
    [InlineData("O4", ViewHead + "cb = func (y: ref/i32) => ()" + ViewInit + Local + "    view.cb(r)\n    return need(view.cb)", null)]
    [InlineData(
        "x1",
        ViewHead + "cb = missingName" + ViewInit + Local + "    view.cb(r)\n    return 1",
        "UnresolvedBinding_Kd: missingName",
        "UninitializedPlace_Kd: init(data: ref/i32 during source) => self.data = data")]
    public void AFieldClosureNamingItsContainersOriginIsTheLocatedLimit(string id, string declarations, string? record, string? mir = null)
        => Assert.Equal([id, .. new[] { record, mir }.OfType<string>()], [id, .. RawPointerSyntaxTest.Records(ConcreteClosureTest.NeedOwned + declarations)]);

    // SPEC 11.6, 7.7.4 (DECISIONS BU-107, PLAN G381): an inferred Field's initializer is bound with its header, before the capability
    // engine judges, so an erasure there (a call argument here; a Case payload in EnumBindingTest) is one located Unsupported_Kd at the
    // converted value until Owned is judged during header binding (R7); it was InternalInvariant_Kd. Its typed and local twins are valid.
    [Theory]
    [InlineData("q3", "struct S\n    public let p = P.init(func (x: i32) -> i32 => x)\n", "Unsupported_Kd: (x: i32) -> i32")]
    [InlineData("q3t", "struct S\n    public let p: P = P.init(func (x: i32) -> i32 => x)\n", null)]
    [InlineData("q3l", "func g() -> i32\n    let p = P.init(func (x: i32) -> i32 => x)\n    return 1\n", null)]
    public void AnErasureInAFieldHeaderIsTheLocatedLimit(string id, string declarations, string? record)
        => Assert.Equal(
            [id, .. new[] { record }.OfType<string>()],
            [id, .. RawPointerSyntaxTest.Records("struct P\n    public let f: (i32) -> i32\n    public init(f: (i32) -> i32) => self.f = f@move\n\n" + declarations)]);
}
