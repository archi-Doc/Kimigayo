// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 3.4, 3.4.3, 15.1.5, 16.3.1: one path computation decides writes, Moves and exclusive borrows; the diagnostic names the
/// layer that denies the capability. A shared access Binding synthesizes for an element is not such a layer.</summary>
public class PathCapabilityDiagnosticTest
{
    private const string S = "struct S\n    public var f: string\n    public var n: i32 = 0\n    public init(f: string) => self.f = f@move\n";

    // A stored exclusive reference below a shared layer: the path grants shared access only (SPEC 15.6.2).
    private const string Layers =
        "struct Inner\n    public var n: i32 = 0\nstruct Outer {a}\n    public let inner: uniq/Inner during a\n    public let slot: uniq/Option<i32> during a\n" +
        "    public init(inner: uniq/Inner during a, slot: uniq/Option<i32> during a)\n        self.inner = inner@move\n        self.slot = slot@move\n";

    // Parts an element storing a reference to Point reaches through the layers it stores (SPEC 3.4.3, 4.7.1; PLAN G147 (2b)).
    private const string Element =
        "struct Inner\n    public var s: string = \"\"\n    public init() => ()\n    public func mutate(self: uniq/Self) => self.s = \"m\"\n" +
        "struct Point\n    public var inner: Inner = Inner.init()\n    public var n: i32 = 0\n    public init() => ()\n" +
        "struct Outer\n    public var point: Point = Point.init()\n    public init() => ()\nfunc keep(i: Inner) => ()\n";

    // A function f whose var entries store exclusive references to Point.
    private const string Entries = "func f(value: uniq/Point during a)\n    var entries: Dictionary<i32, uniq/Point during a> = [1: value@move]\n";

    // A function f whose var vals store an exclusive reference to Point.
    private const string Vals = "func f(value: uniq/Point during a)\n    var vals: Array<uniq/Point during a> = [value@move]\n";

    // A struct D whose drop body follows, with an owned, a Copy and a nested Field: drop's self has exclusive access (SPEC 16.3.1).
    private const string Drop =
        "struct Inner\n    public var s: string = \"\"\n    public init() => ()\nfunc consume(s: string) => ()\n" +
        "struct D\n    var f: string\n    var n: i32 = 0\n    var inner: Inner = Inner.init()\n    public init(f: string) => self.f = f@move\n    drop\n";

    // A struct Leaf whose drop body follows, with a base Field.
    private const string Leaf = "open struct Base\n    public var value: string\n    protected init(value: string) => self.value = value@move\n" +
        "struct Leaf : Base\n    public init() : base(\"v\")\n        ()\n    drop\n";

    // A function f whose let entries store exclusive references to Point: a let binding cannot be exclusively acquired, so its
    // subscripts select index, which shares the element (SPEC 4.7.1, 15.1.5; DECISIONS BU-13).
    private const string LetEntries = "func f(value: uniq/Point during a)\n    let entries: Dictionary<i32, uniq/Point during a> = [1: value@move]\n";

    // An object Type D derived from B, for an upcast (SPEC 13.6.2).
    private const string Upcast = "open struct B\n    public init() => ()\nstruct D: B\n    public init() => ()\n";

    // Functions taking an exclusive and a shared i32 borrow, and two returning a temporary.
    private const string Take = "func take(x: uniq/i32) -> () => x@follow = 1\nfunc look(x: ref/i32) -> i32 => x@follow\n" +
        "func one() -> i32 => 1\nfunc pair() -> (i32, i32) => (1, 2)\n";

    // A struct Box with a computed Field, whose getter result is lent only shared (SPEC 11.5).
    private const string Box = "struct Box\n    public computed calc: i32\n        get() -> i32 => 1\n    public init() => ()\n";

    // A Contract whose required Property level is read through its getter, a getter result lent only shared (SPEC 11.1, 11.5).
    private const string Leveled = "contract Leveled\n    property level: i32 has get, set\n";

    // A struct Pt returned by mk and by Holder's computed Field cpt: Temporary Values whose Fields are lent with them (SPEC 3.4.1, 11.5).
    private const string Made = "struct Pt\n    public var x: i32 = 0\n    public init() => ()\nfunc mk() -> Pt => Pt.init()\n" +
        "struct Holder\n    public computed cpt: Pt\n        get() -> Pt => Pt.init()\n    public init() => ()\n";

