// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>What a value a call retains reaches (SPEC 15.6 Holders, 15.6.4; PLAN G147 (8)): the guards of G147's false errors, the twins that
/// a fix must keep rejected, each labelled from the SPEC.</summary>
public sealed partial class MirBorrowsTest
{
    // Holders a call constructs over its argument, and `make`, which borrows past the argument's referent: all three have the row
    // `result {0 Value}`. `swapOut` returns the old value beside its argument, `passBoth` a Tuple of concrete Types, and `hold` its argument.
    private const string RetainedShapes = """
        struct HeldBox<E>
            public var item: E
            public init(item: E) => self.item = item@move
        struct HeldCell<E> {a}
            public var link: uniq/E during a
            public init(link: uniq/E during a) => self.link = link@move
        group Past
            public func make<E>(link: uniq/(uniq/E during b) during a) -> HeldCell<E> during a
                return HeldCell<E>.init(link: link@follow@follow@uniq)
            public func swapOut<E>(link: uniq/E during a, with: E) -> (E, uniq/E during a)
                let old = Kimi.Intrinsics.exchange(link, with: with@move)
                return (old@move, link)
        func passBoth(item: (uniq/(uniq/i32 during q) during l, uniq/(uniq/i32 during q) during p)) -> (uniq/(uniq/i32 during q) during l, uniq/(uniq/i32 during q) during p)
            return item@move
        func hold<F>(f: F) -> F => f@move

        """;

    // SPEC 15.6, 15.6.3: a write that reaches a parameter's referent stores into storage the body does not own, whose values may be used
    // after every point, so `v@follow = x` errs: a borrow the callee formed past the argument's referent (pastBad, whose row is that of a
    // holder over the argument itself, so no rule over the rows alone tells it from G147's false errors), a second follow (secondBad), a
    // Value that is the parameter's reference (movedBad), a Reborrow through it (reborrowBad), a Tuple holding one beside a local borrow
    // (tupleBad; its callee is concrete, since a generic one adds a false error at `local`'s storage end), the uniq case of a pair layer
    // (pairBad), a read kept beside the argument, the old value `exchange` returns (swapBad). A holder that keeps the written value errs by
    // liveness: Moved into a parameter's referent (escapedBad), used below (usedBad). A read through a Reborrow carries what it reaches, not
    // the Reborrow's Loan (readOk), and a Closure's result that holds nothing (concreteOk) or is unused below (unusedOk) conflicts with
    // nothing.
    [Fact]
    public void AWriteThroughARetainedValueErrsWhereItReachesTheContent()
        => Assert.Equal(
            [
                "pastBad: v@follow = x", "secondBad: v@follow = x", "movedBad: v@follow = x", "reborrowBad: v@follow = x",
                "tupleBad: v@follow = x", "pairBad: v@follow = x", "escapedBad: v@follow = x", "usedBad: v@follow = x",
                "swapBad: v@follow = x",
            ],
            Problems(RetainedShapes + """
                func pastBad<T>(value: uniq/T during q, other: uniq/(uniq/T during q) during p, x: T) -> ()
                    T is Copy
                    var v = value@move
                    var slot = other@move
                    var cell = Past.make(slot@uniq)
                    let child = v
                    cell.link@follow = child@move
                    _ = slot@follow
                    v@follow = x
                func secondBad<T>(value: uniq/T during q, other: uniq/(uniq/T during q) during p, x: T) -> ()
                    T is Copy
                    var v = value@move
                    var slot = other@move
                    var box = HeldBox<uniq/(uniq/(uniq/T during q) during p)>.init(item: slot@uniq)
                    let child = v
                    box.item@follow@follow = child@move
                    _ = slot@follow
                    v@follow = x
                func movedBad(value: uniq/i32 during q, other: uniq/(uniq/i32 during q) during p, x: i32) -> ()
                    var v = value@move
                    var slot = other@move
                    var box = HeldBox<uniq/(uniq/i32 during q) during p>.init(item: slot@move)
                    let child = v
                    box.item@follow = child@move
                    v@follow = x
                func reborrowBad(value: uniq/i32 during q, other: uniq/(uniq/i32 during q) during p, x: i32) -> ()
                    var v = value@move
                    var slot = other@move
                    var cell = HeldCell<uniq/i32 during q>.init(link: slot@follow@uniq)
                    let child = v
                    cell.link@follow = child@move
                    v@follow = x
                func tupleBad(value: uniq/i32 during q, w: uniq/i32 during q, w2: uniq/(uniq/i32 during q) during p, x: i32) -> ()
                    var v = value@move
                    var local = w@move
                    var t = (local@uniq, w2@move)
                    var kept = passBoth(t@move)
                    let child = v
                    kept.1@follow = child@move
                    v@follow = x
                func pairBad<s/T>(value: s/T during q, other: uniq/(s/T during q) during p, x: T) -> ()
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var slot = other@move
                    var cell = HeldCell<s/T during q>.init(link: slot@follow@uniq)
                    let child = v
                    cell.link@follow = child@move
                    _ = slot@follow
                    v@follow = x
                func escapedBad(value: uniq/i32 during q, other: uniq/i32 during q, out: uniq/(uniq/i32 during q) during p, x: i32) -> ()
                    var v = value@move
                    var slot = other@move
                    var cell = HeldCell<uniq/i32 during q>.init(link: slot@uniq)
                    let child = v
                    cell.link@follow = child@move
                    out@follow = slot@move
                    v@follow = x
                func usedBad(value: uniq/i32 during q, other: uniq/i32 during q, x: i32) -> uniq/i32 during q
                    var v = value@move
                    var slot = other@move
                    var cell = HeldCell<uniq/i32 during q>.init(link: slot@uniq)
                    let child = v
                    cell.link@follow = child@move
                    v@follow = x
                    return slot@move
                func swapBad(value: uniq/i32 during q, other: Option<uniq/(uniq/i32 during q) during p>, x: i32) -> ()
                    var v = value@move
                    var slot = other@move
                    var pair = Past.swapOut(slot@uniq, .None)
                    let child = v
                    match pair.0@move
                        .Some(let r) => r@follow = child@move
                        .None => ()
                    v@follow = x
                func readOk<s/T>(value: s/T, x: T) -> T
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    v@follow = x
                    let c = v
                    let r = c@follow
                    let kept = hold(v)
                    _ = kept@follow
                    return r
                func concreteOk(value: uniq/i32) -> i32
                    var v = value@move
                    var h = func [v] () -> i32
                        return v@follow
                    let r = h()
                    let kept = hold(v)
                    _ = kept@follow
                    return r
                func unusedOk<s/T>(value: s/T, x: T) -> T
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var h = func [v] () -> T
                        return v@follow
                    let r = h()
                    _ = r
                    let kept = hold(v)
                    _ = kept@follow
                    return x
                """));
}
