// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
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

    // A Field with a custom get and the standard set, beside a standard Field (D-818).
    private const string CustomGet = "struct W\n    public var q: i32 = 0\n        get() -> i32 => storage\n    public var v: i32 = 0\n    public init() => ()\n";

    // The same Field written through a shared receiver in its own method (D-818's sixth instance, the review's rv.ref-self).
    private const string CustomGetSelf = "struct W\n    public var q: i32 = 0\n        get() -> i32 => storage\n    public init() => ()\n" +
        "    public func put(self: ref/Self) => self.q = 1\n";

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

    // An open struct Fixed with a let Field y, which an exclusive reference to it does not lend exclusively (SPEC 3.4, 15.1.5).
    private const string Fixed = "open struct Fixed\n    public var x: i32 = 0\n    public let y: i32 = 1\n    public init() => ()\n";

    // A Contract whose required Property level is read through its getter, a getter result lent only shared (SPEC 11.1, 11.5).
    private const string Leveled = "contract Leveled\n    property level: i32 has get, set\n";

    // A struct Pt returned by mk and by Holder's computed Field cpt: Temporary Values whose Fields are lent with them (SPEC 3.4.1, 11.5).
    private const string Made = "struct Pt\n    public var x: i32 = 0\n    public init() => ()\nfunc mk() -> Pt => Pt.init()\n" +
        "struct Holder\n    public computed cpt: Pt\n        get() -> Pt => Pt.init()\n    public init() => ()\n";

    // A struct Tw returned by tw(): a standard, a custom-get, a custom-set and a let Field, a nested Pt and a Tuple; a Copy struct Tc;
    // a fixed array and an Array returned by calls (D-827). Read with Made and Take.
    private const string Temps =
        "struct Tw\n    public var v: i32 = 0\n    public var q: i32 = 0\n        get() -> i32 => storage\n    public var s: i32 = 0\n        set(value: i32) -> () => storage = value\n" +
        "    public let k: i32 = 0\n    public var pt: Pt = Pt.init()\n    public var pr: (i32, i32) = (0, 0)\n    public init() => ()\nfunc tw() -> Tw => Tw.init()\n" +
        "struct Tc\n    Self is Copy\n    public var v: i32 = 0\n    public init() => ()\nfunc fixed() -> [2 of i32] => [2 of 0]\n" +
        "func array() -> Array<i32>\n    let a: Array<i32> = [1, 2]\n    return a@move\n";

    // A struct Bc with a uniq/Self method (D-827).
    private const string Bump = "struct Bc\n    public var v: i32 = 0\n    public init() => ()\n    public func bump(self: uniq/Self) => self.v += 1\n";

    // Through a pair layer's owner case (D-827): implicit selections of a temporary's part, whole borrows and receivers of the temporary.
    private const string FollowWhole = Bump + "func pass<s/T>(x: s/T) -> s/T\n    s is owner or uniq\n    return x@move\nfunc mut(r: uniq/Tw) => r.v = 2\n" +
        "func see(r: ref/Tw) -> i32 => r.v\nfunc f<s/T>(x: s/T, a: s/Tw, b: s/Tw, c: s/Tw, d: s/Tw, e: s/Tw, m: s/Bc, n: s/Bc)\n    s is owner or uniq\n" +
        "    a@move.v = 1\n    pass(b@move).v = 1\n    _ = look(c@move.v@ref)\n    mut(d@move@follow@uniq)\n    _ = see(e@move@follow@ref)\n    m@move@follow.bump()\n" +
        "    n@move.bump()\nfunc g() => f(1, tw(), tw(), tw(), tw(), tw(), Bc.init(), Bc.init())\n";

    // A struct G with an Origin parameter, a reference Field and a drop, returned by mkG, and a Tuple with a reference element returned
    // by pr (D-827): f and g write a borrow of another Origin than the temporary's own into its reference part (C-414's C6 and K3), h
    // one of the temporary's own Origin (K2).
    private const string RefPrelude = "struct G {a}\n    public var r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    drop\n" +
        "        Console.writeLine(\"d\")\nfunc mkG(x: ref/i32) -> G{s}\n    origin s.a == x\n    return G.init(x)\nfunc pr(x: ref/i32) -> (ref/i32 during x, i32)\n" +
        "    return (x, 1)\n";

    // A temporary whose shared part holds x@ref while an exclusive borrow of x is written into its other part.
    private const string ExclusivePart = "struct UX {a, b}\n    public var r: ref/i32 during a\n    public var u: uniq/i32 during b\n" +
        "    public init(r: ref/i32 during a, u: uniq/i32 during b)\n        self.r = r\n        self.u = u@move\n" +
        "func mkUX(r: ref/i32, u: uniq/i32) -> UX{s}\n    origin s.a == r\n    origin s.b == u\n    return UX.init(r, u@move)\n" +
        "func fx()\n    var x = 1\n    var z = 2\n    mkUX(x@ref, z@uniq).u = x@uniq\n";

    private const string RefParts = RefPrelude + "func f()\n    var x = 1\n    let y = 2\n    mkG(y@ref).r = x@ref\n    x = 3\nfunc g()\n    let x = 1\n    let y = 2\n" +
        "    pr(y@ref).0 = x@ref\nfunc h()\n    let y = 2\n    mkG(y@ref).r = y@ref\n";

    // Enums Color and Shape (a payload of Color) and functions borrowing them or an Option<i32>, shared or exclusively.
    private const string Cases = "enum Color\n    Red\n    Blue\nenum Shape\n    Dot(Color)\n    Box(i32, Color)\n" +
        "func lookc(x: ref/Color) -> i32 => 1\nfunc looks(x: ref/Shape) -> i32 => 2\nfunc lo(x: ref/Option<i32>) -> i32 => 3\n" +
        "func bumpc(x: uniq/Color) -> i32\n    x@follow = Color.Blue\n    return 4\n";

    // Group storage: a var and a let Field, a var and a let struct, a Tuple, a fixed array, an Array, a Dictionary, object handles (var and
    // let ones of a derived Type, an rc one), a derived struct whose base method borrows its base, function values (var, let, one taking
    // an i32, a struct Holder's, an object handle's, an Array's, obj and rc handles of one, var and let Arrays of an Exclusive Closure), a
    // var whose setter is private, one whose setter is custom and computed Fields of a scalar and a struct; Point's computed Field cx and
    // exclusive and retaining shared methods; the group's glook borrows its argument, and its function bump has its body appended.
    private const string ElementStatics =
        "struct Pt\n    public var x: i32 = 0\n    public init() => ()\ngroup Store\n    public var pt: Pt = Pt.init()\n    public var pair: (i32, i32) = (1, 2)\n" +
        "    public var fa: [2 of i32] = [1, 2]\n    public var values: Array<i32> = [1, 2]\n    public let lpair: (i32, i32) = (1, 2)\n" +
        "    public let lfa: [2 of i32] = [1, 2]\n";

    private const string GroupNest =
        "group State\n    public var nest: ((i32, i32), i32) = ((1, 2), 3)\n    public var ta: (Array<i32>, i32) = ([1, 2], 3)\n" +
        "    public let lnest: ((i32, i32), i32) = ((1, 2), 3)\n    public let lta: (Array<i32>, i32) = ([1, 2], 3)\n";

    private const string GroupPairs = "group State\n    public var pair: (i32, i32) = (1, 2)\n    public let lpair: (i32, i32) = (1, 2)\n";

    // The heads of ElementStatics' and GroupNest's statics, each non-scalar, so P42's limit (StaticStorageRecords).
    private const string StoreStatics = "var pt:|var pair:|var fa:|var values:|let lpair:|let lfa:";

    private const string NestStatics = "var nest:|var ta:|let lnest:|let lta:";

    private const string PairHead = "func f<s/T>(pair: s/(i32, i32), marker: s/T)\n";

    // A caller of PairHead's f with s = uniq: the instance a Library build generates.
    private const string PairCall = "func g()\n    var t = (1, 2)\n    var m: i32 = 0\n    f(t@uniq, m@uniq)\n";

    private const string MakeObj =
        "func make<s/T>(value: s/T) -> s/(i32, i64)\n    s is obj\n    var x: (i32, i64) = (1, 2)\n    let made = x@s\n    return made@move\n";

    private const string Statics =
        "struct Point\n    public var x: i32 = 0\n    public computed cx: i32\n        get() -> i32 => self.x\n        set(value: i32) -> () => self.x = value\n" +
        "    public init() => ()\n    public func grow(self: uniq/Self) => self.x = self.x + 1\n" +
        "    public func view(self: ref/Self) -> ref/i32 during self => self.x@ref\nfunc take(x: uniq/i32) -> () => x@follow = 1\n" +
        "func look(x: ref/i32) -> i32 => x@follow\nfunc lookp(x: ref/Point) -> i32 => x.x\n" +
        "open struct Shape\n    public var size: i32 = 0\n    public init() => ()\n    public func peek(self: ref/Self) -> i32 => self.size\n" +
        "struct Circle: Shape\n    public init() => ()\nstruct Holder\n    public var fn: () -> i32 = func [] () => 1\n    public init() => ()\n" +
        "group State\n    public var counter: i32 = 0\n    public let limit: i32 = 5\n    public var point: Point = Point.init()\n" +
        "    public let fixed: Point = Point.init()\n    public var pair: (i32, i32) = (1, 2)\n    public var fa: [2 of i32] = [1, 2]\n" +
        "    public var values: Array<i32> = [1, 2]\n    public var dict: Dictionary<i32, i32> = [1: 2]\n" +
        "    public var handle: obj/Point = Point.init()@obj\n" +
        "    public var circle: obj/Circle = Circle.init()@obj\n    public let ring: obj/Circle = Circle.init()@obj\n" +
        "    public var shared: rc/Point = Point.init()@rc\n    public var leaf: Circle = Circle.init()\n" +
        "    public var fn: () -> i32 = func [] () => 1\n    public let fixedFn: () -> i32 = func [] () => 2\n" +
        "    public var f1: (i32) -> i32 = func [] (k: i32) => k\n    public var rec: Holder = Holder.init()\n" +
        "    public var h: obj/Holder = Holder.init()@obj\n    public var fns: Array<() -> i32> = [func [] () => 3]\n" +
        "    public var oc: obj/((i32) -> i32) = Kimi.Intrinsics.makeObj<(i32) -> i32>(func [] (k: i32) => k)\n" +
        "    public var rcf: rc/((i32) -> i32) = Kimi.Intrinsics.makeRc<(i32) -> i32>(func [] (k: i32) => k)\n" +
        "    public var ex = (func [] (k: i32) => func [var k] () -> i32 => ++k)(0)\n" +
        "    public let fixedEx = (func [] (k: i32) => func [var k] () -> i32 => ++k)(0)\n" +
        "    public var exs = [(func [] (k: i32) => func [var k] () -> i32 => ++k)(0)]\n" +
        "    public let fixedExs = [(func [] (k: i32) => func [var k] () -> i32 => ++k)(0)]\n" +
        "    public var guarded: i32 = 0\n        private set\n    public var custom: i32 = 0\n" +
        "        set(value: i32) -> () => State.counter = value\n    public computed calc: i32\n        get() -> i32 => 1\n" +
        "    public computed cpt: Point\n        get() -> Point => Point.init()\n    public func glook(x: ref/i32) -> i32 => x@follow\n    public func bump()\n";

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
    [InlineData(Take + Fixed + "func f(p: uniq/Fixed) -> () => take((p.y)@uniq)", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Take + Fixed + "func f<T>(t: uniq/T) -> ()\n    T is Fixed\n    take((t.y)@uniq)", DiagnosticCode.InvalidAssignment_Kd)]
    // SPEC 15.6.2, 13.7.1, 11.2 (D-818): a write or an update of a standard-set target behind a custom get is denied by the shared layer
    // on its path, as with the standard get (the `w.v` twin); before D-818 the target was taken as a getter result, a root, which gave
    // InvalidAssignment_Kd.
    [InlineData(CustomGet + "func f(w: ref/W) => w.q = 1", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(CustomGet + "func f(w: ref/W) => w.q += 1", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(CustomGet + "func f(w: ref/W) => w.v = 1", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(CustomGet + "func f(w: ref/W)\n    var h = func [w] (c: bool) -> ()\n        w.q = 1\n    h(true)", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(CustomGetSelf, DiagnosticCode.SharedPathAccess_Kd)]
    public void TheDenyingLayerIsReported(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.DoesNotContain(c.Problems, x => x.Code is DiagnosticCode.Unsupported_Kd or DiagnosticCode.UninitializedPlace_Kd);
    }

    // SPEC 15.6, 15.6.2, 15.6.4, 11.2: a static Field and its parts are lent as their accessors allow, but no analysis models static Loans
    // (PLAN P42), so every Borrow SPEC lends of a Place rooted at a static Field, through inline parts, elements and an owning handle's
    // payload, is one located Unsupported_Kd at the Borrow (DECISIONS R4-M89 PD12 (i), BU-18, BU-25): a written one (shorthand, typed,
    // @raw, object, case adaptation), qualified or in the Field's group, and an implicit one, at its operand: a receiver (a method's, a user
    // index's, an accessor's, a base method's, a value call's callee), an argument, an expected Type's or a Case payload's adaptation and
    // a value call's input (R4-M90 PQ-1). A
    // Borrow SPEC denies (a let, a private or custom setter, a let ancestor, a shared handle's payload exclusively, an argument's implicit
    // exclusive borrow) keeps its Language error. Parentheses and inline parts keep the Place, which is never a Temporary Value (SPEC 3.4,
    // 3.6), and a getter result is lent only shared (SPEC 11.5). Each row asserts the records its own text locates, after the prelude,
    // whose StaticStorage records one test asserts (OI-1 (a′), DECISIONS R5-M156).
    [Theory]
    [InlineData("        ()\nfunc f()\n    let r = State.dict[1]@ref", "State.dict", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.dict)[1]@ref", "(State.dict)", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.dict[1]", "State.dict", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => State.dict[1] = 3", "State.dict", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = dict[1]", "dict", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => State.point.grow()", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let v = State.point.view()", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        point.grow()", "point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        let v = point.view()", "point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => State.values.append(3)", "State.values", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => State.handle.grow()", "State.handle", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.leaf.peek()", "State.leaf", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.fn()", "State.fn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = fn()", "fn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => (State.fn)()", "(State.fn)", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = (fn)()", "(fn)", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.fixedFn()", "State.fixedFn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = fixedFn()", "fixedFn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.rec.fn()", "State.rec.fn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = rec.fn()", "rec.fn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.h.fn()", "State.h.fn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = h.fn()", "h.fn", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.fns[0]()", "State.fns[0]", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = fns[0]()", "fns[0]", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.fixedEx()", "State.fixedEx", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        _ = fixedEx()", "fixedEx", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => (State.fixedEx)()", "(State.fixedEx)", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.ex()", "State.ex", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = ex()", "ex", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.exs[0]()", "State.exs[0]", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = exs[0]()", "exs[0]", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = fixedExs[0]()", "fixedExs[0]", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.rcf(1)", "State.rcf", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = oc(1)", "oc", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = oc@follow(1)", "oc", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.point.cx", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => State.point.cx = 3", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => State.point.cx += 3", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => look(State.limit)", "State.limit", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => lookp(State.point)", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        _ = glook(counter)", "counter", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r: ref/Point = State.point", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let o: Option<ref/Point> = .Some(State.point)", "State.point", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f(g: (ref/i32) -> i32) -> i32 => g(State.limit)", "State.limit", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() => take(State.counter)", "State.counter", DiagnosticCode.ExclusiveBorrowRequired_Kd)]
    [InlineData("        let r = counter@uniq\n        r@follow += 1", "counter@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        let r = point.x@ref", "point.x@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        let r = values[0]@uniq", "values[0]@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        let r = counter@raw", "counter@raw", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        let r = handle@objref", "handle@objref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\n    public func g<s/T>()\n        s is owner or ref\n        _ = counter@s", "counter@s", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\n    public func lr() -> ref/i32 during static\n        return limit@ref", "limit@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.handle.x@uniq", "State.handle.x@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.handle.x@ref", "State.handle.x@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.handle@follow.x@ref", "State.handle@follow.x@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = (State.handle).x@uniq", "(State.handle).x@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.shared.x@ref", "State.shared.x@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        let r = handle.x@uniq", "handle.x@uniq", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.shared.x@uniq", "State.shared.x@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("        ()\nfunc f()\n    let r = State.ring.size@uniq", "State.ring.size@uniq", DiagnosticCode.InvalidAssignment_Kd)]
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
    public void AStaticBorrowIsUnsupported(string body, string at, DiagnosticCode code)
    {
        var source = Statics + body + "\npublic func main() => ()\n";
        Assert.Equal([(code.ToString(), source.IndexOf(at, Statics.Length, StringComparison.Ordinal), at.Length)], RowRecords(source));
    }

    // SPEC 23.1.2: a static callee's Unsupported_Kd (an owned callee's or a handle's) fails the callee alone, as every implicit static
    // Borrow fails its operand alone, so the value call stays bound and its independent records (an argument's, its applicability's, its
    // result's) are reported beside it (DECISIONS BU-25; fail-before: the callee's record alone).
    [Theory]
    [InlineData("        _ = f1(missing)", "f1", "missing", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("        _ = f1(1) + \"t\"", "f1", "\"t\"", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("        let w: string = f1(2)", "f1", "f1(2)", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("        _ = f1(\"s\")", "f1", "f1(\"s\")", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("        _ = f1(1, 2)", "f1", "f1(1, 2)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("        _ = f1(counter@ref@follow)", "f1", "counter@ref", DiagnosticCode.Unsupported_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.f1(missing)", "State.f1", "missing", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("        _ = oc(missing)", "oc", "missing", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("        _ = oc@follow(1, 2)", "oc", "oc@follow(1, 2)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("        ()\nfunc f() -> i32 => State.rcf(missing)", "State.rcf", "missing", DiagnosticCode.UnresolvedBinding_Kd)]
    public void AStaticCalleeKeepsTheCallsRecords(string body, string callee, string at, DiagnosticCode code)
    {
        var source = Statics + body + "\npublic func main() => ()\n";
        var unsupported = (DiagnosticCode.Unsupported_Kd.ToString(), source.IndexOf(callee, Statics.Length, StringComparison.Ordinal), callee.Length);
        var record = (code.ToString(), source.IndexOf(at, Statics.Length, StringComparison.Ordinal), at.Length);
        Assert.Equal(new[] { unsupported, record }.Order(), RowRecords(source).Order());
    }

    // DECISIONS R5-M82, R5-M156 (OI-1 (a′)): check reports each of the prelude's 26 statics whose storage P42 has not brought (one that can
    // be set or whose Type is not a scalar; `limit` is an immutable scalar) as the collector's StaticStorage record at its declaration,
    // through its accessors, also beside a Binding error after the prelude (SPEC 23.1.2), so the rows above assert
    // their own records once: 26 records, no longer 27, since the MIR decides acceptance (H5): the old analysis's own Unsupported_Kd at
    // the custom setter's static write `State.counter` went with it (1x), and the collector's record at `custom` stands for that static.
    // P42 reverts this test.
    [Theory]
    [InlineData("        ()", "")]
    [InlineData("        _ = missing", "missing")]
    public void ThePreludesStaticsAreUnsupportedAtCheck(string body, string at)
    {
        var source = Statics + body + "\npublic func main() => ()\n";
        var declarations = new[]
        {
            "var counter:", "var point:", "let fixed:", "var pair:", "var fa:", "var values:", "var dict:", "var handle:", "var circle:",
            "let ring:", "var shared:", "var leaf:", "var fn:", "let fixedFn:", "var f1:", "var rec:", "var h:", "var fns:", "var oc:", "var rcf:",
            "var ex =", "let fixedEx =", "var exs =", "let fixedExs =", "var guarded:", "var custom:",
        };
        var expected = StaticStorageRecords(source, declarations).ToList();

        // A complete program has the collector's records alone; a Binding error after the prelude is reported beside them.
        if (at.Length > 0)
        {
            expected.Add((DiagnosticCode.UnresolvedBinding_Kd.ToString(), source.IndexOf(at, Statics.Length, StringComparison.Ordinal), at.Length));
        }

        var observed = DiagnosticCorpus.Check(source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length));
        Assert.Equal(expected.Order(), observed.Order());
    }

    // Reads, writes and calls through the group borrow no static Field in Binding. A case adaptation borrows a function reference as a
    // Temporary Value, as `g@uniq` does, and a static getter result is one too, lent shared with its parts (SPEC 3.6, 11.5).
    [Theory]
    [InlineData("        ()\nfunc f()\n    let r = State.cpt.x@ref\n    let s = (State.cpt).x@ref\n    let t = State.calc@ref")]
    [InlineData("        let r = calc@ref\n        let s = (calc)@ref")]
    [InlineData("        ()\nfunc f() -> i32 => State.counter + State.point.x")]
    [InlineData("        ()\nfunc f() -> i32\n    let v = (State.limit)\n    return v + (State.pair).0 + State.fa[1] + State.values[0] + (State.calc)")]
    [InlineData("        ()\nfunc f()\n    State.counter = 1\n    State.counter += 1\n    State.point.x = 3")]
    [InlineData("        State.counter += 1\nfunc f() => State.bump()")]
    [InlineData("        ()\nfunc g() => ()\nfunc f<s/T>()\n    s is ref\n    _ = g@s")]
    [InlineData("        ()\nfunc g() => ()\nfunc f<s/T>()\n    s is owner or uniq\n    _ = g@s")]
    public void StaticAccessesWithoutABorrowBind(string body)
    {
        var c = MinimalEmissionTest.Analyze(Statics + body + "\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // PLAN G203 (SPEC 3.4.3, 4.7, 4.7.1, 15.1.5): an element of a mutable static's Tuple, fixed array or Array is an inline part of its
    // path and writable as its Field is (`Store.pt.x = 3`), so Binding accepts the write, assigned or updated
    // (AStaticElementWriteRestsOnTheStaticStorageLimit). A `let` static's element keeps InvalidAssignment_Kd.
    [Theory]
    [InlineData("    Store.lpair.0 = 3", "Store.lpair.0", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("    Store.lfa[1] = 3", "Store.lfa[1]", DiagnosticCode.InvalidAssignment_Kd)]
    public void AStaticsElementIsWritableAsItsField(string body, string at, DiagnosticCode code)
    {
        var source = ElementStatics + "func f()\n" + body + "\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length))
            .Where(static x => x.Start >= ElementStatics.Length);
        Assert.Equal([(code.ToString(), source.IndexOf(at, ElementStatics.Length, StringComparison.Ordinal), at.Length)], records);
    }

    // PLAN G203 (DECISIONS BU-15, BU-16; SPEC 3.4.3, 4.7, 11, 13.5.5, 15.1.5): the rule is general, an owned inline element is writable
    // where its owner is. So these SPEC-valid writes also move off InvalidAssignment_Kd: a static's (nested element, its Tuple's Array
    // element, the bare static inside its group, `storage`'s element in a static's custom setter;
    // AStaticElementWriteRestsOnTheStaticStorageLimit) and the others (AnOwnedInlineElementWriteIsGenerated). Each invalid counterpart
    // keeps its error: a `let` static or slot, a `ref/Self` method, `owner or uniq` on a parameter or a `let` copy, a `let` handle. Only
    // the records after each row's prelude are compared.
    [Theory]
    [InlineData("lnest", GroupNest, "func f()\n    State.lnest.0.1 = 3\n", "InvalidAssignment_Kd State.lnest.0.1")]
    [InlineData("lta", GroupNest, "func f()\n    State.lta.0[1] = 3\n", "InvalidAssignment_Kd State.lta.0[1]")]
    [InlineData("bare_let", GroupPairs, "    public func g()\n        lpair.0 = 3\n", "InvalidAssignment_Kd lpair.0")]
    [InlineData("ref_self", "", "struct H\n    public var pr: (i32, i32) = (1, 2)\n    public init() => ()\n    public func m(self: ref/Self) => self.pr.0 = 3\n", "SharedPathAccess_Kd self.pr.0")]
    [InlineData("pair_param", PairHead + "    s is owner or uniq\n", "    pair@follow.0 = 3\n", "InvalidAssignment_Kd pair@follow.0")]
    [InlineData("pair_let", PairHead + "    s is owner or uniq\n", "    let p = pair\n    p@follow.0 = 3\n", "InvalidAssignment_Kd p@follow.0")]
    [InlineData("obj_let", MakeObj, "func f()\n    let h = make(5@obj)\n    h@follow.0 = 3\n", "InvalidAssignment_Kd h@follow.0")]
    public void AnOwnedInlineElementIsWritableWhereItsOwnerIs(string form, string prelude, string body, string records)
    {
        var source = prelude + body + "public func main() => ()\n";
        var shown = DiagnosticCorpus.Check(source).Diagnostics.Where(x => x.Span!.Value.Start >= prelude.Length)
            .Select(x => x.Code + " " + source.Substring(x.Span!.Value.Start, x.Span.Value.Length).Split('\n')[0]).Order(StringComparer.Ordinal);
        Assert.True(string.Join("; ", shown) == records, $"{form}: expected [{records}], found [{string.Join("; ", shown)}]");
    }

    // PLAN G203's valid writes to a mutable static's element (an inline part, a nested element, a Tuple's Array element, the bare static
    // in its group, `storage`'s element in a static's custom setter) rest on the P42 limit since the MIR decides acceptance (H5; 1x): the
    // old analysis's located Unsupported_Kd at the write (and at a static getter's `storage`) went with it, and check rejects the program
    // with the collector's StaticStorage Unsupported_Kd at each of its non-scalar statics alone (DECISIONS R5-M82, R5-M156; SPEC
    // 7.7.2/0's limit on the owner's (e) list), so generation refuses it. P42 makes these programs accepted.
    [Theory]
    [InlineData(ElementStatics + "func f()\n    Store.pt.x = 3\n", StoreStatics, "Store.pt.x = 3")]
    [InlineData(ElementStatics + "func f()\n    Store.pair.0 = 3\n", StoreStatics, "Store.pair.0 = 3")]
    [InlineData(ElementStatics + "func f()\n    Store.pair.0 += 1\n", StoreStatics, "Store.pair.0 += 1")]
    [InlineData(ElementStatics + "func f()\n    Store.fa[1] = 3\n", StoreStatics, "Store.fa[1] = 3")]
    [InlineData(ElementStatics + "func f()\n    Store.fa[0] += 1\n", StoreStatics, "Store.fa[0] += 1")]
    [InlineData(ElementStatics + "func f()\n    Store.values[0] = 3\n", StoreStatics, "Store.values[0] = 3")]
    [InlineData(GroupNest + "func f()\n    State.nest.0.1 = 3\n", NestStatics, "State.nest.0.1 = 3")]
    [InlineData(GroupNest + "func f()\n    State.ta.0[1] = 3\n", NestStatics, "State.ta.0[1] = 3")]
    [InlineData(GroupPairs + "    public func g()\n        pair.0 = 3\n", "var pair:|let lpair:", "pair.0 = 3")]
    [InlineData("group G\n    public var item: (i32, i32) = (1, 2)\n        get() -> (i32, i32) => storage\n        set(value: (i32, i32)) -> () => storage.0 = value.0\n", "var item:", "storage.0 = value.0")]
    public void AStaticElementWriteRestsOnTheStaticStorageLimit(string program, string statics, string write)
    {
        var source = program + "public func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length)).ToArray();
        Assert.Equal(StaticStorageRecords(source, statics.Split('|')).Order(), records.Order());
        Assert.DoesNotContain((DiagnosticCode.Unsupported_Kd.ToString(), source.IndexOf(write, StringComparison.Ordinal), write.Length), records);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // PLAN G203's valid writes outside static storage are accepted since the MIR decides acceptance (H5; 1a, the owner 2026-11-03),
    // where the old analysis gave its located Unsupported_Kd: `storage`'s element in an instance's custom setter, a constructor's
    // `self.pr.0`, a pair layer's element under `s is uniq` (a parameter, a `let` copy, an update) and a `var` copy under `owner or uniq`,
    // and a `var` obj handle's payload element. Check accepts each program with no record, and a Library build generates the function
    // that writes (every root, K11), a generic one as the instance `g` calls.
    [Theory]
    [InlineData("storage", "", "struct H\n    public var item: (i32, i32) = (1, 2)\n        get() -> (i32, i32) => storage\n        set(value: (i32, i32)) -> () => storage.0 = value.0\n    public init() => ()\n", "item.Set")]
    [InlineData("ctor", "", "struct H\n    public var pr: (i32, i32) = (1, 2)\n    public init() => self.pr.0 = 3\n", "init")]
    [InlineData("pair_uniq", PairHead + "    s is uniq\n", "    pair@follow.0 = 3\n" + PairCall, "f")]
    [InlineData("pair_uniq_let", PairHead + "    s is uniq\n", "    let p = pair\n    p@follow.0 = 3\n" + PairCall, "f")]
    [InlineData("pair_uniq_update", PairHead + "    s is uniq\n", "    pair@follow.0 += 3\n" + PairCall, "f")]
    [InlineData("pair_var", PairHead + "    s is owner or uniq\n", "    var p = pair\n    p@follow.0 = 3\n" + PairCall, "f")]
    [InlineData("obj_var", MakeObj, "func f()\n    var h = make(5@obj)\n    h@follow.0 = 3\n    h@follow.1 += 1\n", "f")]
    public void AnOwnedInlineElementWriteIsGenerated(string form, string prelude, string body, string function)
    {
        var source = prelude + body + "public func main() => ()\n";
        var check = DiagnosticCorpus.Check(source);
        Assert.True(check.Accepted && check.Diagnostics.Length == 0, $"{form}: {string.Join("; ", check.Diagnostics.Select(static x => x.Code))}");
        Assert.Contains(function, MinimalEmissionTest.GeneratedBodies(source, OutputKind.Library, accepted: true));
    }

    // PLAN G1068, G203 (D-827; SPEC 3.4.1, 3.6, 11.3.1, 13.7.1, 15.1.5): a newly owned temporary is a writable root, so a part of it is
    // written, updated and exclusively borrowed as its Property permissions allow: a call result's Field (a custom get's too, whose
    // getter the write does not call; a custom set's, called on the temporary), nested, in parentheses, a Tuple, fixed-array or Array
    // element, a Tuple literal's element, an owned handle's payload (also through `@follow`, whole), a Copy (`c@copy.v`, of a getter
    // result too: a new temporary), a moved value (`t@move.v`), a uniq/Self Receiver Expression on a part or on the object of a part's
    // obj handle, and through a pair layer's owner case, explicit or implicit, also borrowed whole (SPEC 13.5.5: P itself); the
    // referent of an exclusive borrow of a temporary is written whole (13.5.5: the referent of uniq/T offers Write). Check accepts each
    // program with no record and a Library build generates the function that writes.
    [Theory]
    [InlineData("field", "func f()\n    tw().v = 1\n    tw().q = 1\n    tw().s = 1\n")]
    [InlineData("update", "func f()\n    tw().v += 1\n    tw().q += 1\n    tw().s += 1\n    tw().v++\n    ++tw().q\n    tw().s++\n")]
    [InlineData("nested", "func f()\n    tw().pt.x = 1\n    tw().pr.0 = 1\n    (tw()).v = 1\n")]
    [InlineData("elements", "func f(i: isize)\n    pair().0 = 1\n    fixed()[0] = 1\n    fixed()[0]++\n    fixed()[i] = 1\n    array()[0] += 1\n    (1, 2).0 = 5\n")]
    [InlineData("replace", "func strs() -> [1 of string] => [\"old\"]\nfunc f()\n    strs()[0] = \"new\"\n    tw().pt = Pt.init()\n")]
    [InlineData("copy", "func f()\n    let c = Tc.init()\n    c@copy.v = 1\n    c@copy.v += 1\n")]
    [InlineData("getter_copy", "struct Gc\n    public var a: Tc = Tc.init()\n        get() -> Tc => storage\n    public init() => ()\nfunc gc() -> Gc => Gc.init()\nfunc f()\n    gc().a@copy.v = 1\n    gc().a = Tc.init()\n")]
    [InlineData("move", "func f(t: Tw)\n    t@move.v = 1\n    let u = Tw.init()\n    u@move.q = 1\n")]
    [InlineData("payload", "func f()\n    Kimi.Intrinsics.makeObj(Tw.init()).v = 1\n    Kimi.Intrinsics.makeObj(Tw.init())@follow.v = 1\n    Kimi.Intrinsics.makeObj(Tw.init())@follow = Tw.init()\n")]
    [InlineData("borrow", "func f()\n    take(tw().v@uniq)\n    take(pair().0@uniq)\n    take(tw().pt.x@uniq)\n    let c = Tc.init()\n    take(c@copy.v@uniq)\n")]
    [InlineData("receiver", Bump + "struct Bh\n    public var h: obj/Bc = Kimi.Intrinsics.makeObj(Bc.init())\n    public var c: Bc = Bc.init()\n    public init() => ()\nfunc bh() -> Bh => Bh.init()\nfunc f()\n    bh().c.bump()\n    bh().h.bump()\n")]
    [InlineData("referent", "func f()\n    tw()@uniq@follow = Tw.init()\n    tw()@uniq@follow.v = 1\n")]
    [InlineData("follow", "func f<s/T>(x: s/T, a: s/Tw, b: s/Tw, c: s/Tw, d: s/Tw)\n    s is owner or uniq\n    a@move@follow.v = 1\n    take(b@move@follow.v@uniq)\n    _ = look(c@move@follow.v@ref)\n    d@move.v += 1\nfunc g() => f(1, tw(), tw(), tw(), tw())\n")]
    [InlineData("follow_whole", FollowWhole)]
    public void APartOfATemporaryIsWritable(string form, string body)
    {
        var source = Take + Made + Temps + body + "public func main() => ()\n";
        var check = DiagnosticCorpus.Check(source);
        Assert.True(check.Accepted && check.Diagnostics.Length == 0, $"{form}: {string.Join("; ", check.Diagnostics.Select(static x => x.Code))}");
        Assert.Contains("f", MinimalEmissionTest.GeneratedBodies(source, OutputKind.Library, accepted: true));
    }

    // The invalid counterparts of APartOfATemporaryIsWritable keep their records: a getter temporary's part (SPEC 11.5), a let Field
    // (11.1, 11.2), an exclusive borrow of a custom accessor's Field (11.2), a moved value read after its temporary's write (15.1.1), and
    // an unspelled exclusive argument, the part being an owned Place outside a Receiver Expression (15.1.5's lending rule); through a
    // pair layer, an owned-Type parameter's or a let local's part in the owner case, also as a uniq/Self Receiver Expression (15.1.5),
    // and a ref case (13.5.5); a Temporary Value itself as an assignment's target, also parenthesized (13.7.1: no Place expression, the
    // owner's O-438 (a)); a borrow of a temporary's part used after the temporary ends or returned (3.6: it cannot outlive it). A
    // borrow of another Origin than the temporary's own written into its reference part is valid (3.4.1: a Place in its Temporary
    // Place; 3.6: writable; 15.6: the written value's Loans are added to the holder) and accepted since R7-O1 H, which deletes Binding's
    // body Origin judgment and with it PLAN G1068's located limit at such a write (A5 judges it; the Middle's Phase 7 ruling names the
    // acceptance move); its invalid twins (the temporary kept alive, an exclusive write beside the temporary's shared part, a written
    // Origin that cannot outlive the Field's) stay rejected. Only the records after the preludes are compared.
    [Theory]
    [InlineData("getter", "func f() => Holder.init().cpt.x = 1\n", "InvalidAssignment_Kd Holder.init().cpt.x")]
    [InlineData("getter_borrow", "func f() => take(Holder.init().cpt.x@uniq)\n", "InvalidAssignment_Kd Holder.init().cpt.x@uniq")]
    [InlineData("let", "func f() => tw().k = 1\n", "InaccessibleBinding_Kd tw().k")]
    [InlineData("let_borrow", "func f() => take(tw().k@uniq)\n", "InvalidAssignment_Kd tw().k@uniq")]
    [InlineData("custom_borrow", "func f()\n    take(tw().q@uniq)\n    take(tw().s@uniq)\n", "InvalidAssignment_Kd tw().q@uniq; InvalidAssignment_Kd tw().s@uniq")]
    [InlineData("moved", "func f() -> i32\n    var u = Tw.init()\n    u@move.v = 1\n    return u.v\n", "MovedPlace_Kd u.v")]
    [InlineData("unspelled", "func f() => take(tw().v)\n", "ExclusiveBorrowRequired_Kd tw().v")]
    [InlineData("follow_param", "func f<s/T>(x: s/T, n: s/Tw, m: s/Tw)\n    s is owner or uniq\n    n@follow.v = 1\n    take(m@follow.v@uniq)\n", "InvalidAssignment_Kd m@follow.v@uniq; InvalidAssignment_Kd n@follow.v")]
    [InlineData("follow_shared", "func f<s/T>(x: s/T, n: s/Tw)\n    s is owner or ref\n    n@move@follow.v = 1\n", "SharedPathAccess_Kd n@move@follow.v")]
    [InlineData("follow_let", Bump + "func f<s/T>(x: s/T, n: s/Tw, b: s/Bc)\n    s is owner or uniq\n    let m = n@move\n    take(m@follow.v@uniq)\n    b.bump()\n", "InvalidAssignment_Kd m@follow.v@uniq; NoApplicableOverload_Kd b.bump()")]
    [InlineData("whole", "func f()\n    tw() = Tw.init()\n    (tw()) = Tw.init()\n    1 = 2\n    var u = Tw.init()\n    u@move = Tw.init()\n", "InvalidAssignment_Kd (tw()); InvalidAssignment_Kd 1; InvalidAssignment_Kd tw(); InvalidAssignment_Kd u@move")]
    [InlineData("outlive", "func f()\n    let r = tw().v@uniq\n    take(r)\n    let t = pair().0@uniq\n    t@follow = 3\n", "ComparisonLoanConflict_Kd pair(); ComparisonLoanConflict_Kd tw()")]
    [InlineData("return", "func g(w: uniq/Tw) -> uniq/i32 during w\n    return tw().v@uniq\n", "UnsatisfiedOriginRelation_Kd tw().v@uniq")] // The twin at `return` went (R7-O1 H).
    [InlineData("origin_limit", RefParts, "")]
    // Its invalid twins: the temporary kept alive past a write of the borrowed local, an exclusive write beside the temporary's
    // shared part, and a written Origin that cannot outlive the Field's.
    [InlineData("origin_kept", RefPrelude + "func fk()\n    var x = 1\n    let y = 2\n    var t = mkG(y@ref)\n    t.r = x@ref\n    x = 3\n    Console.writeLine(\"\\(t.r@follow)\")\n", "ComparisonLoanConflict_Kd x = 3")]
    [InlineData("origin_excl", ExclusivePart, "ComparisonLoanConflict_Kd x@ref")]
    [InlineData("origin_short", RefPrelude + "func fs(p: ref/i32)\n    let l = 5\n    mkG(p).r = l@ref\n", "UnsatisfiedOriginRelation_Kd l@ref")]
    public void APartOfATemporaryKeepsItsPermissions(string form, string body, string records)
    {
        var prelude = Take + Made + Temps;
        var source = prelude + body + "public func main() => ()\n";
        var shown = DiagnosticCorpus.Check(source).Diagnostics.Where(x => x.Span!.Value.Start >= prelude.Length)
            .Select(x => x.Code + " " + source.Substring(x.Span!.Value.Start, x.Span.Value.Length).Split('\n')[0]).Order(StringComparer.Ordinal);
        Assert.True(string.Join("; ", shown) == records, $"{form}: expected [{records}], found [{string.Join("; ", shown)}]");
    }

    [Fact]
    public void AFieldThroughSeveralLayersIsABarePlaceSubject()
    {
        // SPEC 15.1.6, 3.4.3: `r.n` through `ref/(ref/S)` is a Place, so the Subject is Shared and binds `ref/i32`.
        var c = MinimalEmissionTest.Analyze(S + "func f(r: ref/(ref/S during a)) -> i32\n    match r.n\n        let v => return v@follow");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 3.6, 6.5.2: a Case construction is a Temporary Value, qualified, in parentheses or with a nested payload, lent shared or,
    // spelled, exclusively; it is analyzed and generated.
    [Theory]
    [InlineData(Cases + "let a = lookc((Color.Red)@ref) + lookc(Color.Red@ref) + lookc(Color.Blue) + lookc((Color.Blue))")]
    [InlineData(Cases + "let a = bumpc((Color.Blue)@uniq) + bumpc(Color.Red@uniq)")]
    [InlineData(Cases + "let a = lo((Option<i32>.None)@ref) + lo(Kimi.Option<i32>.None@ref) + lo(Option<i32>.Some(2)@ref)")]
    [InlineData(Cases + "let a = looks((Shape.Box(7, Color.Red))@ref) + looks(Shape.Dot(Color.Blue)@ref)")]
    public void ACaseConstructionIsATemporaryValue(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var generated = c.Generate(TextWriter.Null);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified && generated, MinimalEmissionTest.Describe(c));
    }

    // The checked records a row's own text locates, after the Statics prelude, whose StaticStorage records
    // ThePreludesStaticsAreUnsupportedAtCheck asserts once (OI-1 (a′), DECISIONS R5-M156).
    private static IEnumerable<(string Code, int Start, int Length)> RowRecords(string source)
        => DiagnosticCorpus.Check(source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length))
            .Where(static x => x.Start >= Statics.Length);

    // The collector's StaticStorage record of each static of the source's group whose declaration begins with a head ("var counter:"):
    // Unsupported_Kd over the declaration with its accessor lines (P42; DECISIONS R5-M82, R5-M156).
    private static IEnumerable<(string Code, int Start, int Length)> StaticStorageRecords(string source, IEnumerable<string> heads)
        => heads.Select(head =>
        {
            var start = source.IndexOf("    public " + head, source.IndexOf("group ", StringComparison.Ordinal), StringComparison.Ordinal) + "    public ".Length;
            var end = source.IndexOf('\n', start);
            while (source.AsSpan(end + 1).StartsWith("        ", StringComparison.Ordinal))
            {
                end = source.IndexOf('\n', end + 1);
            }

            return (DiagnosticCode.Unsupported_Kd.ToString(), start, end - start);
        });
}