    // Enums Color and Shape (a payload of Color) and functions borrowing them or an Option<i32>, shared or exclusively.
    private const string Cases = "enum Color\n    Red\n    Blue\nenum Shape\n    Dot(Color)\n    Box(i32, Color)\n" +
        "func lookc(x: ref/Color) -> i32 => 1\nfunc looks(x: ref/Shape) -> i32 => 2\nfunc lo(x: ref/Option<i32>) -> i32 => 3\n" +
        "func bumpc(x: uniq/Color) -> i32\n    x@follow = Color.Blue\n    return 4\n";

    // Group storage: a var and a let Field, a var and a let struct, a Tuple, a fixed array, an Array, a Dictionary, object handles (var and
    // let ones of a derived Type), a var whose setter is private, one whose setter is custom and computed Fields of a scalar and a struct;
    // the group's function bump has its body appended.
    private const string Statics =
        "struct Point\n    public var x: i32 = 0\n    public init() => ()\nfunc take(x: uniq/i32) -> () => x@follow = 1\n" +
        "open struct Shape\n    public var size: i32 = 0\n    public init() => ()\nstruct Circle: Shape\n    public init() => ()\n" +
        "group State\n    public var counter: i32 = 0\n    public let limit: i32 = 5\n    public var point: Point = Point.init()\n" +
        "    public let fixed: Point = Point.init()\n    public var pair: (i32, i32) = (1, 2)\n    public var fa: [2 of i32] = [1, 2]\n" +
        "    public var values: Array<i32> = [1, 2]\n    public var dict: Dictionary<i32, i32> = [1: 2]\n" +
        "    public var handle: obj/Point = Point.init()@obj\n" +
        "    public var circle: obj/Circle = Circle.init()@obj\n    public let ring: obj/Circle = Circle.init()@obj\n" +
        "    public var guarded: i32 = 0\n        private set\n    public var custom: i32 = 0\n" +
        "        set(value: i32) -> () => State.counter = value\n    public computed calc: i32\n        get() -> i32 => 1\n" +
        "    public computed cpt: Point\n        get() -> Point => Point.init()\n    public func bump()\n";

