// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A Closure over a `var` local without a stated Origin (SPEC 15.3.6, 15.8.2, 7.7.2, 7.7.3; DECISIONS G553, G570): the local's Type
/// holds a local region, one Origin solved over all its bounds, which the Closure's environment and its published result name alike. A
/// result keeps its dependence on the call receiver and the capture's Loans exactly as over a `let` binding or a stated Origin.</summary>
public sealed partial class MirBorrowsTest
{
    private const string OverParameter = "func f(value: uniq/i32 during q) -> ()\n    var v = value@move\n";

    private const string OverLocal = "func inspect<T>(value: ref/T) => ()\nfunc f() -> ()\n    var x: i32 = 1\n    var v = x@ref\n";

    private const string Arrow = "    var h = func [v] () => v\n";

    private const string TwoLive = "    let r = h()\n    let r2 = h()\n    r@follow = 1\n    r2@follow = 2\npublic func main() => ()\n";

    private const string WriteLive = "    let r = h()\n    x = 5\n    inspect(r)\npublic func main() => ()\n";

    private const string Main = "public func main() => ()\n";

    private const string See = "func see(p: ref/i32) => ()\n";
    // A Closure over a `var` r with two bounds, p and a local's Borrow, whose body holds a clause: the rows of AClauseOverAnEnclosingRegionIsARow.
    private const string ClauseOver = See + "func f(p: ref/i32)\n    var q: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n";

    private const string ClauseEnd = "        return w\n    r = q@ref\n    let n = c()\n    see(n@ref)\n" + Main;

    // A struct whose clause relates its two Origins, built over a Closure's result: the rows of
    // AStructsClauseAtItsConstructorOverAnEnclosingRegionIsTheCallsRow and its twins.
    private const string ClauseAtConstructor = See + "struct P {a, b}\n    origin a outlives b\n    public let x: ref/i32 during a\n    public let y: ref/i32 during b\n" +
        "    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\nfunc f(p: ref/i32)\n";

    // A call's and a constructor's Function Type parameter, the destinations of a named Closure value (the rows ag2v, ag7v).
    private const string TakeFn = "func takeFn(g: () -> i32) -> i32 => g()\n";

    private const string HoldFn = "struct H\n    public var f: () -> i32\n    public init(f: () -> i32)\n        self.f = f@move\n";

    // A Closure header over an enclosing `let` region r (the rows of AClosuresHeaderReadsARegionItCarriesAsItsValue): Box's slot takes the
    // header result's default, which reads r's region as its value exactly where the Closure carries it.
    private const string HeaderBox = "struct Box {a}\n    public let v: ref/i32 during a\n    public init(v: ref/i32 during a) => self.v = v\n";

    private const string HeaderLet = Main + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    let r = p\n";

    private const string HeaderOf = "(k: Option<ref/i32 during r>) -> Box => $abort(\"no\")";

    private const string TakeG = "func takeG<T>(g: (Option<ref/i32 during static>) -> T) -> i32 => 0\n";

    // The same header over a `var` region r with candidate `static` so far, or over a `let` r bound to a body local's Borrow, and a body
    // that returns r in a Box (the rows of AClosuresHeaderDefaultIsJudgedOnceItsBodyIsBound and its invalid twins, R7-O2 O2-F).
    private const string HeaderVar = Main + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n";

    private const string HeaderBorrow = Main + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    let r = n@ref\n";

    private const string HeaderInit = "(k: Option<ref/i32 during r>) -> Box => Box.init(r)";

    private const string TakeG2 = "func takeG2<T>(g: (Option<ref/i32 during static>) -> T, x: i32) -> i32 => 0\n";

    // f with a caller's borrow s, and a body-local annotation's Function Type over the `var` r (the rows fsh*, bod2*, fty*; R7-O2 O2-F).
    private const string HeaderS = Main + "func f(p: ref/i32 during static, s: ref/i32) -> i32\n";

    private const string AnnotatedG = "    let g: (Option<ref/i32 during r>) -> Box = ";

    // A Function Item whose input's result names its universal o (SPEC 15.3.4), and a header with a conditional direct-borrow input y
    // (SPEC 15.4.3, 8.1.2): the rows fda2v/fda2n/fda3v and pm2v/pm2n/pm3v (R7-O2 O2-F, design §10.7's C3).
    private const string TakeO = "func take(h: (Option<ref/i32 during static>) -> Box during o, x: ref/i32 during o) -> i32 => 0\n";

    private const string PairG = Main + "func g<s/T>(p: ref/i32 during static, x: s/T) -> i32\n    s is owner or ref\n    T is Owned\n    let n: i32 = 1\n    var r = p\n";

    private const string PairOf = "(y: s/T during r, k: Option<ref/i32 during r>) -> Box => $abort(\"no\")";

