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
    // the receiver even when its body returns the parameter, retention being per argument (15.6.4), over a `var` as over a `let`.
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
}