    [Theory]
    [InlineData("var values: Array<string> = [\"a\"]\nlet view = values[..]\nlet m = view[0]@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: uniq/S) -> string => r.f@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData("func f(r: uniq/string) -> string => r@follow@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData("func f(v: ref/i32) => v@follow = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f(v: ref/i32) => v@follow += 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/S) => r.n = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/S) -> uniq/i32 during r => r.n@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f(v: ref/i32) -> uniq/i32 during v => v@follow@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f(v: ref/i32) -> uniq/i32 during v => v@follow@uniq/i32", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/S) -> uniq/i32 during r => r.n@uniq/i32", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Upcast + "func f(r: ref/(obj/D))\n    let x = r@follow@objuniq/B", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nview[0] = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/S) -> string => r.f@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f(x: i32) => x = 2", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let value) if (value = 1) => value\n    _ => 0", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/(ref/S during a)) -> string => r.f@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o}) => r.inner.n = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o}) => r.inner.n += 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o})\n    let u = r.inner.n@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o})\n    match r.slot\n        .Some(let x) => x@follow += 1\n        .None => ()", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o})\n    let s = r.slot@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(a: ref/Array<S>) => a[0].n = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(a: ref/[2 of S]) => a[1].n += 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(a: uniq/Array<S>) -> string => a[0].f@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Drop + "        let m = self.f@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Drop + "        let m = self.n@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Drop + "        let m = self.inner.s@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Drop + "        if self.n > 0\n            consume(self.f@move)", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Leaf + "        let m = self.value@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Element + Entries + "    keep(entries[1].inner@move)", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Element + "func f(value: uniq/Outer during a)\n    var entries: Dictionary<i32, uniq/Outer during a> = [1: value@move]\n    keep(entries[1].point.inner@move)", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Element + Vals + "    keep(vals[0].inner@move)", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Element + "func f(d: ref/Dictionary<i32, uniq/Point during x>) => d[1].n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(value: ref/Point during a)\n    var entries: Dictionary<i32, ref/Point during a> = [1: value]\n    entries[1].n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(value: ref/(uniq/Point during a) during b)\n    var entries: Dictionary<i32, ref/(uniq/Point during a) during b> = [1: value]\n    entries[1].n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(value: uniq/(ref/Point during a) during b)\n    var entries: Dictionary<i32, uniq/(ref/Point during a) during b> = [1: value@move]\n    entries[1].n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + LetEntries + "    entries[1].n = 5", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Element + LetEntries + "    entries[1].n += 1", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Element + LetEntries + "    let r = entries[1].inner@uniq", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Element + LetEntries + "    entries[1].inner.mutate()", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData(Element + LetEntries + "    keep(entries[1].inner@move)", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Element + "func f(value: uniq/Point during a)\n    let outer: Dictionary<i32, Dictionary<i32, uniq/Point during a>> = [1: [2: value@move]]\n    outer[1][2].n = 5", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Element + "func f(d: Dictionary<i32, uniq/Point during x>) => d[1].n = 5", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Element + "func f(vals: Array<uniq/Point during x>) => vals[0].n = 5", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Element + "func f(value: uniq/Point during a)\n    let vals: Array<uniq/Point during a> = [value@move]\n    vals[0].n = 5", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Element + "func f(value: uniq/Point during a)\n    let vals: Array<uniq/Point during a> = [value@move]\n    vals[0].inner.mutate()", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData(Element + "func f(p: ref/(rc/Point)) => p.n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(p: ref/(rc/Point)) => keep(p.inner@move)", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(p: uniq/(rc/Point)) => p.n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(p: ref/(arc/Point)) => p.n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(p: ref/(obj/Point)) => p.n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Element + "func f(value: rc/Point)\n    var entries: Dictionary<i32, rc/Point> = [1: value@move]\n    entries[1].n = 5", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Take + "func f() -> ()\n    let x: i32 = 1\n    take((x)@uniq)", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Take + "func f() -> ()\n    let t = (1, 2)\n    take((t).0@uniq)", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Take + Box + "func f() -> ()\n    var b = Box.init()\n    take((b.calc)@uniq)", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Take + Leveled + "func f<T>(t: uniq/T) -> ()\n    T is Leveled\n    take((t.level)@uniq)", DiagnosticCode.InvalidAssignment_Kd)]
    public void TheDenyingLayerIsReported(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.DoesNotContain(c.Problems, x => x.Code is DiagnosticCode.Unsupported_Kd or DiagnosticCode.UninitializedPlace_Kd);
    }

    // SPEC 15.6.2, 15.6.4, 11.2: a static Field and its parts are lent as their accessors allow, but no analysis models static Loans (PLAN
    // P42), so a written Borrow (shorthand, typed, @raw, object, case adaptation) of the Field or a Field or part below it, through the
    // Field's qualified name and in its group too, is one located Unsupported_Kd (DECISIONS R4-M89 PD12 (i), BU-18); a Borrow SPEC denies
    // (a let, a private or custom setter, a let ancestor) keeps its Language error. Parentheses and inline parts keep the Place, which is
    // never a Temporary Value (SPEC 3.4, 3.6), and a getter result is lent only shared (SPEC 11.5). Not covered: Borrows through a static
    // object handle's payload and a Dictionary element, whose index borrows its static receiver implicitly (PLAN G100 (b)).
    [Theory]
    [InlineData("        ()\nfunc f()\n    let r = State.dict[1]@ref", "State.dict[1]", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.dict)[1]@ref", "(State.dict)[1]", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("        ()\nfunc f() => take((State.limit)@uniq)", "(State.limit)@uniq", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.limit)@ref", "(State.limit)@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => take((State.counter)@uniq)", "(State.counter)@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.pair.0@ref", "State.pair.0@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.pair).1@ref", "(State.pair).1@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.fa[1]@ref", "State.fa[1]@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.values[0]@ref", "State.values[0]@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.limit)@uniq/i32", "(State.limit)@uniq/i32", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.counter)@raw", "(State.counter)@raw", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.handle)@objref", "(State.handle)@objref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f<s/T>()\n    s is ref\n    _ = (State.limit)@s", "(State.limit)@s", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => take((State.calc)@uniq)", "(State.calc)@uniq", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.counter@uniq\n    r@follow = 3", "State.counter@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32\n    let r = State.counter@ref\n    return r@follow", "State.counter@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32\n    let r = State.limit@ref\n    return r@follow", "State.limit@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        let r = State.counter@uniq\n        r@follow += 1", "State.counter@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => take(State.counter@uniq)", "State.counter@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => take(State.point.x@uniq)", "State.point.x@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.fixed.x@ref", "State.fixed.x@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => take(State.pair.0@uniq)", "State.pair.0@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.handle@objuniq", "State.handle@objuniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.guarded@ref", "State.guarded@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => take(State.counter@uniq/i32)", "State.counter@uniq/i32", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.counter@raw", "State.counter@raw", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.handle@objref", "State.handle@objref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.circle@objref/Shape", "State.circle@objref/Shape", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.circle@objuniq/Shape", "State.circle@objuniq/Shape", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f<s/T>()\n    s is ref\n    _ = State.limit@s", "State.limit@s", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f<s/T>()\n    s is owner or ref\n    _ = State.counter@s", "State.counter@s", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f<s/T>()\n    s is owner or uniq\n    _ = State.counter@s", "State.counter@s", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f<s/T>()\n    s is ref or uniq\n    _ = State.counter@s", "State.counter@s", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f<s/T>()\n    s is ref or uniq\n    _ = State.limit@s", "State.limit@s", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.limit@uniq", "State.limit@uniq", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.guarded@uniq", "State.guarded@uniq", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.custom@uniq", "State.custom@uniq", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.fixed.x@uniq", "State.fixed.x@uniq", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f<s/T>()\n    s is owner or uniq\n    _ = State.limit@s", "State.limit@s", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.ring@objuniq/Shape", "State.ring@objuniq/Shape", DiagnosticCode.InvalidAssignment_Kd)]
    public void AQualifiedStaticBorrowIsUnsupported(string body, string at, DiagnosticCode code)
    {
        var source = Statics + body + "\npublic func main() => ()\n";
        var observed = DiagnosticCorpus.Check(source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length));
        Assert.Equal([(code.ToString(), source.IndexOf(at, StringComparison.Ordinal), at.Length)], observed);
    }

    // Reads, writes and calls through the group borrow no static Field in Binding, and an unqualified Borrow in its group binds until
    // A5-F9 lands (DECISIONS R4-M90 PQ-1). A case adaptation borrows a function reference as a Temporary Value, as `g@uniq` does, and a
    // static getter result is one too, lent shared with its parts (SPEC 3.6, 11.5).
    [Theory]
    [InlineData("        ()\nfunc f()\n    let r = State.cpt.x@ref\n    let s = (State.cpt).x@ref\n    let t = State.calc@ref")]
    [InlineData("        let r = calc@ref\n        let s = (calc)@ref")]
    [InlineData("        ()\nfunc f() -> i32 => State.counter + State.point.x")]
    [InlineData("        ()\nfunc f() -> i32\n    let v = (State.limit)\n    return v + (State.pair).0 + State.fa[1] + State.values[0] + (State.calc)")]
    [InlineData("        ()\nfunc f()\n    State.counter = 1\n    State.counter += 1\n    State.point.x = 3")]
    [InlineData("        State.counter += 1\nfunc f() => State.bump()")]
    [InlineData("        let r = counter@uniq\n        r@follow += 1")]
    [InlineData("        ()\nfunc g() => ()\nfunc f<s/T>()\n    s is ref\n    _ = g@s")]
    [InlineData("        ()\nfunc g() => ()\nfunc f<s/T>()\n    s is owner or uniq\n    _ = g@s")]
    public void StaticAccessesWithoutAQualifiedBorrowBind(string body)
    {
        var c = MinimalEmissionTest.Analyze(Statics + body + "\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void AFieldThroughSeveralLayersIsABarePlaceSubject()
    {
        // SPEC 15.1.6, 3.4.3: `r.n` through `ref/(ref/S)` is a Place, so the Subject is Shared and binds `ref/i32`.
        var c = MinimalEmissionTest.Analyze(S + "func f(r: ref/(ref/S during a)) -> i32\n    match r.n\n        let v => return v@follow");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(S + "let o = S.init(\"a\")\nlet t = o.f@move")]
    [InlineData(S + "func f(r: uniq/S) => r.n = 3")]
    [InlineData("func f(r: uniq/i32) => r@follow += 1")]
    [InlineData("func f(r: uniq/string) -> uniq/string => r@move")]
    [InlineData(S + "func f(a: uniq/Array<S>) => a[0].n = 3")]
    [InlineData(S + "func f(a: uniq/[2 of S]) => a[1].n += 3")]
    [InlineData(S + "var v: Array<S> = [S.init(\"a\")]\nlet r = v@uniq\nr[0].n = 3")]
    [InlineData(Element + Entries + "    entries[1].n = 5")]
    [InlineData(Element + Entries + "    entries[1].n += 5")]
    [InlineData("func f(value: uniq/(i32, i32) during a)\n    var entries: Dictionary<i32, uniq/(i32, i32) during a> = [1: value@move]\n    entries[1].0 += 1")]
    [InlineData(Element + Vals + "    vals[0].n = 5")]
    [InlineData(Element + Vals + "    vals[0].n += 1")]
    [InlineData("func f(value: uniq/(i32, i32) during a)\n    var vals: Array<uniq/(i32, i32) during a> = [value@move]\n    vals[0].0 = 5")]
    [InlineData(Element + "func f(a: uniq/Array<uniq/Point during x>) => a[0].n = 5")]
    [InlineData(Element + "func f(d: uniq/Dictionary<i32, uniq/Point during x>) => d[1].n = 5")]
    [InlineData(Element + "func f(value: uniq/Point during a)\n    var entries: Dictionary<i32, uniq/Point during a> = [1: value@move]\n    let d = entries@uniq\n    d[1].n = 5")]
    [InlineData(Element + LetEntries + "    let n = entries[1].n\n    let k = entries[1]@follow.n\n    let s = entries[1]@follow.inner.s@ref")]
    [InlineData(Element + "func f() -> i32\n    var t = ([1: Point.init()], 0)\n    t.0[1].n = 5\n    return t.0[1].n")]
    [InlineData(Element + "func f() -> i32\n    var vals: Array<Dictionary<i32, Point>> = [[1: Point.init()]]\n    vals[0][1].n = 5\n    return vals[0][1].n")]
    [InlineData(Element + "func make() -> Dictionary<i32, Point> => [1: Point.init()]\nfunc f() => make()[1].n = 5")]
    [InlineData(Element + "func f(p: ref/(rc/Point)) -> i32 => p.n")]
    [InlineData(Element + "func f(p: uniq/(obj/Point)) => p.n = 5")]
    [InlineData(Take + "func f() -> i32\n    var x: i32 = 1\n    take((x)@uniq)\n    take((one())@uniq)\n    return x")]
    [InlineData(Take + Box + "func f(b: Box) -> i32 => look((b.calc)@ref) + look(pair().0@ref) + look((pair()).1@ref)")]
    [InlineData(Take + Made + "func f(h: Holder) -> i32 => look(mk().x@ref) + look((mk()).x@ref) + look(mk().x) + look(h.cpt.x@ref) + look((h.cpt).x@ref)")]
    [InlineData("func lookz(x: ref/isize) -> isize => x@follow\nfunc f<length N>(a: [N of i32]) -> isize => lookz(N@ref) + lookz((N)@ref)")]
    public void OwnedAndExclusivePathsKeepTheirCapabilities(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 3.6, 6.5.2: a Case construction is a Temporary Value, qualified, in parentheses or with a nested payload, lent shared or,
    // spelled, exclusively; it is analyzed and generated on either pipeline.
    [Theory]
    [InlineData(Cases + "let a = lookc((Color.Red)@ref) + lookc(Color.Red@ref) + lookc(Color.Blue) + lookc((Color.Blue))")]
    [InlineData(Cases + "let a = bumpc((Color.Blue)@uniq) + bumpc(Color.Red@uniq)")]
    [InlineData(Cases + "let a = lo((Option<i32>.None)@ref) + lo(Kimi.Option<i32>.None@ref) + lo(Option<i32>.Some(2)@ref)")]
    [InlineData(Cases + "let a = looks((Shape.Box(7, Color.Red))@ref) + looks(Shape.Dot(Color.Blue)@ref)")]
    public void ACaseConstructionIsATemporaryValue(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var generated = c.WriteIr(TextWriter.Null, out var failure);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified && generated, MinimalEmissionTest.Describe(c, failure));
    }
}