    // SPEC 15.8.2's table, an exclusive capture: "Reborrow of a captured exclusive reference ... in a Shared or Exclusive call, depends on
    // the call receiver", so while a result is live a second Exclusive call (7.7.3, 15.6.2), or a Move of the Closure, conflicts: an arrow
    // and a block body, only the first result used, a local's borrow, an elided Origin, a second capture, a Closure held in a Field, one
    // called through `uniq/F`, `[var v]` and `[v@move]`, an assigned `var`, a Move of the Closure, a Tuple part, an explicit Reborrow, a
    // Shared call's Reborrow, a Closure in a Closure's environment, a generic body. The local region's sources need not be the
    // signature's: one assigned after the capture, which the signature lacks, and two distinct ones. The rows the `let` forms and a stated
    // Origin have, which stay. A shared capture "keeps its external Origin", and the capture's Loans with it, so `x = 5` errs while r is
    // live: with and without a capture list, in a block whose Closure is dead, in a struct, in a Closure's environment. An owned Array
    // replaced under r is the use after destruction. A Consuming call "transfers its Origin and capability": a struct holding the shared
    // reference, an exclusive reference, and that one from a `let` binding. A result naming a source of the capture carries a read through
    // the receiver even when its body returns the parameter, retention being per argument (15.6.4), over a `var` as over a `let`. A Closure
    // read through another keeps all this: called in its environment, through a borrow of it, defined in its body, held in a Field of it.
    // An annotation `during r` names the region itself, and a call adds to it: stored in a `var` binding, and the argument the result may
    // hold though the body returns the capture. A result over a `var` region one of whose sources is a captured `let` local's value may
    // retain that capture, the local's region being its solved value (PLAN G492-a (ii); each accepted before): the body returning the
    // parameter, the source added after the Closure, two bodies deep, a call's region in the capture, a meet with a parameter Origin, a
    // Closure over the Closure.
    [Theory]
    [InlineData(OverParameter + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData(OverParameter + "    var h = func [v] ()\n        return v\n" + TwoLive, "let r2 = h()", "h")]
    [InlineData(OverParameter + Arrow + "    let r = h()\n    let r2 = h()\n    r@follow = 1\n" + Main, "let r2 = h()", "h")]
    [InlineData("func f() -> ()\n    var x: i32 = 1\n    var v = x@uniq\n" + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData("func f(value: uniq/i32) -> ()\n    var v = value@move\n" + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData("func f(value: uniq/i32 during q, other: uniq/i32 during q) -> ()\n    var v = value@move\n    var w = other@move\n    var h = func [v, w] () => v\n" + TwoLive, "let r2 = h()", "h")]
    [InlineData("struct Keeper<F>\n    public var f: F\n    public init(f: F) => self.f = f@move\n" + OverParameter + "    var k = Keeper.init(func [v] () => v)\n    let r = k.f()\n    let r2 = k.f()\n    r@follow = 1\n    r2@follow = 2\n" + Main, "let r2 = k.f()", "k.f")]
    [InlineData(OverParameter + Arrow + "    let g = h@uniq\n    let r = g()\n    let r2 = g()\n    r@follow = 1\n    r2@follow = 2\n" + Main, "let r2 = g()", "g")]
    [InlineData(OverParameter + "    var h = func [var v] () => v\n" + TwoLive, "let r2 = h()", "h")]
    [InlineData(OverParameter + "    var h = func [v@move] () => v\n" + TwoLive, "let r2 = h()", "h")]
    [InlineData("func f(value: uniq/i32 during q, other: uniq/i32 during q) -> ()\n    var v = value@move\n    v = other@move\n" + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData(OverParameter + Arrow + "    let r = h()\n    let moved = h@move\n    r@follow = 1\n" + Main, "let moved = h@move", "h")]
    [InlineData("func f(value: uniq/i32 during q) -> ()\n    var t = (value@move, true)\n    var h = func [t@move] () => t.0\n" + TwoLive, "let r2 = h()", "h")]
    [InlineData(OverParameter + "    var h = func [v] () => v@follow@uniq\n" + TwoLive, "let r2 = h()", "h")]
    [InlineData("func inspect<T>(value: ref/T) => ()\n" + OverParameter + "    var h = func [v] () => v@follow@ref\n    let r = h()\n    let moved = h@move\n    inspect(r)\n" + Main, "let moved = h@move", "h")]
    [InlineData(OverParameter + Arrow + "    var g = func [var h@move] () => h()\n    let r = g()\n    let r2 = g()\n    r@follow = 1\n    r2@follow = 2\n" + Main, "let r2 = g()", "g")]
    [InlineData("func f<T>(value: uniq/T during q, x: T) -> ()\n    T is Copy\n    var v = value@move\n" + Arrow + "    let r = h()\n    let r2 = h()\n    r@follow = x\n    r2@follow = x\n" + Main, "let r2 = h()", "h")]
    [InlineData("func f(value: uniq/i32 during p, other: uniq/i32 during q) -> ()\n    var v = value@move\n" + Arrow + "    let r = h()\n    let r2 = h()\n    r@follow = 1\n    r2@follow = 2\n    v = other@move\n" + Main, "let r2 = h()", "h")]
    [InlineData("func f(value: uniq/i32 during p, other: uniq/i32 during q, c: bool) -> ()\n    var v = value@move\n    if c\n        v = other@move\n" + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData("func f(value: uniq/i32 during q) -> ()\n    let v = value\n" + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData("func f() -> ()\n    var x: i32 = 1\n    let v = x@uniq\n" + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData("func f(value: uniq/i32 during q) -> ()\n    var v: uniq/i32 during q = value@move\n" + Arrow + TwoLive, "let r2 = h()", "h")]
    [InlineData(OverLocal + Arrow + WriteLive, "x = 5", null)]
    [InlineData(OverLocal + "    var h = func () => v\n" + WriteLive, "x = 5", null)]
    [InlineData("func inspect<T>(value: ref/T) => ()\nfunc f() -> ()\n    var x: i32 = 1\n    var r: ref/i32 = x@ref\n    do\n        var v = x@ref\n        var h = func [v] () => v\n        r = h()\n    x = 5\n    inspect(r)\n" + Main, "x = 5", null)]
    [InlineData("struct View {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\nfunc inspect<T>(value: ref/T) => ()\nfunc f() -> ()\n    var x: i32 = 1\n    var w = View.init(x@ref)\n    var h = func [w@move] () => w.r\n" + WriteLive, "x = 5", null)]
    [InlineData(OverLocal + Arrow + "    var g = func [h@move] () => h()\n    let r = g()\n    x = 5\n    inspect(r)\n" + Main, "x = 5", null)]
    [InlineData("func inspect<T>(value: ref/T) => ()\nfunc f() -> ()\n    var a: Array<i32> = [1, 2]\n    var v = a@ref\n" + Arrow + "    let r = h()\n    a = [3]\n    inspect(r)\n" + Main, "a = [3]", null)]
    [InlineData("struct View {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\nfunc inspect<T>(value: ref/T) => ()\nfunc f() -> ()\n    var x: i32 = 1\n    var w = View.init(x@ref)\n    var h = func [w@move] () => w@move\n    let r = h@move()\n    x = 5\n    inspect(r.r)\n" + Main, "x = 5", null)]
    [InlineData("func f() -> ()\n    var x: i32 = 1\n    var v = x@uniq\n    var h = func [v@move] () => v@move\n    let r = h@move()\n    x = 5\n    r@follow = 1\n" + Main, "x = 5", null)]
    [InlineData("func f() -> ()\n    var x: i32 = 1\n    let v = x@uniq\n    var h = func [v@move] () => v@move\n    let r = h@move()\n    x = 5\n    r@follow = 1\n" + Main, "x = 5", null)]
    [InlineData("func f(value: uniq/i32 during q, other: uniq/i32 during q) -> ()\n    var v = value@move\n    var h = func [v] (p: uniq/i32 during q) -> uniq/i32 during q => p\n    let a = h(other)\n    v@follow = 3\n    a@follow = 1\n" + Main, "v@follow = 3", null)]
    [InlineData("func f(value: uniq/i32 during q, other: uniq/i32 during q) -> ()\n    let v = value\n    var h = func [v] (p: uniq/i32 during q) -> uniq/i32 during q => p\n    let a = h(other)\n    v@follow = 3\n    a@follow = 1\n" + Main, "v@follow = 3", null)]
    [InlineData(See + "func t()\n    var x: i32 = 1\n    var r = x@ref\n    let inner = func [r] () => r\n    let outer = func [inner] () => inner()\n    let v = outer()\n    x = 2\n    see(v)\n" + Main, "x = 2", null)]
    [InlineData("func t()\n    var x: i32 = 1\n    var r = x@uniq\n    var inner = func [r] () => r\n    var outer = func [inner@uniq] () => inner@follow()\n    let a = outer()\n    let b = outer()\n    a@follow = 2\n    b@follow = 3\n" + Main, "let b = outer()", "outer")]
    [InlineData(See + "func t()\n    var x: i32 = 1\n    var r = x@ref\n    let outer = func [r] ()\n        let inner = func [r] () => r\n        return inner()\n    let v = outer()\n    x = 2\n    see(v)\n" + Main, "x = 2", null)]
    [InlineData("struct H<F>\n    public var f: F\n    public init(f: F)\n        self.f = f@move\n" + See + "func t()\n    var x: i32 = 1\n    var r = x@ref\n    let h = H.init(func [r] () => r)\n    let d = func [h@ref] () => h.f()\n    let v = d()\n    x = 2\n    see(v)\n" + Main, "x = 2", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var r = a@ref\n    var c = func [var r] (p: ref/i32 during r) -> ref/i32 during r\n        r = p\n        return r\n    _ = c(b@ref)\n    b = 3\n    let v = c(a@ref)\n    see(v)\n" + Main, "b = 3", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var r = a@ref\n    let c = func [r] (p: ref/i32 during r) -> ref/i32 during r => r\n    let v = c(b@ref)\n    b = 3\n    see(v)\n" + Main, "b = 3", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    var r = sa\n    r = sb\n    let c = func [sa] (p: ref/i32 during r) -> ref/i32 during r => p\n    let v = c(r)\n    a = 3\n    see(v)\n" + Main, "a = 3", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    var r = sa\n    let c = func [sa] (p: ref/i32 during r) -> ref/i32 during r => p\n    r = b@ref\n    let v = c(r)\n    a = 3\n    see(v)\n" + Main, "a = 3", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    var r = sa\n    r = b@ref\n    let outer = func [sa] (p: ref/i32 during r) -> ref/i32 during r\n        let w = sa\n        let inner = func [w] (q: ref/i32 during r) -> ref/i32 during r => q\n        return inner(p)\n    let v = outer(r)\n    a = 3\n    see(v)\n" + Main, "a = 3", null)]
    [InlineData("func id(p: ref/i32) -> ref/i32 during p => p\n" + See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    let x = id(sa)\n    var r = sa\n    r = b@ref\n    let c = func [x] (q: ref/i32 during r) -> ref/i32 during r => q\n    let v = c(r)\n    a = 3\n    see(v)\n" + Main, "a = 3", null)]
    [InlineData(See + "func f(q: ref/i32, k: bool)\n    var a: i32 = 1\n    let sa = a@ref\n    var r = q\n    if k => r = sa\n    let c = func [sa] (z: ref/i32 during r) -> ref/i32 during r => z\n    let v = c(q)\n    a = 3\n    see(v)\n" + Main, "a = 3", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    var r = sa\n    r = b@ref\n    let inner = func [sa] (z: ref/i32 during r) -> ref/i32 during r => z\n    let outer = func [inner] (z: ref/i32 during r) -> ref/i32 during r => inner(z)\n    let v = outer(r)\n    a = 3\n    see(v)\n" + Main, "a = 3", null)]
    public void AResultOverACapturedRegionKeepsTheReceiverAndTheCapture(string source, string context, string? subject)
        => MirTargetSide.Check(source, (DiagnosticCode.ComparisonLoanConflict_Kd, MirTargetSide.At(context, subject)));

    // The valid twins: one call, a first result dead at the second call (also the second alone used, and a later source), a shared capture's
    // two results, also over a Move of the Closure, a Copy result, a Shared call's two Reborrows, `v` after the last use of the Closure's
    // results, a result that is the parameter with its capture unused, a Consuming call's result dead before the write; each result over a
    // region a captured `let` local's value is a source of, dead before the write.
    [Theory]
    [InlineData(OverParameter + Arrow + "    let r = h()\n    r@follow = 1\n" + Main)]
    [InlineData(OverParameter + Arrow + "    let r = h()\n    r@follow = 1\n    let r2 = h()\n    r2@follow = 2\n" + Main)]
    [InlineData(OverParameter + Arrow + "    let r = h()\n    let r2 = h()\n    r2@follow = 2\n" + Main)]
    [InlineData("func f(value: uniq/i32 during p, other: uniq/i32 during q) -> ()\n    var v = value@move\n" + Arrow + "    let r = h()\n    r@follow = 1\n    let r2 = h()\n    r2@follow = 2\n    v = other@move\n" + Main)]
    [InlineData("func inspect<T>(value: ref/T) => ()\nfunc f(value: ref/i32 during q) -> ()\n    var v = value\n" + Arrow + "    let r = h()\n    let r2 = h()\n    inspect(r)\n    inspect(r2)\n" + Main)]
    [InlineData("func inspect<T>(value: ref/T) => ()\nfunc f(value: ref/i32 during q) -> ()\n    var v = value\n" + Arrow + "    let r = h()\n    let moved = h@move\n    inspect(r)\n" + Main)]
    [InlineData(OverParameter + "    var h = func [v] () => v@follow\n    let r = h()\n    let r2 = h()\n    _ = r + r2\n" + Main)]
    [InlineData("func inspect<T>(value: ref/T) => ()\n" + OverParameter + "    var h = func [v] () => v@follow@ref\n    let r = h()\n    let r2 = h()\n    inspect(r)\n    inspect(r2)\n" + Main)]
    [InlineData(OverParameter + Arrow + "    let r = h()\n    r@follow = 1\n    v@follow = 2\n" + Main)]
    [InlineData("func f(value: uniq/i32 during q, other: uniq/i32 during q, third: uniq/i32 during q) -> ()\n    var v = value@move\n    var h = func [v] (p: uniq/i32 during q) -> uniq/i32 during q => p\n    let a = h(other)\n    let b = h(third)\n    a@follow = 1\n    b@follow = 2\n" + Main)]
    [InlineData("func f() -> ()\n    var x: i32 = 1\n    var v = x@uniq\n    var h = func [v@move] () => v@move\n    let r = h@move()\n    r@follow = 1\n    x = 5\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    var r = sa\n    r = sb\n    let c = func [sa] (p: ref/i32 during r) -> ref/i32 during r => p\n    let v = c(r)\n    see(v)\n    a = 3\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    var r = sa\n    let c = func [sa] (p: ref/i32 during r) -> ref/i32 during r => p\n    r = b@ref\n    let v = c(r)\n    see(v)\n    a = 3\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    var r = sa\n    r = b@ref\n    let outer = func [sa] (p: ref/i32 during r) -> ref/i32 during r\n        let w = sa\n        let inner = func [w] (q: ref/i32 during r) -> ref/i32 during r => q\n        return inner(p)\n    let v = outer(r)\n    see(v)\n    a = 3\n" + Main)]
    [InlineData("func id(p: ref/i32) -> ref/i32 during p => p\n" + See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    let x = id(sa)\n    var r = sa\n    r = b@ref\n    let c = func [x] (q: ref/i32 during r) -> ref/i32 during r => q\n    let v = c(r)\n    see(v)\n    a = 3\n" + Main)]
    [InlineData(See + "func f(q: ref/i32, k: bool)\n    var a: i32 = 1\n    let sa = a@ref\n    var r = q\n    if k => r = sa\n    let c = func [sa] (z: ref/i32 during r) -> ref/i32 during r => z\n    let v = c(q)\n    see(v)\n    a = 3\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    var r = sa\n    r = b@ref\n    let inner = func [sa] (z: ref/i32 during r) -> ref/i32 during r => z\n    let outer = func [inner] (z: ref/i32 during r) -> ref/i32 during r => inner(z)\n    let v = outer(r)\n    see(v)\n    a = 3\n" + Main)]
    public void ACapturedRegionConflictsWithNothingLive(string source) => MirTargetSide.Check(source);

    // SPEC 15.3.6, 15.6.4 (the consent review's B1): a local has one region, the meet of all its sources, and a meet outlives what all its
    // sources do, so a result naming one source of several cannot hold the capture and keeps
    // none of its Loans: a source added after the capture, one before it, either order of a replaced source, two parameters' exclusive
    // references, `static` beside a local Borrow, a Closure that reads its capture, a Consuming call, a Closure over a Closure, one in a Field.
    // The same where a source is a `let` local, whose region is its solved value (PLAN G492-a (ii)): added after the capture, beside a
    // parameter; and a capture over a third local that is no source of the result's region, which the result cannot hold either.
    [Theory]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = b@ref\n    var r = a@ref\n    let c = func [r] (p: ref/i32 during s) -> ref/i32 during s => p\n    r = s\n    let v = c(s)\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = b@ref\n    let u = a@ref\n    var r = u\n    r = s\n    let c = func [r] (p: ref/i32 during u) -> ref/i32 during u => p\n    let v = c(u)\n    b = 3\n    see(v)\n    see(b@ref)\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = b@ref\n    var r = s\n    r = a@ref\n    let c = func [r] (p: ref/i32 during s) -> ref/i32 during s => p\n    let v = c(s)\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func f(q: ref/i32, k: bool)\n    var a: i32 = 1\n    var r = q\n    if k => r = a@ref\n    let c = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n    let v = c(q)\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData("func f(value: uniq/i32 during p, other: uniq/i32 during q, m: uniq/i32 during p, k: bool)\n    var v = value@move\n    if k => v = other@move\n    var h = func [v] (z: uniq/i32 during p) -> uniq/i32 during p => z\n    let a = h(m)\n    v@follow = 3\n    a@follow = 1\n" + Main)]
    [InlineData(See + "func t(s: ref/i32 during static, k: bool)\n    var a: i32 = 1\n    var r = s\n    if k => r = a@ref\n    let c = func [r] (p: ref/i32 during static) -> ref/i32 during static => p\n    let v = c(s)\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func pick(q: ref/i32, k: bool) -> ref/i32 during q\n    var a: i32 = 1\n    var r = q\n    if k => r = a@ref\n    let log = func [r] (p: ref/i32 during q) -> ref/i32 during q\n        see(r)\n        return p\n    let v = log(q)\n    a = 3\n    return v\n" + Main)]
    [InlineData(See + "func consumeText(s: string) => ()\nfunc f(q: ref/i32, k: bool)\n    var a: i32 = 1\n    var r = q\n    if k => r = a@ref\n    let t = \"x\"\n    let c = func [r, t@move] (p: ref/i32 during q) -> ref/i32 during q\n        consumeText(t@move)\n        return p\n    let v = c@move(q)\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func f(q: ref/i32, k: bool)\n    var a: i32 = 1\n    var r = q\n    if k => r = a@ref\n    let inner = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n    let outer = func [inner] (p: ref/i32 during q) -> ref/i32 during q => inner(p)\n    let v = outer(q)\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "struct H<F>\n    public var f: F\n    public init(f: F)\n        self.f = f@move\nfunc f(q: ref/i32, k: bool)\n    var a: i32 = 1\n    var r = q\n    if k => r = a@ref\n    let h = H.init(func [r] (p: ref/i32 during q) -> ref/i32 during q => p)\n    let v = h.f(q)\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let sa = a@ref\n    let sb = b@ref\n    var r = sa\n    let c = func [r] (p: ref/i32 during sb) -> ref/i32 during sb => p\n    r = sb\n    let v = c(sb)\n    a = 3\n    see(v)\n" + Main)]
    [InlineData(See + "func f(q: ref/i32, k: bool)\n    var a: i32 = 1\n    let sa = a@ref\n    var r = q\n    if k => r = sa\n    let c = func [r] (z: ref/i32 during q) -> ref/i32 during q => z\n    let v = c(q)\n    a = 3\n    see(v)\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var d: i32 = 4\n    let sd = d@ref\n    var r = a@ref\n    r = b@ref\n    let c = func [sd] (p: ref/i32 during r) -> ref/i32 during r => p\n    let v = c(r)\n    d = 5\n    see(v)\n" + Main)]
    public void AResultNamingOneSourceCannotHoldTheCapture(string source) => MirTargetSide.Check(source);

    // SPEC 15.3.6, 15.6.4, 15.6.5 (DECISIONS: G570; G553's limit under the owner's O-51 and O-52, which it removes): the region is one meet,
    // so a position naming a source of several holds no capture of the region, whenever the bound arrives: two Closures over one `var` (k2),
    // a bound after the literal with the capture's Loan elsewhere (v7) and its exclusive form (v7u), a literal in a `while`, a `for`, a position
    // over a local, a `defer` with a bound below it (z57, z64, z34, z67). A contract read while the body binds is no contract of the final
    // region: with a parameter's region, an erasure's read of the literal changes nothing (e1) against its twin with no erasure (e1n).
    [Theory]
    [InlineData(See + "func both<A, B, T>(x: ref/A, y: ref/B, t: T) -> T\n    return t@move\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    var r = s\n    let c1 = func [r] () => r\n    r = b@ref\n    let c2 = func [r] () => r\n    let v = both(c1@ref, c2@ref, s)\n    b = 3\n    see(v)\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var c: i32 = 3\n    let s = a@ref\n    let w = b@ref\n    var r = s\n    let h = func [r, w] (p: ref/i32 during s) -> ref/i32 during s => p\n    let v = h(s)\n    r = c@ref\n    b = 3\n    see(v)\n    see(r)\n" + Main)]
    [InlineData("func t(value: uniq/i32 during q, other: uniq/i32 during q, third: uniq/i32 during z)\n    var v = value@move\n    var h = func [v] (p: uniq/i32 during q) -> uniq/i32 during q => p\n    let a = h(other)\n    v@follow = 3\n    a@follow = 1\n    v = third@move\n" + Main)]
    [InlineData(See + "func f(q: ref/i32, n: i32)\n    var a: i32 = 1\n    var r = q\n    var v = q\n    var i: i32 = 0\n    while i < n\n        let c = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n        v = c(q)\n        r = a@ref\n        i += 1\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func f(q: ref/i32)\n    var a: i32 = 1\n    var r = q\n    var v = q\n    for n in 0..2\n        let c = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n        v = c(q)\n        r = a@ref\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let u = a@ref\n    let s = b@ref\n    var r = s\n    var v = s\n    var i: i32 = 0\n    while i < 2\n        let c = func [r] (p: ref/i32 during s) -> ref/i32 during s => p\n        v = c(s)\n        r = u\n        i += 1\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main)]
    [InlineData(See + "func f(q: ref/i32)\n    var a: i32 = 1\n    var r = q\n    var v = q\n    defer\n        let c = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n        v = c(q)\n        a = 3\n        see(v)\n        see(a@ref)\n    r = a@ref\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var d: i32 = 3\n    let s = a@ref\n    var r = s\n    let c = func (x: ref/i32 during r, p: ref/i32 during s) -> ref/i32 during s => p\n    let f: (ref/i32 during r, ref/i32 during s) -> ref/i32 during s = c\n    r = d@ref\n    let v = c(b@ref, s)\n    b = 3\n    see(v)\n    see(r)\n" + Main)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var d: i32 = 3\n    let s = a@ref\n    var r = s\n    let c = func (x: ref/i32 during r, p: ref/i32 during s) -> ref/i32 during s => p\n    r = d@ref\n    let v = c(b@ref, s)\n    b = 3\n    see(v)\n    see(r)\n" + Main)]
    public void APositionNamingOneSourceHoldsNoCaptureWheneverTheBoundArrives(string source) => MirTargetSide.Check(source);

    // SPEC 15.6.4 (DECISIONS: G570, a named move): a local region's atom reaches its sources in any contract, a named generic call's too, so
    // a `var` over one source is read as its `let` twin, per argument (d2's reasoning). PLAN G935 (the owner's O-360 (a), RG1's second
    // half): keep's result, of its own U, holds u (s, a's Loan) and no read through t, a T, which no premise makes a U, so v carries no
    // Loan on b and both rows are valid (relabelled from the instantiated reading); the var/let reading is pinned where it decides a row
    // in the callee's terms by the rows that follow it in G570's unit.
    [Theory]
    [InlineData(See + "func keep<T, U>(t: ref/T, u: U) -> U\n    return u@move\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    var x = s\n    var w = b@ref\n    var h = (x, w)\n    let v = keep(h@ref, s)\n    b = 3\n    see(v)\n" + Main)]
    [InlineData(See + "func keep<T, U>(t: ref/T, u: U) -> U\n    return u@move\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    let x = s\n    let w = b@ref\n    let h = (x, w)\n    let v = keep(h@ref, s)\n    b = 3\n    see(v)\n" + Main)]
    public void AGenericCallReadsAVarOverOneSourceAsItsLetTwin(string source)
        => MirTargetSide.Check(source);

    // G570's purpose where the retention stands in the callee's terms (PLAN G935): readT's result, a borrow formed through t (SPEC
    // 15.6.4), reaches h, which holds w, a `var` over b's shared borrow (x5r) or its `let` twin (x5lr); v is used after `b = 3`, so the
    // write conflicts in both.
    [Theory]
    [InlineData("func seeT<T>(p: T) => ()\nfunc readT<T>(t: ref/T) -> ref/T during t => t\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    var x = s\n    var w = b@ref\n    var h = (x, w)\n    let v = readT(h@ref)\n    b = 3\n    seeT(v)\n" + Main)]
    [InlineData("func seeT<T>(p: T) => ()\nfunc readT<T>(t: ref/T) -> ref/T during t => t\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    let x = s\n    let w = b@ref\n    let h = (x, w)\n    let v = readT(h@ref)\n    b = 3\n    seeT(v)\n" + Main)]
    public void AResultBorrowedThroughAVarOverOneSourceIsReadAsItsLetTwin(string source)
        => MirTargetSide.Check(source, (DiagnosticCode.ComparisonLoanConflict_Kd, MirTargetSide.At("b = 3", null)));

    // SPEC 10.1, 8.3.3, 15.2.3, 15.3.6 (DECISIONS: G572 U2a): an Unknown at step 5 is an obligation judged after every body is bound, when a
    // local region's bounds are complete, so `T is Owned` over a region whose bounds are all `static` holds: a bound over a Closure's result
    // (s1), a constructor's bound (s4), a local (g4), beside a waiting Closure argument (w1) and through a method (w1m). The rows left
    // the owner's O-57 recorded limit (s1, s4), valid by SPEC 15.2.3 (Q13: a valid program rejected before).
    [Theory]
    [InlineData("func need<T>(x: T)\n    T is Owned\nfunc f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    need(c())\n" + Main)]
    [InlineData(See + "struct Box<T>\n    public var v: T\n    public init(v: T)\n        T is Owned\n        self.v = v@move\nfunc f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let b = Box.init(c())\n    see(b.v)\n" + Main)]
    [InlineData("func need<T>(t: T) -> ()\n    T is Owned\nfunc t(x: ref/i32 during static)\n    var s = x\n    need(s)\n" + Main)]
    [InlineData("func apply<T>(t: T, f: (T) -> ()) -> ()\n    T is Owned\n    return\nfunc t(x: ref/i32 during static)\n    var s = x\n    apply(s, func (v) => ())\n" + Main)]
    [InlineData("struct K\n    public init() => ()\n    public func need<T>(self: ref/Self, t: T) -> ()\n        T is Owned\n        ()\nfunc t(x: ref/i32 during static)\n    let k = K.init()\n    var s = x\n    k.need(s)\n" + Main)]
    public void AStaticOnlyRegionIsOwnedOnceEveryBodyIsBound(string source) => MirTargetSide.Check(source);

    // SPEC 10.1, 15.2.3: the invalid twins, a later local source, so the region is not `static` and the obligation fails at the call (h5, and
    // w1n beside a waiting argument).
    [Theory]
    [InlineData("func need<T>(t: T) -> ()\n    T is Owned\nfunc t(x: ref/i32 during static)\n    var a: i32 = 1\n    var s = x\n    need(s)\n    s = a@ref\n    see(s)\n" + See + Main, "need(s)")]
    [InlineData("func apply<T>(t: T, f: (T) -> ()) -> ()\n    T is Owned\n    return\nfunc t(x: ref/i32 during static)\n    var a: i32 = 1\n    var s = x\n    apply(s, func (v) => ())\n    s = a@ref\n    see(s)\n" + See + Main, "apply(s, func (v) => ())")]
    public void AnOwnedRegionWithALaterSourceIsUnproven(string source, string context)
        => MirTargetSide.Check(source, (DiagnosticCode.UnprovenConstraint_Kd, MirTargetSide.At(context, null)));

    // SPEC 15.6.4 step 3, 15.2.3 (PLAN G990 D1, its ruling G990-5): a struct's clause at its constructor over an enclosing region
    // (PLAN G572 U2c) is the call's row, since a construction's result premises over its owner's own Type are (RequireResultPremises),
    // and A5 judges it: c()'s result holds r's one source p, so `a outlives b` is `P outlives P`, in the own body (t31) and in a nested
    // body (n21). It was the owner's O-57 and O-59 recorded limit (verbatim 「両方とも(a)でお願いします。」, 「すべて (a)」), UnprovenConstraint_Kd at
    // the constructor call until its unit turned it accepted (Q13: a valid program rejected before).
    [Theory]
    [InlineData(ClauseAtConstructor + "    var r = p\n    let c = func [r] () => r\n    let b = P.init(c(), p)\n    see(b.x)\n" + Main)]
    [InlineData(ClauseAtConstructor + "    var r = p\n    let c = func [r, p] () -> i32\n        let inner = func [r] () => r\n        let b = P.init(inner(), p)\n        see(b.x)\n        return 0\n    let n = c()\n" + Main)]
    public void AStructsClauseAtItsConstructorOverAnEnclosingRegionIsTheCallsRow(string source) => MirTargetSide.Check(source);

    // SPEC 15.6.4 step 3, 15.6.5: the invalid twins, a later local source (AnOwnedRegionWithALaterSourceIsUnproven's form), so r's region
    // holds q's Borrow, which the clause needs to outlive p: Refuted in the own body (t31n), not proven where the nested body carries r
    // (n21n, as AClauseOverAnEnclosingRegionIsARow's c1i); A5's record at the constructor call, relating the clause.
    [Theory]
    [InlineData(ClauseAtConstructor + "    var q: i32 = 1\n    var r = p\n    let c = func [r] () => r\n    let b = P.init(c(), p)\n    see(b.x)\n    r = q@ref\n    see(r)\n" + Main, DiagnosticCode.UnsatisfiedOriginRelation_Kd, "P.init(c(), p)")]
    [InlineData(ClauseAtConstructor + "    var q: i32 = 1\n    var r = p\n    let c = func [r, p] () -> i32\n        let inner = func [r] () => r\n        let b = P.init(inner(), p)\n        see(b.x)\n        return 0\n    r = q@ref\n    let n = c()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "P.init(inner(), p)")]
    public void AStructsClauseAtItsConstructorOverALaterLocalSourceIsA5s(string source, DiagnosticCode code, string context)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, null)));

    // SPEC 10.7, 7.7.4, 15.6.1 (the owner's O-61, verbatim 「O-61 a」; DECISIONS: G570): a comparison with a Function Type is judged by the
    // contract alone: a Callable bound over a `var`'s region (c12) keeps its record. The conversion's Owned condition reads a local `var`
    // region as its candidate once its body is bound (the owner's O-481 (b), verbatim 「O-481 は (b) でよいです」; R7-O2 A1+A2), so its
    // erasures at a stated `let` (e6v, s2h) are valid (AnErasuresOwnedReadsAVarRegionsCandidateOnceItsBodyIsBound). At a call's or a
    // constructor's argument (s2c, e6c, s9) the record is at the argument since PLAN G379 (a): applicability reads
    // the erasure's structural part alone and its Origin part is judged after selection (SPEC 15.6.1, 10.8; DECISIONS R4-M155's t1d_n
    // precedent). There the region's Owned is decided only once the body is bound, so the erasure is the located Unsupported_Kd at the
    // argument (the owner's O-406 (a), G379 (a) R4-R5: a part not decidable before finality at applicability), also for a named value in
    // a nested body over a carried `var` region, which reads as its candidate and has no answer while the body is bound: at a call's
    // argument (ag2v) and a constructor's (ag7v), valid, and their invalid twins (ag2n, ag7n; R7-O2 A1+A2 moved all four from
    // UnprovenConstraint_Kd, Q13: dev read no carried `var` region). G572 U1's f3v (an erasure
    // whose input needs `p outlives R`, R's one bound p) is invalid: R is only named by the nested signature, which gives no premise
    // (SPEC 15.6.5's last clause; R7-O2 A1's ruling 2): MirBorrowsTest.Comparisons.cs.
    [Theory]
    [InlineData(See + "func two(f: () -> ref/i32 during static)\n    see(f())\nfunc t(x: ref/i32 during static)\n    var s = x\n    let c = func [s] () => s\n    two(c@move)\n" + Main, DiagnosticCode.Unsupported_Kd, "c@move", null)] // O-406 (a): the located limit at the argument (G379 (a) R4-R5)
    [InlineData(See + "struct H\n    public var f: () -> ref/i32 during static\n    public init(f: () -> ref/i32 during static)\n        self.f = f@move\nfunc g(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let h = H.init(c@move)\n    see(h.f())\n" + Main, DiagnosticCode.Unsupported_Kd, "c@move", null)] // O-406 (a): the located limit at the argument (G379 (a) R4-R5)
    [InlineData(See + "func two(f: () -> ref/i32 during static)\n    see(f())\nfunc k(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    let g = func [y] () => y\n    two(g@move)\n" + Main, DiagnosticCode.Unsupported_Kd, "g@move", null)] // O-406 (a): the located limit at the argument (G379 (a) R4-R5)
    [InlineData(See + "func apply<F>(g: ref/F) -> i32\n    F is Callable<() -> ref/i32>\n    let w = g()\n    return w@follow\nfunc f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let n = apply(c@ref)\n    see(n@ref)\n" + Main, DiagnosticCode.UnprovenConstraint_Kd, "apply(c@ref)", null)]
    [InlineData(TakeFn + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        let g = func [r] () -> i32 => r@follow\n        return takeFn(g@move)\n    r = p\n    return c()\n" + Main, DiagnosticCode.Unsupported_Kd, "g@move", null)]
    [InlineData(TakeFn + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        let g = func [r] () -> i32 => r@follow\n        return takeFn(g@move)\n    r = n@ref\n    return c()\n" + Main, DiagnosticCode.Unsupported_Kd, "g@move", null)]
    [InlineData(HoldFn + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        let g = func [r] () -> i32 => r@follow\n        let h = H.init(g@move)\n        return 1\n    r = p\n    return c()\n" + Main, DiagnosticCode.Unsupported_Kd, "g@move", null)]
    [InlineData(HoldFn + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        let g = func [r] () -> i32 => r@follow\n        let h = H.init(g@move)\n        return 1\n    r = n@ref\n    return c()\n" + Main, DiagnosticCode.Unsupported_Kd, "g@move", null)]
    public void AComparisonWithAFunctionTypeReadsTheContractAlone(string source, DiagnosticCode code, string context, string? subject)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, subject)));

    // SPEC 15.3.6, 15.6.5 (DECISIONS: G572 U1): a region at a fixed longer end reads as its solved value, each bound outliving the target, and
    // A5 reads it, once every body is bound. The limit of O-57 for these forms ends here: (n1) a stated result, (n2) a store, (n6v) over a
    // call's result in a `var`, (n12) three bodies, (w1) a stated `let`, (w2) a stated parameter, each one bound. The same read through a
    // written result (z24b), two bounds a premise relates (a2), a meet at the position (a3), a body `var` (a5), a later bound a premise
    // relates (a13), slots as separate regions (t23); a join (n26), a stated Type argument (n27) and an Array slot's call (t21) in a nested
    // body, Binding's judge leaving the row; and own-body joins, `Unsupported_Kd` before, which take the same row: with no Closure (g1, g7,
    // g8), and over a Closure's result at a stated local (t29) and at the return (t37), O-59's. An enclosing `var` region's value reached
    // through the Closure's own `var` region, which an invariant call makes equal to another enclosing region, with that Equality row at
    // the chain's last edge (eqw, rejected before) and with a store there (eqwp; PLAN G492-a (ii), the Middle's C-350 C2 (a)).
    [Theory]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r] () -> ref/i32 during p\n        let inner = func [r] () => r\n        return inner()\n    see(outer())\n" + Main)]
    [InlineData("func f(p: ref/i32, o: uniq/(ref/i32 during p))\n    var r = p\n    var w = func [r, o] ()\n        let inner = func [r] () => r\n        o@follow = inner()\n    w()\n" + Main)]
    [InlineData(See + "func id(p: ref/i32) -> ref/i32 during p\n    return p\nfunc f(p: ref/i32)\n    var y = id(p)\n    let outer = func [y] () -> ref/i32 during p\n        let inner = func [y] () => y\n        return inner()\n    see(outer())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    let top = func [p] () -> ref/i32 during p\n        var y = p\n        let mid = func [y] () -> ref/i32 during p\n            let low = func [y] () => y\n            return low()\n        return mid()\n    see(top())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r] () -> i32\n        let inner = func [r] () => r\n        let w: ref/i32 during p = inner()\n        return w@follow\n    let n = outer()\n    see(n@ref)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r] () -> i32\n        let inner = func [r] () => r\n        let g = func (z: ref/i32 during p) -> i32 => z@follow\n        return g(inner())\n    let n = outer()\n    see(n@ref)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let c = func [r] () -> ref/i32 during p => r\n    see(c())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32)\n    origin p outlives q\n    var r = p\n    r = q\n    let c = func [r] () -> ref/i32 during q => r\n    see(c())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32)\n    var r = p\n    r = q\n    let c = func [r] () -> ref/i32 during (p and q) => r\n    see(c())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let c = func [r] () -> ref/i32 during p\n        var y = r\n        return y\n    see(c())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32)\n    origin q outlives p\n    var r = p\n    let c = func [r] () -> ref/i32 during p => r\n    r = q\n    see(c())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32) -> ref/i32 during p\n    var a: i32 = 1\n    var t = (p, p)\n    let c = func [t] () -> ref/i32 during p => t.0\n    t.1 = a@ref\n    return c()\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r, p] () -> ref/i32 during p\n        let inner = func [r] () => r\n        let v: ref/i32 during p = if true => inner() else => p\n        return v\n    see(outer())\n" + Main)]
    [InlineData(See + "func id<T>(v: T) -> T => v@move\nfunc f(p: ref/i32)\n    var r = p\n    let outer = func [r] () -> ref/i32 during p\n        let inner = func [r] () => r\n        return id<ref/i32 during p>(inner())\n    see(outer())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r, p] () -> i32\n        let inner = func [r] () => r\n        var a: Array<ref/i32 during p> = [p]\n        a.append(inner())\n        return a[1]@follow\n    let n = outer()\n    see(n@ref)\n" + Main)]
    [InlineData("func f(p: ref/i32, k: bool) -> ref/i32 during p\n    var r = p\n    let v: ref/i32 during p = if k => r else => p\n    return v\n" + Main)]
    [InlineData("func f(p: ref/i32, q: ref/i32, k: bool) -> ref/i32 during (p and q)\n    var r = p\n    var s = q\n    return if k => r else => s\n" + Main)]
    [InlineData("struct Box {o}\n    public let r: ref/i32 during o\n    public init(r: ref/i32 during o)\n        self.r = r\nfunc f(p: ref/i32, k: bool) -> ref/i32 during p\n    var r = p\n    let b: Box during p = if k => Box.init(r) else => Box.init(p)\n    return b.r\n" + Main)]
    [InlineData(See + "func f(p: ref/i32, k: bool)\n    var r = p\n    let c = func [r] () => r\n    let v: ref/i32 during p = if k => c() else => p\n    see(v)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32, k: bool) -> ref/i32 during p\n    var r = p\n    let c = func [r] () => r\n    return if k => c() else => p\n" + Main)]
    [InlineData("func same(x: uniq/(ref/i32 during a), y: uniq/(ref/i32 during a)) => ()\nfunc use(p: ref/i32) -> i32\n    var r = p\n    var s = r\n    var f = func [var r, s] () -> ()\n        var w = s\n        same(r@uniq, w@uniq)\n    f()\n    return 0\n" + Main)]
    [InlineData("func put(t: uniq/(ref/i32 during a), v: ref/i32 during a) => ()\nfunc use(p: ref/i32) -> i32\n    var r = p\n    var s = r\n    var f = func [var r, s] () -> ()\n        var w = s\n        put(r@uniq, w)\n    f()\n    return 0\n" + Main)]
    public void AFixedPositionReadsARegionsSolvedValue(string source)
        => MirTargetSide.Check(source);

    // The same rule, a bound that does not outlive the position (15.6.5, one tense): a later local (b3), a later parameter (b2), a source
    // through a reference before the literal (b5), a call's argument to a `during r` parameter (b4), a nested body with a later source (b8),
    // a loop's back edge (c1), a second capture's Origin (v7r). A result written `during s` over a `let s` (b1n, and b1-stated-var with a
    // later write of the borrowed local): the signature names s's own region (PLAN G492-a (ii)), a fixed Origin of the Closure with no
    // premise relating the capture's region to it (the owner's O-249 Q1 (a); the region's solved value is the meet of its sources,
    // DECISIONS BU-127 and R4-M184), so the result is Unknown as its `var s` twin's; b1n was accepted. b1-stated-var keeps the write's
    // Loan conflict beside it, independent (SPEC 15.6.1, 23.1.2): the Closure's retention reads r's region as its solved value, s's region
    // (PLAN G492-a (ii), N6), so the result retains the capture. eqw's twin
    // over a second parameter (eqwi): `q outlives` the other region's value is Unknown at the call.
    [Theory]
    [InlineData(See + "func f(p: ref/i32) -> ref/i32 during p\n    var a: i32 = 1\n    var r = p\n    let c = func [r] () -> ref/i32 during p => r\n    r = a@ref\n    return c()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r", null, null)]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32)\n    var r = p\n    let c = func [r] () -> ref/i32 during p => r\n    r = q\n    see(c())\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r", null, null)]
    [InlineData(See + "func f(p: ref/i32) -> ref/i32 during p\n    var a: i32 = 1\n    var r = p\n    let h = r@uniq\n    h@follow = a@ref\n    let c = func [r] () -> ref/i32 during p => r\n    return c()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r", null, null)]
    [InlineData(See + "func f(p: ref/i32) -> ref/i32 during p\n    var a: i32 = 1\n    var r = p\n    let g = func (x: ref/i32 during r) -> ref/i32 during p => x\n    return g(a@ref)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> x", "x", null, null)]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    var r = p\n    let outer = func [r] () -> ref/i32 during p\n        let inner = func [r] () -> ref/i32 during p => r\n        return inner()\n    r = q\n    return outer()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r", null, null)]
    [InlineData(See + "func t(s: ref/i32, n: i32)\n    var a: i32 = 1\n    var r = s\n    var last = s\n    var i = 0\n    while i < n\n        let c = func [r] (p: ref/i32 during s) -> ref/i32 during s => r\n        last = c(s)\n        r = a@ref\n        i += 1\n    see(last)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r", null, null)]
    [InlineData(See + "func t()\n    var b: i32 = 2\n    let s = b@ref\n    var r = s\n    let c = func [r] () -> ref/i32 during s => r\n    let v = c()\n    see(v)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r", null, null)]
    [InlineData(See + "func t()\n    var b: i32 = 2\n    let s = b@ref\n    var r = s\n    let c = func [r] () -> ref/i32 during s => r\n    let v = c()\n    b = 3\n    see(v)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r", DiagnosticCode.ComparisonLoanConflict_Kd, "b = 3")]
    [InlineData("func same(x: uniq/(ref/i32 during a), y: uniq/(ref/i32 during a)) => ()\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    var r = p\n    var s = q\n    var f = func [var r, s] () -> ()\n        var w = s\n        same(r@uniq, w@uniq)\n    f()\n    return 0\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "same(r@uniq, w@uniq)", "w@uniq", null, null)]
    public void ABoundThatDoesNotOutliveThePositionStaysRejected(string source, DiagnosticCode code, string context, string? subject, DiagnosticCode? second, string? secondContext)
        => MirTargetSide.Check(source, second is not { } other ? [(code, MirTargetSide.At(context, subject))] : [(code, MirTargetSide.At(context, subject)), (other, MirTargetSide.At(secondContext!))]);

    // PLAN G492-a (ii), N6 (design-ii.md §13.18.12; labels rev5\labels-n6.md; SPEC 15.6.4, 15.8.2, 23.1.2): a Closure callee's
    // retention reads an enclosing local region as its solved value (Binding.AddRegionMeet), so a result over the value a capture holds
    // retains the capture's Loans and the write meets them: a `var` capture whose relation holds (f4bv), its holder twin (f5holdv), no
    // Closure (n1), a capture and a result over one region (n2, a `var` one n3, one Closure level deeper n4); n2ok's use comes first.
    [Theory]
    [InlineData("f4bv", "func see(p: ref/i32) => ()\nfunc t()\n    var b: i32 = 2\n    var s = b@ref\n    var r = s\n    let c = func [r] () -> ref/i32 during s => r\n    let v = c()\n    b = 3\n    see(v)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd: b = 3")]
    [InlineData("f5holdv", "func use() -> i32\n    let x: i32 = 4\n    var r = x@ref\n    let start = r\n    var c = func [var start] (n: ref/i32 during r) -> ref/i32 during r\n        let old = start\n        start = n\n        return old\n    if x > 0\n        let y: i32 = 5\n        c(y@ref)\n    let z = c(r)\n    return z@follow\npublic func main() => ()\n", "ComparisonLoanConflict_Kd: let y: i32 = 5")] // R7-O2 A1 (O-466 (b)): start's carried candidate is r's region, so `start = n` holds; the conflict stays
    [InlineData("n1", "func see(p: ref/i32) => ()\nfunc t()\n    var b: i32 = 2\n    let s = b@ref\n    var r = s\n    let v = r\n    b = 3\n    see(v)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd: b = 3")]
    [InlineData("n2", "func see(p: ref/i32) => ()\nfunc t()\n    var b: i32 = 2\n    let s = b@ref\n    let c = func [s] () -> ref/i32 during s => s\n    let v = c()\n    b = 3\n    see(v)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd: b = 3")]
    [InlineData("n3", "func see(p: ref/i32) => ()\nfunc t()\n    var b: i32 = 2\n    var s = b@ref\n    let c = func [s] () -> ref/i32 during s => s\n    let v = c()\n    b = 3\n    see(v)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd: b = 3")]
    [InlineData("n4", "func see(p: ref/i32) => ()\nfunc t()\n    var b: i32 = 2\n    let s = b@ref\n    let c = func [s] () -> ref/i32 during s\n        let d = func [s] () -> ref/i32 during s => s\n        return d()\n    let v = c()\n    b = 3\n    see(v)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd: b = 3")]
    [InlineData("n2ok", "func see(p: ref/i32) => ()\nfunc t()\n    var b: i32 = 2\n    let s = b@ref\n    let c = func [s] () -> ref/i32 during s => s\n    let v = c()\n    see(v)\n    b = 3\npublic func main() => ()\n", "")]
    public void AClosuresRetentionReadsARegionAsItsValue(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, records);

    // SPEC 15.6.5's Closure sentence, 15.3.6 (R7-O2 A1; the owner's O-443 (a)): a Closure body that receives an enclosing local's region
    // through a capture entry reads it as its value at either end of a relation and at an inner chain node: a `let` region's published value
    // (p2v `r == p`, p8v `r == (p and q)`, p9v `p outlives r`, eq_let, lv_let, e1l `p outlives r outlives` the receiver), a contravariant
    // `let` slot's (p5v), a retention through it (p10v, ret_v2) and a `var` region's candidate by its row (lv_var, eq_var, p10v_var, a `var`
    // Pattern binding pc_eq_var). Each was rejected with BU-127's shorter-end limit (Q-542's nine, O-443 (a); the `var` rows G572 (ii)).
    [Theory]
    [InlineData("p2v", "func see(p: ref/i32) => ()\nfunc f(p: ref/i32)\n    let r = p\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin r == p\n        return w\n    let n = c()\n    see(n@ref)\npublic func main() => ()\n", "")]
    [InlineData("p5v", "struct C {s}\n    public let call: (ref/i32 during s) -> i32\n    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\nfunc f(a: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    let h = C.init(g@move)\n    let k = func [h@move, a] () -> i32 => (h.call)(a)\n    return k@move()\npublic func main() => ()\n", "")]
    [InlineData("p8v", "func see(p: ref/i32) => ()\nfunc f(p: ref/i32, q: ref/i32, k: bool)\n    let r = if k => p else => q\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin r == (p and q)\n        return w\n    let n = c()\n    see(n@ref)\npublic func main() => ()\n", "")]
    [InlineData("p9v", "func f(p: ref/i32, q: ref/i32) -> ref/i32 during q\n    origin p outlives q\n    let r = p\n    let c = func [r] (x: ref/i32 during q) -> ref/i32 during q\n        let w: i32 = 1\n            origin p outlives r\n        return x\n    return c(q)\npublic func main() => ()\n", "")]
    [InlineData("p10v", "func f(z: uniq/i32, x: ref/i32)\n    origin z outlives x\n    let first = x\n    var remember = func [var first] (value: ref/i32 during x) => first = value\n    remember(z@follow@ref)\n    remember(x)\npublic func main() => ()\n", "")]
    [InlineData("ret_v2", "public func main() => ()\nfunc f(z: uniq/i32, x: ref/i32)\n    origin z outlives x\n    let first = x\n    let second = first\n    var remember = func [var second] (value: ref/i32 during x) => second = value\n    remember(z@follow@ref)\n    remember(x)\n", "")]
    [InlineData("eq_let", "public func main() => ()\nfunc same(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o)) => ()\nfunc f(p: ref/i32, h: uniq/(ref/i32 during p))\n    let r = p\n    var c = func [var r, h] ()\n        same(h, r@uniq)\n    c()\n", "")]
    [InlineData("lv_let", "public func main() => ()\nfunc see(p: ref/i32) => ()\nfunc f(p: ref/i32)\n    let r = p\n    var c = func [var r, p] ()\n        r = p\n        see(r)\n    c()\n", "")]
    [InlineData("e1l", "public func main() => ()\nfunc same(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o)) => ()\nfunc f(p: ref/i32, q: ref/i32)\n    let r = p\n    var s = p\n    var outer = func [var r] (h: uniq/(ref/i32 during p))\n        same(h, r@uniq)\n    outer(s@uniq)\n", "")]
    [InlineData("lv_var", "public func main() => ()\nfunc see(p: ref/i32) => ()\nfunc f(p: ref/i32)\n    var r = p\n    var c = func [var r, p] ()\n        r = p\n        see(r)\n    c()\n", "")]
    [InlineData("eq_var", "public func main() => ()\nfunc same(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o)) => ()\nfunc f(p: ref/i32, h: uniq/(ref/i32 during p))\n    var r = p\n    var c = func [var r, h] ()\n        same(h, r@uniq)\n    c()\n", "")]
    [InlineData("p10v_var", "public func main() => ()\nfunc f(z: uniq/i32, x: ref/i32)\n    origin z outlives x\n    var first = x\n    var remember = func [var first] (value: ref/i32 during x) => first = value\n    remember(z@follow@ref)\n    remember(x)\n", "")]
    [InlineData("pc_eq_var", "public func main() => ()\nfunc f(p: ref/i32, o: Option<ref/i32 during p>) -> i32\n    match o@move\n        .Some(var r)\n            let c = func [r] () -> i32\n                let w: i32 = 1\n                    origin r == p\n                return w\n            return c()\n        .None => return 0\n", "")]
    public void ACarriedRegionReadsAsItsValueAtEitherEnd(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, records);

    // SPEC 15.6.1 (DECISIONS: G572 U1): a body's clause is an obligation, so one over an enclosing region is A5's row, judged at the clause: a
    // later bound that does not hold (c1i), in a meet (e3), two bodies below the region (e6), through a slot (e2).
    [Theory]
    [InlineData(ClauseOver + "        let w: i32 = 1\n            origin r outlives p\n" + ClauseEnd, "origin r outlives p")]
    [InlineData(ClauseOver + "        let w: i32 = 1\n            origin (r and p) outlives p\n" + ClauseEnd, "origin (r and p) outlives p")]
    [InlineData(See + "func f(p: ref/i32)\n    var q: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        let d = func [r] () -> i32\n            let w: i32 = 1\n                origin r outlives p\n            return w\n        return d()\n    r = q@ref\n    let n = c()\n    see(n@ref)\n" + Main, "origin r outlives p")]
    [InlineData(See + "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\nfunc f(p: ref/i32)\n    var q: i32 = 1\n    var r = p\n    let c = func [r, p] () -> i32\n        let h0 = H.init(p)\n        let h: H{x} = h0@move\n            origin r outlives x.s\n            origin x.s == p\n        return h.item@follow\n    r = q@ref\n    let n = c()\n    see(n@ref)\n" + Main, "origin r outlives x.s")]
    public void AClauseOverAnEnclosingRegionIsARow(string source, string clause)
        => MirTargetSide.Check(source, (DiagnosticCode.UnprovenOriginRelation_Kd, MirTargetSide.At(clause, null)));

    // SPEC 15.6.5's Closure sentence, 15.3.6 (R7-O2 A1; was G572's recorded limit, BU-127): a region at the shorter end of a direction of a
    // relation the body checks (of `==` or `outlives`) reads as its value where the body carries it. R's one bound p makes R outlive p and
    // p outlive R: a stated local through `uniq` (i1), a call's equal Origins in either order (e1, also q5; i3), a clause `r == p` (c5i,
    // also a4v) and `s == r` (v3), the laws `r == (r and p)` (l2v) and `(r and p) outlives r` (l4v), and the Closure writing p into its
    // `[var r]` (z3v). Each was rejected with UnprovenOriginRelation_Kd before.
    [Theory]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    var outer = func [var r] ()\n        let u = r@uniq\n        let h: uniq/(ref/i32 during p) = u\n        see(h@follow)\n    outer()\n" + Main)]
    [InlineData("func same(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o))\n    ()\nfunc f(p: ref/i32)\n    var r = p\n    var s = p\n    var outer = func [var r] (h: uniq/(ref/i32 during p))\n        same(h, r@uniq)\n    outer(s@uniq)\n" + Main)]
    [InlineData("func same(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o))\n    ()\nfunc f(p: ref/i32)\n    var r = p\n    var s = p\n    var outer = func [var r] (h: uniq/(ref/i32 during p))\n        same(r@uniq, h)\n    outer(s@uniq)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin r == p\n        return w\n    let n = c()\n    see(n@ref)\n" + Main)]
    [InlineData(See + "func f(s: ref/i32)\n    var r = s\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin s == r\n        return w\n    let n = c()\n    see(n@ref)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin r == (r and p)\n        return w\n    let n = c()\n    see(n@ref)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var a: i32 = 3\n    var r = p\n    r = a@ref\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin (r and p) outlives r\n        return w\n    let n = c()\n    see(n@ref)\n" + Main)]
    [InlineData("func put(h: uniq/(ref/i32 during o), v: ref/i32 during o)\n    h@follow = v\nfunc f(p: ref/i32) -> ref/i32 during p\n    var r = p\n    var c = func [var r, p] () -> ref/i32 during p\n        put(r@uniq, p)\n        return r\n    return c()\n" + Main)]
    public void ARegionAtTheShorterEndReadsAsItsValue(string source)
        => MirTargetSide.Check(source);

    // SPEC 15.6.5 ("`a == b` is edges both ways"; DECISIONS: G572 U1): with the region second of `==`, the backward direction has it at the
    // longer end, so it is a row too and Binding's judge never takes it as holding. R's bound p: a clause `static == r` (q1), an explicit Type
    // argument naming R at `uniq` with a `static` holder (q14, base's record at the argument), a meet first (w6); R's bounds `static` and p
    // (v1i), `static` and a local Borrow (v2i).
    [Theory]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin static == r\n        return w\n    let n = c()\n    see(n@ref)\n" + Main, "origin static == r", null)]
    [InlineData(See + "func id<T>(v: T) -> T => v@move\nfunc f(p: ref/i32)\n    var r = p\n    let outer = func (h0: uniq/(ref/i32 during static))\n        let h = id<uniq/(ref/i32 during r)>(h0)\n        see(h@follow)\n    see(r)\n" + Main, "id<uniq/(ref/i32 during r)>(h0)", "h0")]
    [InlineData(See + "func f(p: ref/i32)\n    var a: i32 = 1\n    var r = a@ref\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin (static and p) == r\n        return w\n    let n = c()\n    see(n@ref)\n" + Main, "origin (static and p) == r", null)]
    [InlineData(See + "func f(s: ref/i32 during static, p: ref/i32)\n    var r = s\n    r = p\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin static == r\n        return w\n    let n = c()\n    see(n@ref)\n" + Main, "origin static == r", null)]
    [InlineData("func id<T>(v: T) -> T => v@move\nfunc f(x: uniq/(ref/i32 during static), s: ref/i32 during static)\n    var a: i32 = 7\n    var r = s\n    r = a@ref\n    let outer = func [r] (h0: uniq/(ref/i32 during static))\n        let h = id<uniq/(ref/i32 during r)>(h0)\n        h@follow = r\n    outer(x)\n" + Main, "id<uniq/(ref/i32 during r)>(h0)", "h0")]
    public void AnEqualityWithTheRegionSecondIsA5s(string source, string context, string? subject)
        => MirTargetSide.Check(source, (DiagnosticCode.UnprovenOriginRelation_Kd, MirTargetSide.At(context, subject)));

    // Their valid twins: R's only bound is `static`, so both directions hold, at a clause (v1) and at a Type argument with the store (v2).
    [Theory]
    [InlineData(See + "func f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin static == r\n        return w\n    let n = c()\n    see(n@ref)\n" + Main)]
    [InlineData("func id<T>(v: T) -> T => v@move\nfunc f(x: uniq/(ref/i32 during static), s: ref/i32 during static)\n    var r = s\n    let outer = func [r] (h0: uniq/(ref/i32 during static))\n        let h = id<uniq/(ref/i32 during r)>(h0)\n        h@follow = r\n    outer(x)\n" + Main)]
    public void AnEqualityWithTheRegionSecondHoldsByItsBound(string source)
        => MirTargetSide.Check(source);

    // The twin of c1i: the region's one bound holds, and the row is judged, not skipped.
    [Fact]
    public void AClauseAnEnclosingRegionMeetsHolds()
        => MirTargetSide.Check(See + "func f(p: ref/i32)\n    var r = p\n    let c = func [r] () -> i32\n        let w: i32 = 1\n            origin r outlives p\n        return w\n    let n = c()\n    see(n@ref)\n" + Main);

    // SPEC 15.2.3, 15.3.6, 7.7.4 (the owner's O-481 (b), verbatim 「O-481 は (b) でよいです」; O-471 (a); R7-O2 A1+A2): an erasure's Owned
    // condition reads a local `var` region as its candidate once its body is bound, its own body's region (A2's half) or one the body
    // carries (O-443 (a), O-466 (b)), in turn: an Owned proof that reads a region with no answer yet while a body is bound is the erasure's
    // obligation, judged in Binding's Final prologue (JudgeErasedOwned). Valid, each `static` through R's one bound: the erasure in the
    // region's own body of a literal (s2: through y's `let`; eo0v, eol0v: a later `r = p`, `b = p`) or of a named Closure value (e6v, s2h,
    // rv1v: its result during R itself; dev's O-61 (a) reading superseded for the Owned condition); in a nested body that carries R (c11,
    // c11own0, s2own0), at a `return` (rt1v) and at a call's argument (ag1v: a Closure literal bound at its destination, judged at the
    // literal, not by the applicability row that judges a named value's erasure, s2c). An object view's payload erasure reads the same
    // (obj1v, SPEC 15.8.1: RequireObjectErasure takes the same obligation). Each was rejected with UnprovenConstraint_Kd before.
    [Theory]
    [InlineData("s2", See + "func f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    var g: () -> ref/i32 during static = func [y] () => y\n    see(g())\n" + Main)]
    [InlineData("e6v", See + "func t(x: ref/i32 during static)\n    var s = x\n    let c = func [s] () => s\n    let f: () -> ref/i32 during static = c@move\n    see(f())\n" + Main)]
    [InlineData("s2h", See + "func f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    let g = func [y] () => y\n    var h: () -> ref/i32 during static = g@move\n    see(h())\n" + Main)]
    [InlineData("eo0v", "func keepStatic(x: ref/i32 during static) -> i32 => x@follow\nfunc needOwned<T>(x: T) -> i32\n    T is Owned\n    return 0\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let g: () -> i32 = func [r] () -> i32 => r@follow\n    r = p\n    return g()\n")]
    [InlineData("eol0v", "func keepStatic(x: ref/i32 during static) -> i32 => x@follow\nfunc needOwned<T>(x: T) -> i32\n    T is Owned\n    return 0\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var b = p\n    let r = b\n    let g: () -> i32 = func [r] () -> i32 => r@follow\n    b = p\n    return g()\n")]
    [InlineData("ag1v", "func takeFn(g: () -> i32) -> i32 => g()\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        return takeFn(func [r] () -> i32 => r@follow)\n    r = p\n    return c()\n")]
    [InlineData("obj1v", "open struct Base\n    protected init() => ()\nstruct Leaf<T>: Base\n    public init(): base() => ()\npublic func main() => ()\nfunc f(s: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = s\n    let h = Kimi.Intrinsics.makeObj(Leaf<ref/i32 during r>.init())\n    let v = h@objref/Base\n    r = s\n    return 0\n")]
    [InlineData("rt1v", "func takeFn(g: () -> i32) -> i32 => g()\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> () -> i32\n        return func [r] () -> i32 => r@follow\n    r = p\n    return c()()\n")]
    [InlineData("c11", See + "func f(p: ref/i32 during static)\n    var r = p\n    let outer = func [r] ()\n        let inner = func [r] () => r\n        let g: () -> ref/i32 during static = inner@move\n        see(g())\n    outer()\n" + Main)]
    [InlineData("c11own0", "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let outer = func [r] () -> i32\n        let inner = func [r] () -> i32 => r@follow\n        let g: () -> i32 = inner@move\n        return g()\n    return outer()\n" + Main)]
    [InlineData("s2own0", "func f(s: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    var g: () -> i32 = func [y] () => y@follow\n    return g()\n" + Main)]
    [InlineData("rv1v", "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> ref/i32 during r => r\n    let g: () -> ref/i32 during r = c@move\n    r = p\n    return g()@follow\n" + Main)]
    public void AnErasuresOwnedReadsAVarRegionsCandidateOnceItsBodyIsBound(string form, string program)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, string.Empty);

    // SPEC 15.3.6 (a candidate over all of the variable's bounds), 15.2.3, 15.6.5 (R7-O2 A1+A2; the owner's O-471 (a), O-481 (b)): a `var`
    // region's candidate has an answer only once its body is bound (RegionValue's gate, RegionBounded), so an erasure's Owned proof that
    // reads it while a body is bound is judged at Final (JudgeErasedOwned). A bound recorded after the literal (`r = n@ref`; through
    // `var r = b` in c11b; `b = n@ref` through `let r = b` in eol0n) is in the candidate: rejected. In the region's own body, where `n@ref` is a
    // Borrow local to the body, the chain to `static` is Refuted (SPEC 15.6.5): UnsatisfiedConstraint_Kd (s2own, s2late, eo0n, eol0n, rv1n;
    // dev's Unknown was judged before the bound existed, a record-kind move under Q13); in a nested body (c11own, c11late, c11b, rt1n)
    // `n@ref` is an enclosing Borrow, fixed there: Unknown, UnprovenConstraint_Kd. A relation's record stays beside it (c11late, s2late,
    // c11b). Their valid twins: AnErasuresOwnedReadsAVarRegionsCandidateOnceItsBodyIsBound.
    [Theory]
    [InlineData("c11own", "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let outer = func [r] () -> i32\n        let inner = func [r] () -> i32 => r@follow\n        let g: () -> i32 = inner@move\n        return g()\n    r = n@ref\n    return outer()\n" + Main, "UnprovenConstraint_Kd: let g: () -> i32 = inner@move")]
    [InlineData("s2own", "func f(s: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    var g: () -> i32 = func [y] () => y@follow\n    r = n@ref\n    return g()\n" + Main, "UnsatisfiedConstraint_Kd: var g: () -> i32 = func [y] () => y@follow")]
    [InlineData("c11late", See + "func f(p: ref/i32 during static)\n    let n: i32 = 1\n    var r = p\n    let outer = func [r] ()\n        let inner = func [r] () => r\n        let g: () -> ref/i32 during static = inner@move\n        see(g())\n    r = n@ref\n    outer()\n" + Main, "UnprovenConstraint_Kd: let g: () -> ref/i32 during static = inner@move | UnprovenOriginRelation_Kd: let g: () -> ref/i32 during static = inner@move")]
    [InlineData("s2late", See + "func f(s: ref/i32 during static)\n    let n: i32 = 1\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    var g: () -> ref/i32 during static = func [y] () => y\n    r = n@ref\n    see(g())\n" + Main, "UnsatisfiedConstraint_Kd: var g: () -> ref/i32 during static = func [y] () => y | UnprovenOriginRelation_Kd: var g: () -> ref/i32 during static = func [y] () => y")]
    [InlineData("c11b", See + "func f(p: ref/i32 during static)\n    let n: i32 = 1\n    var b = p\n    var r = b\n    let outer = func [r] ()\n        let inner = func [r] () => r\n        let g: () -> ref/i32 during static = inner@move\n        see(g())\n    b = n@ref\n    outer()\n" + Main, "UnprovenConstraint_Kd: let g: () -> ref/i32 during static = inner@move | UnprovenOriginRelation_Kd: let g: () -> ref/i32 during static = inner@move")]
    [InlineData("eo0n", "func keepStatic(x: ref/i32 during static) -> i32 => x@follow\nfunc needOwned<T>(x: T) -> i32\n    T is Owned\n    return 0\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let g: () -> i32 = func [r] () -> i32 => r@follow\n    r = n@ref\n    return g()\n", "UnsatisfiedConstraint_Kd: let g: () -> i32 = func [r] () -> i32 => r@follow")]
    [InlineData("eol0n", "func keepStatic(x: ref/i32 during static) -> i32 => x@follow\nfunc needOwned<T>(x: T) -> i32\n    T is Owned\n    return 0\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var b = p\n    let r = b\n    let g: () -> i32 = func [r] () -> i32 => r@follow\n    b = n@ref\n    return g()\n", "UnsatisfiedConstraint_Kd: let g: () -> i32 = func [r] () -> i32 => r@follow")]
    [InlineData("ag1n", "func takeFn(g: () -> i32) -> i32 => g()\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        return takeFn(func [r] () -> i32 => r@follow)\n    r = n@ref\n    return c()\n", "UnprovenConstraint_Kd: return takeFn(func [r] () -> i32 => r@follow)")]
    [InlineData("obj1n", "open struct Base\n    protected init() => ()\nstruct Leaf<T>: Base\n    public init(): base() => ()\npublic func main() => ()\nfunc f(s: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = s\n    let h = Kimi.Intrinsics.makeObj(Leaf<ref/i32 during r>.init())\n    let v = h@objref/Base\n    r = n@ref\n    return 0\n", "UnprovenConstraint_Kd: let v = h@objref/Base")]
    [InlineData("rt1n", "func takeFn(g: () -> i32) -> i32 => g()\npublic func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> () -> i32\n        return func [r] () -> i32 => r@follow\n    r = n@ref\n    return c()()\n", "UnprovenConstraint_Kd: return func [r] () -> i32 => r@follow")]
    [InlineData("rv1n", "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    var r = p\n    let c = func [r] () -> ref/i32 during r => r\n    let g: () -> ref/i32 during r = c@move\n    r = n@ref\n    return g()@follow\n" + Main, "UnsatisfiedConstraint_Kd: let g: () -> ref/i32 during r = c@move")]
    public void ACarriedVarRegionsCandidateIsReadOnceItsBodyIsBound(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, records);

    // SPEC 15.4.3 rule 3, 15.6.5, 7.7.2 (R7-O2 O2-H; the owner's O-443 (a), O-487 (a)): a Closure's header reads an enclosing body's region
    // as its value exactly when the Closure carries it, as its body does. A written list's entries name their regions before any binding
    // of the header (Binding.ClosureHeaderScope): at the commitment (ann1l, lc6v with a later `let`; hx1v through `[r@ref]`), the header
    // probe (h6v) and the call-signature evidence (hxg1v); and while a body is bound a region is carried in turn through the enclosing
    // Closures' own entries, reading no candidate (AdmittedEntry: lc5v, h1v; forwarded by an enclosing omitted list, h5v; one whose body
    // captured r first, hw1v). Each `let` region's value is `static`. Each was rejected with MissingOriginBinding_Kd at the header before
    // (the header bound before the entries recorded their regions); dev accepted them. Their invalid twins:
    // AClosuresHeaderOverARegionItDoesNotCarryIsAFixedOrigin.
    [Theory]
    [InlineData("ann1l", HeaderBox + HeaderLet + "    let c = func [r] " + HeaderOf + "\n    return 0\n")]
    [InlineData("lc6v", HeaderBox + HeaderLet + "    let c = func [r] " + HeaderOf + "\n    let q = 1\n    return q\n")]
    [InlineData("hx1v", HeaderBox + HeaderLet + "    let c = func [r@ref] " + HeaderOf + "\n    return 0\n")]
    [InlineData("h6v", HeaderBox + HeaderLet + "    let c: (Option<ref/i32 during r>) -> Box = func [r] (k) -> Box => $abort(\"no\")\n    return 0\n")]
    [InlineData("hxg1v", HeaderBox + TakeG + HeaderLet + "    return takeG(func [r] " + HeaderOf + ")\n")]
    [InlineData("lc5v", HeaderBox + HeaderLet + "    let c = func [r] () -> i32\n        let d = func [r] " + HeaderOf + "\n        return 1\n    return c()\n")]
    [InlineData("h1v", HeaderBox + HeaderLet + "    let c = func [r] () -> i32\n        let d = func [r] () -> i32\n            let e = func [r] " + HeaderOf + "\n            return 1\n        return d()\n    return c()\n")]
    [InlineData("h5v", HeaderBox + HeaderLet + "    let c = func () -> i32\n        let d = func [r] " + HeaderOf + "\n        return 1\n    return c()\n")]
    [InlineData("hw1v", HeaderBox + HeaderLet + "    let c = func () -> i32\n        let z = r\n        let d = func [r] " + HeaderOf + "\n        return 1\n    return c()\n")]
    public void AClosuresHeaderReadsARegionItCarriesAsItsValue(string form, string program)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, string.Empty);

    // SPEC 15.6.5's last clause, 15.3.4 (D-862; R7-O2 O2-H): a region a Closure's header names but the Closure does not carry is a fixed
    // Origin with no premise, so the header result's default has no proof: the entry names another binding (h0n; at a generic argument,
    // hxg1n, whose lone candidate applies, the default judged once f's body is bound: R7-O2 O2-F), a chain broken by an inner list (h2n),
    // an omitted list whose body uses nothing (lc7v). A carried region whose value is a body-local Borrow (h1n) is read as it, which does
    // not outlive `static`.
    [Theory]
    [InlineData("h0n", HeaderBox + HeaderLet + "    let q: i32 = 1\n    let c = func [q] " + HeaderOf + "\n    return 0\n", "MissingOriginBinding_Kd: let c = func [q] " + HeaderOf)]
    [InlineData("hxg1n", HeaderBox + TakeG + HeaderLet + "    let q: i32 = 1\n    return takeG(func [q] " + HeaderOf + ")\n", "MissingOriginBinding_Kd: return takeG(func [q] " + HeaderOf + ")")]
    [InlineData("h2n", HeaderBox + HeaderLet + "    let c = func [r] () -> i32\n        let q: i32 = 1\n        let d = func [q] " + HeaderOf + "\n        return 1\n    return c()\n", "MissingOriginBinding_Kd: let d = func [q] " + HeaderOf)]
    [InlineData("lc7v", HeaderBox + HeaderLet + "    let c = func " + HeaderOf + "\n    return 0\n", "MissingOriginBinding_Kd: let c = func " + HeaderOf)]
    [InlineData("h1n", HeaderBox + Main + "func f(p: ref/i32 during static) -> i32\n    let n: i32 = 1\n    let r = n@ref\n    let c = func [r] () -> i32\n        let d = func [r] () -> i32\n            let e = func [r] " + HeaderOf + "\n            return 1\n        return d()\n    return c()\n", "MissingOriginBinding_Kd: let e = func [r] " + HeaderOf)]
    public void AClosuresHeaderOverARegionItDoesNotCarryIsAFixedOrigin(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, records);

    // SPEC 15.4.3 rule 3, 15.4.4, 8.3.3 (R7-O2 O2-F): a header result's default needs its inputs provably Owned; where that proof reads
    // a fact complete only once the body is bound, the default is judged under Finality, the slot naming its own atom until then. An
    // omitted list's own capture of r (h3v; with an erasure of c, f6v; over a `var` r, f4v), a `var` r's candidate `static` (ann1v; a body
    // returning r, f2v; forwarded by an enclosing omitted list, h5vv) and a body-local annotation's Function Type over a `var` r (fty1v)
    // are valid; each was rejected with MissingOriginBinding_Kd at the slot before (fty1v also UnresolvedBinding_Kd at the literal). So
    // are the readers of the atom, which take its completion `static`: a call of c (f7v), its result where `T is Owned` is checked
    // (own1v), at a `during static` parameter (take1v), returned (ret1v), held by a `var` (vb1v), in a nested body (nst1v), a call of g
    // (fty3v), an erasure to g whose written result is p's (fsh1v, fsh2v), a body returning a carried value (bod2v), a second default over
    // a value read from c's result (def3v), a condition with take's universal at its shorter end (fda3v) and the meet of a conditional
    // direct-borrow input's borrow case (pm3v).
    [Theory]
    [InlineData("h3v", HeaderBox + HeaderLet + "    let c = func " + HeaderInit + "\n    return 0\n")]
    [InlineData("f6v", HeaderBox + HeaderLet + "    let c = func " + HeaderInit + "\n    let g: (Option<ref/i32 during r>) -> Box = c@move\n    return 0\n")]
    [InlineData("f4v", HeaderBox + HeaderVar + "    let c = func " + HeaderInit + "\n    r = p\n    return 0\n")]
    [InlineData("ann1v", HeaderBox + HeaderVar + "    let c = func [r] " + HeaderOf + "\n    r = p\n    return 0\n")]
    [InlineData("f2v", HeaderBox + HeaderVar + "    let c = func [r] " + HeaderInit + "\n    r = p\n    return 0\n")]
    [InlineData("h5vv", HeaderBox + HeaderVar + "    let c = func () -> i32\n        let d = func [r] " + HeaderOf + "\n        return 1\n    return c()\n")]
    [InlineData("fty1v", HeaderBox + HeaderVar + AnnotatedG + "func [r] (k) -> Box => $abort(\"no\")\n    r = p\n    return 0\n")]
    [InlineData("f7v", HeaderBox + HeaderVar + "    let c = func " + HeaderInit + "\n    r = p\n    let b = c(.None)\n    return 0\n")]
    [InlineData("own1v", HeaderBox + "func need<T>(x: T) -> i32\n    T is Owned\n    return 1\n" + HeaderVar + "    let c = func " + HeaderInit + "\n    r = p\n    return need(c(.None))\n")]
    [InlineData("take1v", HeaderBox + "func take(b: Box during static) -> i32 => 0\n" + HeaderVar + "    let c = func " + HeaderInit + "\n    r = p\n    return take(c(.None))\n")]
    [InlineData("ret1v", HeaderBox + Main + "func f(p: ref/i32 during static) -> Box\n    var r = p\n    let c = func " + HeaderInit + "\n    r = p\n    return c(.None)\n")]
    [InlineData("vb1v", HeaderBox + HeaderVar + "    let c = func " + HeaderInit + "\n    r = p\n    var b = c(.None)\n    b = Box.init(p)\n    return 0\n")]
    [InlineData("nst1v", HeaderBox + HeaderVar + "    let c = func [r] () -> i32\n        let d = func " + HeaderInit + "\n        let b = d(.None)\n        return 1\n    r = p\n    return c()\n")]
    [InlineData("fty3v", HeaderBox + HeaderVar + AnnotatedG + "func [r] (k) -> Box => $abort(\"no\")\n    r = p\n    let b = g(.None)\n    return 0\n")]
    [InlineData("fsh1v", HeaderBox + HeaderS + "    var r = p\n" + AnnotatedG + "func [p] (k) -> Box during p => Box.init(p)\n    r = p\n    return 0\n")]
    [InlineData("fsh2v", HeaderBox + HeaderS + "    var r = p\n" + AnnotatedG + "func (k) -> Box during p => $abort(\"no\")\n    r = p\n    return 0\n")]
    [InlineData("bod2v", HeaderBox + HeaderS + "    var r = p\n    let c = func [r, p] (k: Option<ref/i32 during r>) -> Box => Box.init(p)\n    r = p\n    return 0\n")]
    [InlineData("def3v", HeaderBox + HeaderVar + "    let c = func " + HeaderInit + "\n    r = p\n    let v = c(.None).v\n    let e = func [v] (q: Option<ref/i32 during v>) -> Box => Box.init(v)\n    return 0\n")]
    [InlineData("fda3v", HeaderBox + TakeO + HeaderVar + "    let t = take(func [r] " + HeaderOf + ", p)\n    r = p\n    return t\n")]
    [InlineData("pm3v", HeaderBox + PairG + "    let c = func [r] " + PairOf + "\n    r = p\n    return 0\n")]
    public void AClosuresHeaderDefaultIsJudgedOnceItsBodyIsBound(string form, string program)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, string.Empty);

    // SPEC 15.4.4, 23.1.2 (R7-O2 O2-F): a deferred default refuted at Final is the slot's MissingOriginBinding_Kd; its atom stays Unbound
    // resting on the failed slot, so the records naming it (the body's `Box.init(r)`, a call of c) rest on it: ann1n, f1n, f3n, f5n, f7n,
    // f8n. A literal a candidate trial reads and nothing commits keeps that trial's default, judged at Final beside the call's record
    // (hxg3n; hxg2n over a region it does not carry). An annotation's slot and the literal's own slot fail apart, and the erasure to the
    // annotated Type naming the failed atom rests, as does a call of g (fty1n, fty3n: two records on one line). The slot's owner is its
    // declaration, never an enclosing Closure: c's use keeps its record (fty2n). A body's record independent of the slot stays (fz1n),
    // and a deferred slot proven at Final records nothing (fz3n; MissingOriginBinding_Kd before). A literal whose omitted list carries no
    // region keeps r a fixed Origin (lc7v's class), at a call argument whose result condition then rests (fda2n, fda2v: the call's
    // NoApplicableOverload_Kd beside it before) and with a conditional direct-borrow input (pm2n, pm2v).
    [Theory]
    [InlineData("ann1n", HeaderBox + HeaderVar + "    let c = func [r] " + HeaderOf + "\n    r = n@ref\n    return 0\n", "MissingOriginBinding_Kd: let c = func [r] " + HeaderOf)]
    [InlineData("f1n", HeaderBox + HeaderBorrow + "    let c = func " + HeaderInit + "\n    return 0\n", "MissingOriginBinding_Kd: let c = func " + HeaderInit)]
    [InlineData("f3n", HeaderBox + HeaderVar + "    let c = func [r] " + HeaderInit + "\n    r = n@ref\n    return 0\n", "MissingOriginBinding_Kd: let c = func [r] " + HeaderInit)]
    [InlineData("f5n", HeaderBox + HeaderVar + "    let c = func " + HeaderInit + "\n    r = n@ref\n    return 0\n", "MissingOriginBinding_Kd: let c = func " + HeaderInit)]
    [InlineData("f7n", HeaderBox + HeaderBorrow + "    let c = func " + HeaderInit + "\n    let b = c(.None)\n    return 0\n", "MissingOriginBinding_Kd: let c = func " + HeaderInit)]
    [InlineData("f8n", HeaderBox + HeaderVar + "    let c = func " + HeaderInit + "\n    r = n@ref\n    let b = c(.None)\n    return 0\n", "MissingOriginBinding_Kd: let c = func " + HeaderInit)]
    [InlineData("hxg3n", HeaderBox + TakeG2 + HeaderBorrow + "    return takeG2(func " + HeaderInit + ", true)\n", "NoApplicableOverload_Kd: return takeG2(func " + HeaderInit + ", true) | MissingOriginBinding_Kd: return takeG2(func " + HeaderInit + ", true)")]
    [InlineData("hxg2n", HeaderBox + TakeG2 + HeaderLet + "    let q: i32 = 1\n    return takeG2(func [q] " + HeaderOf + ", true)\n", "NoApplicableOverload_Kd: return takeG2(func [q] " + HeaderOf + ", true) | MissingOriginBinding_Kd: return takeG2(func [q] " + HeaderOf + ", true)")]
    [InlineData("fty1n", HeaderBox + HeaderVar + AnnotatedG + "func [r] (k) -> Box => $abort(\"no\")\n    r = n@ref\n    return 0\n", "MissingOriginBinding_Kd: let g: (Option<ref/i32 during r>) -> Box = func [r] (k) -> Box => $abort(\"no\")")]
    [InlineData("fty3n", HeaderBox + HeaderVar + AnnotatedG + "func [r] (k) -> Box => $abort(\"no\")\n    r = n@ref\n    let b = g(.None)\n    return 0\n", "MissingOriginBinding_Kd: let g: (Option<ref/i32 during r>) -> Box = func [r] (k) -> Box => $abort(\"no\")")]
    [InlineData("fty2n", HeaderBox + HeaderVar + "    let c = func [r] () -> i32\n        let g: (Option<ref/i32 during r>) -> Box = $abort(\"no\")\n        return 1\n    let x: bool = c()\n    r = n@ref\n    return 0\n", "MissingOriginBinding_Kd: let g: (Option<ref/i32 during r>) -> Box = $abort(\"no\") | TypeMismatch_Kd: let x: bool = c()")]
    [InlineData("fz1n", HeaderBox + HeaderBorrow + "    let c = func (k: Option<ref/i32 during r>) -> Box\n        let z: bool = 1\n        return Box.init(r)\n    return 0\n", "MissingOriginBinding_Kd: let c = func (k: Option<ref/i32 during r>) -> Box | TypeMismatch_Kd: let z: bool = 1")]
    [InlineData("fz3n", HeaderBox + HeaderVar + "    let c = func (k: Option<ref/i32 during r>) -> Box\n        let z: bool = 1\n        return Box.init(r)\n    r = p\n    return 0\n", "TypeMismatch_Kd: let z: bool = 1")]
    [InlineData("fda2n", HeaderBox + TakeO + HeaderVar + "    let t = take(func " + HeaderOf + ", p)\n    r = n@ref\n    return t\n", "MissingOriginBinding_Kd: let t = take(func " + HeaderOf + ", p)")]
    [InlineData("fda2v", HeaderBox + TakeO + HeaderVar + "    let t = take(func " + HeaderOf + ", p)\n    r = p\n    return t\n", "MissingOriginBinding_Kd: let t = take(func " + HeaderOf + ", p)")]
    [InlineData("pm2n", HeaderBox + PairG + "    let c = func " + PairOf + "\n    r = n@ref\n    return 0\n", "MissingOriginBinding_Kd: let c = func " + PairOf)]
    [InlineData("pm2v", HeaderBox + PairG + "    let c = func " + PairOf + "\n    r = p\n    return 0\n", "MissingOriginBinding_Kd: let c = func " + PairOf)]
    public void AClosuresDeferredHeaderDefaultRefutedAtFinalIsTheSlotsRecord(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, records);

    // SPEC 15.4.4, 15.3.7, 7.7.4 (R7-O2 O2-F): a deferred default proven at Final is `static`, and the conditions that named its atom are
    // judged against it then: an erasure of a literal whose written result is a caller's borrow s to g's annotated Type over the `var` r
    // needs `s outlives static`, beside the environment's Owned where the literal captures s (fsh1n) and alone where it captures nothing
    // (fsh2n; A5's row, where an undeferred target gives the comparison's UnprovenOriginContract_Kd: PLAN's open issue on one condition's
    // two kinds); and a literal whose slot is proven `static` cannot return s (bod2n).
    [Theory]
    [InlineData("fsh1n", HeaderBox + HeaderS + "    let n: i32 = 1\n    var r = p\n" + AnnotatedG + "func [s] (k) -> Box during s => Box.init(s)\n    r = p\n    return 0\n", "UnprovenConstraint_Kd: let g: (Option<ref/i32 during r>) -> Box = func [s] (k) -> Box during s => Box.init(s) | UnprovenOriginRelation_Kd: let g: (Option<ref/i32 during r>) -> Box = func [s] (k) -> Box during s => Box.init(s)")]
    [InlineData("fsh2n", HeaderBox + HeaderS + "    let n: i32 = 1\n    var r = p\n" + AnnotatedG + "func (k) -> Box during s => $abort(\"no\")\n    r = p\n    return 0\n", "UnprovenOriginRelation_Kd: let g: (Option<ref/i32 during r>) -> Box = func (k) -> Box during s => $abort(\"no\")")]
    [InlineData("bod2n", HeaderBox + HeaderS + "    var r = p\n    let c = func [r, s] (k: Option<ref/i32 during r>) -> Box => Box.init(s)\n    r = p\n    return 0\n", "UnprovenOriginRelation_Kd: let c = func [r, s] (k: Option<ref/i32 during r>) -> Box => Box.init(s)")]
    public void AClosuresDeferredHeaderDefaultProvenAtFinalJudgesItsReaders(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program, records);

    // R7-O2 O2-F (design §10.6): the result default reads the slot's row once per resolution and writes it only where a proof defers, so
    // a warm rebind of a header proven at its resolution (ann1l) allocates nothing.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfAProvenHeaderDefaultAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(HeaderBox + HeaderLet + "    let c = func [r] " + HeaderOf + "\n    return 0\n");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // SPEC 15.3.6, 15.6.5, 15.8.2 (DECISIONS: G570): an inferred result names an enclosing region itself, as an annotated one does, so what
    // the region takes after the literal stays in it. A later call's argument to a `during r` parameter, captured or not: the Loan (u1, u1a)
    // and the chain (u2, u2c). A store in a `[var r]` binding (u3). A source below a literal in a loop (z50) or a `defer` (z65). The same
    // chain with the literal before the loop (u16i), since relations are judged with neither flow nor liveness (the Front's C-108).
    [Theory]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var r = a@ref\n    let c = func (p: ref/i32 during r) => p\n    let v = c(b@ref)\n    b = 3\n    see(v)\n" + Main, DiagnosticCode.ComparisonLoanConflict_Kd, "b = 3", null)]
    [InlineData(See + "func t()\n    var a: Array<i32> = [1]\n    var b: Array<i32> = [2]\n    var r = a[0]@ref\n    let c = func (p: ref/i32 during r) => p\n    let v = c(b[0]@ref)\n    b = [3]\n    see(v)\n" + Main, DiagnosticCode.ComparisonLoanConflict_Kd, "b = [3]", null)]
    [InlineData("func f(p: ref/i32 during x, q: ref/i32 during y) -> ref/i32 during x\n    var r = p\n    let c = func (a: ref/i32 during r) => a\n    return c(q)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "return c(q)", "c(q)")]
    [InlineData("func f(p: ref/i32 during x, q: ref/i32 during y) -> ref/i32 during x\n    var r = p\n    let c = func [r] (a: ref/i32 during r) => a\n    return c(q)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "return c(q)", "c(q)")]
    [InlineData("func f(p: ref/i32 during x, q: ref/i32 during y) -> ref/i32 during x\n    var r = p\n    var c = func [var r] (a: ref/i32 during r)\n        let out = r\n        r = a\n        return out\n    _ = c(q)\n    return c(q)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "return c(q)", "c(q)")]
    [InlineData("func f(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    var r = p\n    var out = p\n    var i: i32 = 0\n    while i < 2\n        let c = func [r] () => r\n        out = c()\n        r = q\n        i += 1\n    return out\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "return out", "out")]
    [InlineData("func f(p: ref/i32, q: ref/i32, o: uniq/(ref/i32 during p))\n    var r = p\n    defer\n        let c = func [r] () => r\n        o@follow = c()\n    r = q\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "o@follow = c()", "c()")]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    var r = p\n    var out = p\n    let c = func [r] () => r\n    var i: i32 = 0\n    while i < 2\n        out = c()\n        r = q\n        i += 1\n    see(r)\n    return out\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "return out", "out")]
    public void ALaterBoundOfAnEnclosingRegionReachesTheResult(string source, DiagnosticCode code, string context, string? subject)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, subject)));

    // SPEC 15.8.2, R4-M159 (DECISIONS: G570): a region of a maker's body leaves it through the Closure Type's Origin arguments, inferred or
    // stated, so a caller reads the Origins its call bound.
    [Theory]
    [InlineData("func f(p: ref/i32 during x) -> ref/i32 during x\n    let make = func (a: ref/i32)\n        var r = a\n        return func [r] () => r\n    let g = make(p)\n    return g()\n" + Main)]
    [InlineData("func f(p: ref/i32 during x) -> ref/i32 during x\n    let make = func (a: ref/i32)\n        var r = a\n        return func [r] () -> ref/i32 during r => r\n    let g = make(p)\n    return g()\n" + Main)]
    [InlineData("func f(p: ref/i32 during x) -> ref/i32 during x\n    let outer = func (a: ref/i32)\n        let make = func (b: ref/i32)\n            var r = b\n            return func [r] () => r\n        return make(a)\n    let g = outer(p)\n    return g()\n" + Main)]
    public void AMakersRegionReachesItsCallerAsItsSources(string source) => MirTargetSide.Check(source);

    // The invalid counterparts: the caller's write under the result (m3) and a chain between two parameters of the maker's call (m14, at
    // the returned value: R7-O1 H, A5's record at the value).
    [Theory]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    let make = func (p: ref/i32)\n        var r = p\n        return func [r] () => r\n    let g = make(a@ref)\n    let v = g()\n    a = 2\n    see(v)\n" + Main, DiagnosticCode.ComparisonLoanConflict_Kd, "a = 2", null)]
    [InlineData("func f(p: ref/i32 during x, q: ref/i32 during y, k: bool) -> ref/i32 during x\n    let make = func (a: ref/i32, b: ref/i32)\n        var r = a\n        if k => r = b\n        return func [r] () -> ref/i32 during r => r\n    let g = make(p, q)\n    return g()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "return g()", "g()")]
    public void AMakersRegionKeepsItsSourcesAtTheCaller(string source, DiagnosticCode code, string context, string? subject)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, subject)));
}
