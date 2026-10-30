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

    // An open struct Fixed with a let Field y, which an exclusive reference to it does not lend exclusively (SPEC 3.4, 15.1.5).
    private const string Fixed = "open struct Fixed\n    public var x: i32 = 0\n    public let y: i32 = 1\n    public init() => ()\n";

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
    // let ones of a derived Type, an rc one), a derived struct whose base method borrows its base, function values (var, let, one taking
    // an i32, a struct Holder's, an object handle's, an Array's, obj and rc handles of one, var and let Arrays of an Exclusive Closure), a
    // var whose setter is private, one whose setter is custom and computed Fields of a scalar and a struct; Point's computed Field cx and
    // exclusive and retaining shared methods; the group's glook borrows its argument, and its function bump has its body appended.
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
    // through its accessors, on every pipeline, also beside a Binding error after the prelude (SPEC 23.1.2), so the rows above assert
    // their own records once. P42 reverts this test.
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
        var group = Statics.IndexOf("group State", StringComparison.Ordinal);
        var expected = declarations.Select(head =>
        {
            var start = Statics.IndexOf("    public " + head, group, StringComparison.Ordinal) + "    public ".Length;
            return (DiagnosticCode.Unsupported_Kd.ToString(), start, Statics.IndexOf("\n    public ", start, StringComparison.Ordinal) - start);
        }).ToList();

        // Beside a Binding error the old analysis does not run; in a complete program it reports its own Unsupported_Kd at the custom
        // setter's static write (P42).
        expected.Add(at.Length > 0
            ? (DiagnosticCode.UnresolvedBinding_Kd.ToString(), source.IndexOf(at, Statics.Length, StringComparison.Ordinal), at.Length)
            : (DiagnosticCode.Unsupported_Kd.ToString(), Statics.IndexOf("State.counter = value", StringComparison.Ordinal), "State.counter".Length));

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

    // The checked records a row's own text locates, after the Statics prelude, whose StaticStorage records
    // ThePreludesStaticsAreUnsupportedAtCheck asserts once (OI-1 (a′), DECISIONS R5-M156).
    private static IEnumerable<(string Code, int Start, int Length)> RowRecords(string source)
        => DiagnosticCorpus.Check(source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length))
            .Where(static x => x.Start >= Statics.Length);
}
