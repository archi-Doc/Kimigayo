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
    // hold though the body returns the capture.
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
    public void AResultOverACapturedRegionKeepsTheReceiverAndTheCapture(string source, string context, string? subject)
        => MirTargetSide.Check(source, (DiagnosticCode.ComparisonLoanConflict_Kd, MirTargetSide.At(context, subject)));

    // The valid twins: one call, a first result dead at the second call (also the second alone used, and a later source), a shared capture's
    // two results, also over a Move of the Closure, a Copy result, a Shared call's two Reborrows, `v` after the last use of the Closure's
    // results, a result that is the parameter with its capture unused, a Consuming call's result dead before the write.
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
    public void ACapturedRegionConflictsWithNothingLive(string source) => MirTargetSide.Check(source);

    // SPEC 15.3.6, 15.6.4 (the consent review's B1): a local has one region, the meet of all its sources, and a meet outlives what all its
    // sources do, so a result naming one source of several cannot hold the capture and keeps
    // none of its Loans: a source added after the capture, one before it, either order of a replaced source, two parameters' exclusive
    // references, `static` beside a local Borrow, a Closure that reads its capture, a Consuming call, a Closure over a Closure, one in a Field.
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
    // a `var` over one source is read as its `let` twin, per argument (d2's reasoning): x5 was accepted before this unit, x5l stays rejected.
    [Theory]
    [InlineData(See + "func keep<T, U>(t: ref/T, u: U) -> U\n    return u@move\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    var x = s\n    var w = b@ref\n    var h = (x, w)\n    let v = keep(h@ref, s)\n    b = 3\n    see(v)\n" + Main)]
    [InlineData(See + "func keep<T, U>(t: ref/T, u: U) -> U\n    return u@move\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    let x = s\n    let w = b@ref\n    let h = (x, w)\n    let v = keep(h@ref, s)\n    b = 3\n    see(v)\n" + Main)]
    public void AGenericCallReadsAVarOverOneSourceAsItsLetTwin(string source)
        => MirTargetSide.Check(source, (DiagnosticCode.ComparisonLoanConflict_Kd, MirTargetSide.At("b = 3", null)));

    // A recorded limit (the owner's O-57, verbatim 「両方とも(a)でお願いします。」): valid and accepted before G570, rejected until the Front's
    // G565 lands. `StaticOrigin` is Unknown for a region, so a `static`-only region is not Owned: (s1 and its renamed twin w4, O-57, G565) a
    // bound, (s2, O-57, G565) a capture's erasure, (s4, O-57, G565) a constructor's bound. The class, as the owner's O-59 names it (verbatim
    // 「すべて (a)」, amending O-57), keeps a struct's clause at its constructor: (t31, O-59, G572 U2) in the region's own body, as n21 is nested.
    // Each is this record, here, until its unit turns it accepted.
    [Theory]
    [InlineData("func need<T>(x: T)\n    T is Owned\nfunc f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    need(c())\n" + Main, DiagnosticCode.UnprovenConstraint_Kd, "need(c())", null)]
    [InlineData(See + "struct Box<T>\n    public var v: T\n    public init(v: T)\n        T is Owned\n        self.v = v@move\nfunc f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let b = Box.init(c())\n    see(b.v)\n" + Main, DiagnosticCode.UnprovenConstraint_Kd, "Box.init(c())", null)]
    [InlineData(See + "struct P {a, b}\n    origin a outlives b\n    public let x: ref/i32 during a\n    public let y: ref/i32 during b\n    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\nfunc f(p: ref/i32)\n    var r = p\n    let c = func [r] () => r\n    let b = P.init(c(), p)\n    see(b.x)\n" + Main, DiagnosticCode.UnprovenConstraint_Kd, "P.init(c(), p)", null)]
    public void AConstraintOverAnEnclosingRegionIsARecordedLimit(string source, DiagnosticCode code, string context, string? subject)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, subject)));

    // SPEC 10.7, 7.7.4, 15.6.1 (the owner's O-61, verbatim 「O-61 a」; DECISIONS: G570): a comparison with a Function Type is judged by the
    // contract alone, so a `var`'s region over a `static` source is no `static` there. Invalid, accepted by dev, rejected now: an erasure
    // (s2h), a call's argument (s2c), a Callable bound (c12). An acceptance hole closed, no limit.
    [Theory]
    [InlineData(See + "func f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    let g = func [y] () => y\n    var h: () -> ref/i32 during static = g@move\n    see(h())\n" + Main, DiagnosticCode.UnprovenConstraint_Kd, "g@move", null)]
    [InlineData(See + "func two(f: () -> ref/i32 during static)\n    see(f())\nfunc k(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    let g = func [y] () => y\n    two(g@move)\n" + Main, DiagnosticCode.NoApplicableOverload_Kd, "two(g@move)", null)]
    [InlineData(See + "func apply<F>(g: ref/F) -> i32\n    F is Callable<() -> ref/i32>\n    let w = g()\n    return w@follow\nfunc f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let n = apply(c@ref)\n    see(n@ref)\n" + Main, DiagnosticCode.UnprovenConstraint_Kd, "apply(c@ref)", null)]
    public void AComparisonWithAFunctionTypeReadsTheContractAlone(string source, DiagnosticCode code, string context, string? subject)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, subject)));

    // SPEC 15.3.6, 15.6.5 (DECISIONS: G572 U1): a region at a fixed longer end reads as its solved value, each bound outliving the target, and
    // A5 reads it, once every body is bound. The limit of O-57 for these forms ends here: (n1) a stated result, (n2) a store, (n6v) over a
    // call's result in a `var`, (n12) three bodies, (w1) a stated `let`, (w2) a stated parameter, each one bound. The same read through a
    // written result (z24b, b1n), two bounds a premise relates (a2), a meet at the position (a3), a body `var` (a5), a later bound a premise
    // relates (a13), slots as separate regions (t23); a join (n26), a stated Type argument (n27) and an Array slot's call (t21) in a nested
    // body, Binding's judge leaving the row; and own-body joins, `Unsupported_Kd` before, which take the same row: with no Closure (g1, g7,
    // g8), and over a Closure's result at a stated local (t29) and at the return (t37), O-59's.
    [Theory]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r] () -> ref/i32 during p\n        let inner = func [r] () => r\n        return inner()\n    see(outer())\n" + Main)]
    [InlineData("func f(p: ref/i32, o: uniq/(ref/i32 during p))\n    var r = p\n    var w = func [r, o] ()\n        let inner = func [r] () => r\n        o@follow = inner()\n    w()\n" + Main)]
    [InlineData(See + "func id(p: ref/i32) -> ref/i32 during p\n    return p\nfunc f(p: ref/i32)\n    var y = id(p)\n    let outer = func [y] () -> ref/i32 during p\n        let inner = func [y] () => y\n        return inner()\n    see(outer())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    let top = func [p] () -> ref/i32 during p\n        var y = p\n        let mid = func [y] () -> ref/i32 during p\n            let low = func [y] () => y\n            return low()\n        return mid()\n    see(top())\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r] () -> i32\n        let inner = func [r] () => r\n        let w: ref/i32 during p = inner()\n        return w@follow\n    let n = outer()\n    see(n@ref)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let outer = func [r] () -> i32\n        let inner = func [r] () => r\n        let g = func (z: ref/i32 during p) -> i32 => z@follow\n        return g(inner())\n    let n = outer()\n    see(n@ref)\n" + Main)]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    let c = func [r] () -> ref/i32 during p => r\n    see(c())\n" + Main)]
    [InlineData(See + "func t()\n    var b: i32 = 2\n    let s = b@ref\n    var r = s\n    let c = func [r] () -> ref/i32 during s => r\n    let v = c()\n    see(v)\n" + Main)]
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
    public void AFixedPositionReadsARegionsSolvedValue(string source)
        => MirTargetSide.Check(source);

    // The same rule, a bound that does not outlive the position (15.6.5, one tense): a later local (b3), a later parameter (b2), a source
    // through a reference before the literal (b5), a call's argument to a `during r` parameter (b4), a nested body with a later source (b8),
    // a loop's back edge (c1), a second capture's Origin (v7r). b1-stated-var's record returns to the write: G553's edge keeps the Loan.
    [Theory]
    [InlineData(See + "func f(p: ref/i32) -> ref/i32 during p\n    var a: i32 = 1\n    var r = p\n    let c = func [r] () -> ref/i32 during p => r\n    r = a@ref\n    return c()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r")]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32)\n    var r = p\n    let c = func [r] () -> ref/i32 during p => r\n    r = q\n    see(c())\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r")]
    [InlineData(See + "func f(p: ref/i32) -> ref/i32 during p\n    var a: i32 = 1\n    var r = p\n    let h = r@uniq\n    h@follow = a@ref\n    let c = func [r] () -> ref/i32 during p => r\n    return c()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r")]
    [InlineData(See + "func f(p: ref/i32) -> ref/i32 during p\n    var a: i32 = 1\n    var r = p\n    let g = func (x: ref/i32 during r) -> ref/i32 during p => x\n    return g(a@ref)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> x", "x")]
    [InlineData(See + "func f(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    var r = p\n    let outer = func [r] () -> ref/i32 during p\n        let inner = func [r] () -> ref/i32 during p => r\n        return inner()\n    r = q\n    return outer()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r")]
    [InlineData(See + "func t(s: ref/i32, n: i32)\n    var a: i32 = 1\n    var r = s\n    var last = s\n    var i = 0\n    while i < n\n        let c = func [r] (p: ref/i32 during s) -> ref/i32 during s => r\n        last = c(s)\n        r = a@ref\n        i += 1\n    see(last)\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "=> r", "r")]
    [InlineData(See + "func t()\n    var b: i32 = 2\n    let s = b@ref\n    var r = s\n    let c = func [r] () -> ref/i32 during s => r\n    let v = c()\n    b = 3\n    see(v)\n" + Main, DiagnosticCode.ComparisonLoanConflict_Kd, "b = 3", null)]
    public void ABoundThatDoesNotOutliveThePositionStaysRejected(string source, DiagnosticCode code, string context, string? subject)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, subject)));

    // SPEC 15.6.1 (DECISIONS: G572 U1): a body's clause is an obligation, so one over an enclosing region is A5's row, judged at the clause: a
    // later bound that does not hold (c1i), in a meet (e3), two bodies below the region (e6), through a slot (e2).
    [Theory]
    [InlineData(ClauseOver + "        let w: i32 = 1\n            origin r outlives p\n" + ClauseEnd, "origin r outlives p")]
    [InlineData(ClauseOver + "        let w: i32 = 1\n            origin (r and p) outlives p\n" + ClauseEnd, "origin (r and p) outlives p")]
    [InlineData(See + "func f(p: ref/i32)\n    var q: i32 = 1\n    var r = p\n    let c = func [r] () -> i32\n        let d = func [r] () -> i32\n            let w: i32 = 1\n                origin r outlives p\n            return w\n        return d()\n    r = q@ref\n    let n = c()\n    see(n@ref)\n" + Main, "origin r outlives p")]
    [InlineData(See + "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\nfunc f(p: ref/i32)\n    var q: i32 = 1\n    var r = p\n    let c = func [r, p] () -> i32\n        let h0 = H.init(p)\n        let h: H{x} = h0@move\n            origin r outlives x.s\n            origin x.s == p\n        return h.item@follow\n    r = q@ref\n    let n = c()\n    see(n@ref)\n" + Main, "origin r outlives x.s")]
    public void AClauseOverAnEnclosingRegionIsARow(string source, string clause)
        => MirTargetSide.Check(source, (DiagnosticCode.UnprovenOriginRelation_Kd, MirTargetSide.At(clause, null)));

    // An Equality row over an enclosing region is A5's too: `p outlives r` fails (15.3.6: no meet at an equality), beside the exclusive layer's
    // own row. (i1) a stated local; (e1) a call's equal Origins, the fixed one first, where the row has no input to relate, so the record loses
    // Binding's `relation` location and the exclusive layer's row now shows (Q13: dependent).
    [Theory]
    [InlineData(See + "func f(p: ref/i32)\n    var r = p\n    var outer = func [var r] ()\n        let u = r@uniq\n        let h: uniq/(ref/i32 during p) = u\n        see(h@follow)\n    outer()\n" + Main, "let h: uniq/(ref/i32 during p) = u", "uniq/(ref/i32 during p)")]
    [InlineData("func same(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o))\n    ()\nfunc f(p: ref/i32)\n    var r = p\n    var s = p\n    var outer = func [var r] (h: uniq/(ref/i32 during p))\n        same(h, r@uniq)\n    outer(s@uniq)\n" + Main, "r@uniq", "r@uniq")]
    public void AnEqualityOverAnEnclosingRegionIsA5s(string source, string first, string second)
        => MirTargetSide.Check(source, (DiagnosticCode.UnprovenOriginRelation_Kd, MirTargetSide.At(first, null)), (DiagnosticCode.UnprovenOriginRelation_Kd, MirTargetSide.At(second, null)));

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

    // s2 of the same limit (O-57, G565): the erasure's Owned record; the body's fit reads the region's value (G572 U1).
    [Fact]
    public void AnErasedCaptureOverAStaticRegionIsARecordedLimit()
        => MirTargetSide.Check(
            See + "func f(s: ref/i32 during static)\n    var r = s\n    let c = func [r] () => r\n    let y = c()\n    var g: () -> ref/i32 during static = func [y] () => y\n    see(g())\n" + Main,
            (DiagnosticCode.UnprovenConstraint_Kd, MirTargetSide.At("func [y] () => y", null)));

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

    // The invalid counterparts: the caller's write under the result (m3) and a chain between two parameters of the maker's call (m14).
    [Theory]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    let make = func (p: ref/i32)\n        var r = p\n        return func [r] () => r\n    let g = make(a@ref)\n    let v = g()\n    a = 2\n    see(v)\n" + Main, DiagnosticCode.ComparisonLoanConflict_Kd, "a = 2", null)]
    [InlineData("func f(p: ref/i32 during x, q: ref/i32 during y, k: bool) -> ref/i32 during x\n    let make = func (a: ref/i32, b: ref/i32)\n        var r = a\n        if k => r = b\n        return func [r] () -> ref/i32 during r => r\n    let g = make(p, q)\n    return g()\n" + Main, DiagnosticCode.UnprovenOriginRelation_Kd, "return g()", null)]
    public void AMakersRegionKeepsItsSourcesAtTheCaller(string source, DiagnosticCode code, string context, string? subject)
        => MirTargetSide.Check(source, (code, MirTargetSide.At(context, subject)));
}
