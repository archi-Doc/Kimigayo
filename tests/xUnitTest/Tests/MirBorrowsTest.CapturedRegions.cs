// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A Closure over a `var` local without a stated Origin (SPEC 15.8.2, 7.7.2, 7.7.3; PLAN G553): the local's Type holds a local
/// region, which the Closure's environment keeps and its signature resolves to the region's sources. A result keeps its dependence on the
/// call receiver and the capture's Loans exactly as over a `let` binding or a stated Origin.</summary>
public sealed partial class MirBorrowsTest
{
    private const string OverParameter = "func f(value: uniq/i32 during q) -> ()\n    var v = value@move\n";

    private const string OverLocal = "func inspect<T>(value: ref/T) => ()\nfunc f() -> ()\n    var x: i32 = 1\n    var v = x@ref\n";

    private const string Arrow = "    var h = func [v] () => v\n";

    private const string TwoLive = "    let r = h()\n    let r2 = h()\n    r@follow = 1\n    r2@follow = 2\npublic func main() => ()\n";

    private const string WriteLive = "    let r = h()\n    x = 5\n    inspect(r)\npublic func main() => ()\n";

    private const string Main = "public func main() => ()\n";

    private const string See = "func see(p: ref/i32) => ()\n";

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

    // SPEC 7.7.2, 15.3.6, 15.6.4 (the consent review's B1): a capture is a snapshot whose local region is the meet of its sources as of the
    // literal, and a meet outlives what all its sources do, so a result naming one source of several cannot hold the capture and keeps
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

    // A recorded conservative limit (the owner's O-51, verbatim 「全部 (a)」; the root fix with R7-O2's local slot regions): these three are
    // valid and clean on the base, and shape C rejects them. The region's edge is a may-fact on its one node, which the rows read as a fit:
    // (k2, O-51, R7-O2) one Closure's meet reaches another Closure's occurrence of the region (SPEC 7.7.2: each capture is its own snapshot);
    // (v7, O-51, R7-O2) a position naming the meet as of the literal where the region, solved over a later bound, could not hold the capture
    // (SPEC 15.3.6); (v7u, O-51, R7-O2) its exclusive form. R7-O2 turns these into accepted rows; until then each is this record, here.
    // The class, widened by the owner's O-52 (verbatim 「a」; the same root fix): the meet is in source order, and a literal in a loop or a
    // `defer` runs after statements below it, so a source assigned there reaches the literal missing from the meet, and the result, which
    // only passes its parameter through, reads as holding the capture that does hold it, valid on either reading of `v7`: (z57, O-51, O-52,
    // R7-O2) a `while`, where a position over a parameter can hold no local Borrow (SPEC 15.6.5); (z64, O-51, O-52, R7-O2) a `for`; (z34,
    // O-51, O-52, R7-O2) a position over a local; (z67, O-51, O-52, R7-O2) a `defer`.
    [Theory]
    [InlineData(See + "func both<A, B, T>(x: ref/A, y: ref/B, t: T) -> T\n    return t@move\nfunc t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let s = a@ref\n    var r = s\n    let c1 = func [r] () => r\n    r = b@ref\n    let c2 = func [r] () => r\n    let v = both(c1@ref, c2@ref, s)\n    b = 3\n    see(v)\n" + Main, "b = 3", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    var c: i32 = 3\n    let s = a@ref\n    let w = b@ref\n    var r = s\n    let h = func [r, w] (p: ref/i32 during s) -> ref/i32 during s => p\n    let v = h(s)\n    r = c@ref\n    b = 3\n    see(v)\n    see(r)\n" + Main, "b = 3", null)]
    [InlineData("func t(value: uniq/i32 during q, other: uniq/i32 during q, third: uniq/i32 during z)\n    var v = value@move\n    var h = func [v] (p: uniq/i32 during q) -> uniq/i32 during q => p\n    let a = h(other)\n    v@follow = 3\n    a@follow = 1\n    v = third@move\n" + Main, "v@follow = 3", null)]
    [InlineData(See + "func f(q: ref/i32, n: i32)\n    var a: i32 = 1\n    var r = q\n    var v = q\n    var i: i32 = 0\n    while i < n\n        let c = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n        v = c(q)\n        r = a@ref\n        i += 1\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main, "a = 3", null)]
    [InlineData(See + "func f(q: ref/i32)\n    var a: i32 = 1\n    var r = q\n    var v = q\n    for n in 0..2\n        let c = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n        v = c(q)\n        r = a@ref\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main, "a = 3", null)]
    [InlineData(See + "func t()\n    var a: i32 = 1\n    var b: i32 = 2\n    let u = a@ref\n    let s = b@ref\n    var r = s\n    var v = s\n    var i: i32 = 0\n    while i < 2\n        let c = func [r] (p: ref/i32 during s) -> ref/i32 during s => p\n        v = c(s)\n        r = u\n        i += 1\n    a = 3\n    see(v)\n    see(a@ref)\n" + Main, "a = 3", null)]
    [InlineData(See + "func f(q: ref/i32)\n    var a: i32 = 1\n    var r = q\n    var v = q\n    defer\n        let c = func [r] (p: ref/i32 during q) -> ref/i32 during q => p\n        v = c(q)\n        a = 3\n        see(v)\n        see(a@ref)\n    r = a@ref\n" + Main, "a = 3", null)]
    public void ACapturedRegionsMeetIsAConservativeLimit(string source, string context, string? subject)
        => MirTargetSide.Check(source, (DiagnosticCode.ComparisonLoanConflict_Kd, MirTargetSide.At(context, subject)));

    // Not this unit's limit: a hole it leaves as dev has it, the other direction of the same root (PLAN G570; the owner's O-52, verbatim 「a」;
    // removal R7-O2). `ResolveLocalResultOrigins` publishes the result in source order too, so `q`, which reaches the literal on the next
    // iteration, never reaches the Origin check, and z50 is accepted. SPEC 15.6.5 gives it the record its control has, `out = r` with no
    // Closure, which this row states. It fails by design, so it is Explicit until R7-O2, its expectation unchanged: no row pins the acceptance.
    [Fact(Explicit = true)]
    public void ALaterSourceReachesTheOriginCheckPendingG570()
        => MirTargetSide.Check(
            "func f(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    var r = p\n    var out = p\n    var i: i32 = 0\n    while i < 2\n        let c = func [r] () => r\n        out = c()\n        r = q\n        i += 1\n    return out\n" + Main,
            (DiagnosticCode.UnprovenOriginRelation_Kd, MirTargetSide.At("return out", "out")));
}
