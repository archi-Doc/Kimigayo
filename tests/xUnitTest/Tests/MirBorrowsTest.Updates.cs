// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5-F8's coverage of compound assignment, increment, decrement and whole-value updates on MIR (SPEC 13.7.2, 15.6.7, 15.7; the
/// A5 Step 2 plan's 2d-5 and 2d-0's diverging-update outcome, PLAN G396 (1)): an update protects its destination from its location to its
/// write, also through a pair layer and on the assumed path after a diverging operand, and `replace`, `exchange` and `swap` are calls
/// whose targets activate together. Each row's label is from SPEC; the rows passed on arrival, as coverage rows do.</summary>
public sealed partial class MirBorrowsTest
{
    // The callees and Types of the update rows: an exclusive use of an i32, an index that takes its Array exclusively, a counter returning
    // a value, and a computed and a stored Property with custom setters.
    private const string UpdateShapes = """
        func touch(value: uniq/i32) => ()
        func touchI(value: uniq/Array<i32>) -> isize => 0
        func bumpN(value: uniq/i32) -> i32 => 1
        struct UGauge
            var hits: i32 = 0
            public var mark: i32 = 0
                set(value: i32) -> () => storage = value
            public computed level: i32
                get() -> i32 => self.hits
                set(value: i32) -> () => self.hits = value
            public init() => ()

        """;

    // SPEC 13.7.2's writeback protection beside TheWritebackActivatesItsReservation and the setter rows
    // (ASettersReservationProtectsItsReceiverUntilTheWrite): a live exclusive Loan meets the update's read (c3i, c9u for a prefix
    // increment); the right-hand side completes before the destination is located (c4v), but an index that takes the Array exclusively
    // meets the index call's reservation (c5i). A shared Loan live across the write conflicts for a fixed-array element (c6i), an Array
    // element at another index (c7i: the Array's index borrows the whole Array, SPEC 4.7.1), a Field of a fixed-array element (c8i), a
    // postfix increment and a prefix decrement (c9i, c9p), a write through a reference (c10i), a stored Field (c12i), and a computed and a
    // stored Property's setter below a pair layer (c11i, c11m; G1112's form, R4-M223). Distinct constant indices and distinct Fields of a
    // fixed array or a struct are disjoint (c6v, c8v, c12v); an increment's result is a value (c9v); c10v and c11v (the Loan ended first)
    // are valid.
    [Fact]
    public void AnUpdateProtectsItsDestinationUntilItsWrite()
        => Assert.Equal(
            [
                "c3i: x += 1", "c5i: touchI(values@uniq) CallActivationConflict_Kd", "c6i: a[0] += 1 PlacementActivationConflict_Kd",
                "c7i: values[1] += 1 PlacementActivationConflict_Kd", "c8i: fs[0].a += 1 PlacementActivationConflict_Kd",
                "c9i: x++ PlacementActivationConflict_Kd", "c9p: --x PlacementActivationConflict_Kd", "c9u: ++x",
                "c10i: p@follow += 1 PlacementActivationConflict_Kd", "c11i: p.level += 1 CallActivationConflict_Kd",
                "c11m: p.mark += 1 CallActivationConflict_Kd", "c12i: pr.a *= 2 PlacementActivationConflict_Kd",
            ],
            Problems(UpdateShapes + """
                func c3i()
                    var x = 1
                    let u = x@uniq
                    x += 1
                    touch(u)
                func c4v()
                    var x = 1
                    x += bumpN(x@uniq)
                func c5i()
                    var values: Array<i32> = [1, 2]
                    values[touchI(values@uniq)] += 1
                func c6v()
                    var a: [2 of i32] = [1, 2]
                    let r = a[0]@ref
                    a[1] += 1
                    inspect(r)
                func c6i()
                    var a: [2 of i32] = [1, 2]
                    let r = a[0]@ref
                    a[0] += 1
                    inspect(r)
                func c7i()
                    var values: Array<i32> = [1, 2]
                    let r = values[0]@ref
                    values[1] += 1
                    inspect(r)
                func c8v()
                    var fs: [2 of Pair] = [Pair.init(), Pair.init()]
                    let r = fs[0].a@ref
                    fs[0].b += 1
                    fs[1].a += 1
                    inspect(r)
                func c8i()
                    var fs: [2 of Pair] = [Pair.init(), Pair.init()]
                    let r = fs[0].a@ref
                    fs[0].a += 1
                    inspect(r)
                func c9i()
                    var x = 1
                    let r = x@ref
                    x++
                    inspect(r)
                func c9p()
                    var x = 1
                    let r = x@ref
                    --x
                    inspect(r)
                func c9u()
                    var x = 1
                    let u = x@uniq
                    ++x
                    touch(u)
                func c9v()
                    var x = 1
                    let old = x++
                    --x
                    inspect(old@ref)
                func c10v(p: uniq/i32)
                    p@follow += 1
                    p@follow++
                func c10i(p: uniq/i32)
                    let q = p@follow@ref
                    p@follow += 1
                    inspect(q)
                func c11i<s/T>(value: s/T, p: s/UGauge)
                    s is uniq
                    let r = p@follow@ref
                    p.level += 1
                    inspect(r)
                func c11v<s/T>(value: s/T, p: s/UGauge)
                    s is uniq
                    let r = p@follow@ref
                    inspect(r)
                    p.level += 1
                    p.mark++
                func c11m<s/T>(value: s/T, p: s/UGauge)
                    s is uniq
                    let r = p@follow@ref
                    p.mark += 1
                    inspect(r)
                func c12v()
                    var pr = Pair.init()
                    let r = pr.a@ref
                    pr.b += 1
                    inspect(r)
                func c12i()
                    var pr = Pair.init()
                    let r = pr.a@ref
                    pr.a *= 2
                    inspect(r)
                """));

    // 2d-0's outcome for a diverging update (SPEC 13.7.2 "If a step does not complete, later steps do not run", 14.10.3): no rule and no
    // located Unsupported_Kd (2d-5u is void). A diverging right-hand side or index (`return`, `$abort`) builds and is valid (c13v, c14v,
    // c14a for an Array index, c15v); on its assumed path the update's write still runs, so a shared Loan a later use keeps meets it, for a
    // local (c13i, c15i) and a computed Property's setter reservation (c16i), as R4-M37 G6 checks an activation there; with no later use
    // the program is valid (c13n).
    [Fact]
    public void ADivergingUpdateIsCheckedOnItsAssumedPath()
        => Assert.Equal(
            [
                "c13i: x += (return 1) PlacementActivationConflict_Kd", "c15i: x -= $abort(\"h\") PlacementActivationConflict_Kd",
                "c16i: g.level += (return 1) CallActivationConflict_Kd",
            ],
            Problems(UpdateShapes + """
                func c13v() -> i32
                    var x = 1
                    x += (return 1)
                    return x
                func c13i() -> i32
                    var x = 1
                    let r = x@ref
                    x += (return 1)
                    inspect(r)
                    return x
                func c13n() -> i32
                    var x = 1
                    let r = x@ref
                    x += (return 1)
                    return x
                func c14v() -> i32
                    var a: [2 of i32] = [1, 2]
                    a[(return 0)] += 1
                    return 1
                func c14a() -> i32
                    var values: Array<i32> = [1, 2]
                    values[(return 0)] += 1
                    return 1
                func c15v()
                    var x = 1
                    x += $abort("h")
                func c15i()
                    var x = 1
                    let r = x@ref
                    x -= $abort("h")
                    inspect(r)
                func c16i() -> i32
                    var g = UGauge.init()
                    let r = g@ref
                    g.level += (return 1)
                    inspect(r)
                    return 0
                """));

    // SPEC 15.7 beside its examples (VerifiedSpecExampleTest): `swap`'s targets must be statically disjoint, so distinct constant indices
    // of a fixed array are valid (w5v) and the same index (w5i) or two Array elements (w5a: each index takes the whole Array) are not. A
    // target's activation meets a live shared Loan for `replace` and `exchange` (w6i, w6x; w6v ends it first), a fixed-array element's
    // Field (w9i; w9v writes another Field), a Field below a pair layer (w11i; w11v another Field) and a borrowed target Reborrowed without
    // a spelling (w12i; w12v); an exclusive argument meets the target's reservation (w13i, SPEC 15.6.7). `exchange` replaces no holder
    // (SPEC 15.6): the slot keeps the old referent's Loan beside the new one (w7v, w7j) and the old value carries it (w7i); replacing a
    // slot meets a borrow of the slot itself (w8r, SPEC 15.7.3's example through `replace`).
    [Fact]
    public void AWholeValueUpdateActivatesItsTargets()
        => Assert.Equal(
            [
                "w5i: Kimi.Intrinsics.swap(a[0]@uniq, a[0]@uniq) CallReservationConflict_Kd",
                "w5a: Kimi.Intrinsics.swap(values[0]@uniq, values[1]@uniq) CallReservationConflict_Kd",
                "w6i: Kimi.Intrinsics.replace(p@uniq, with: 2) CallActivationConflict_Kd",
                "w6x: Kimi.Intrinsics.exchange(p@uniq, with: 2) CallActivationConflict_Kd", "w7v: a = 5", "w7i: a = 5", "w7j: b = 5",
                "w8r: Kimi.Intrinsics.replace(slot@uniq, with: other@ref) CallActivationConflict_Kd",
                "w9i: Kimi.Intrinsics.replace(fs[0].a@uniq, with: 3) CallActivationConflict_Kd",
                "w11i: Kimi.Intrinsics.replace(p.a@uniq, with: 3) CallActivationConflict_Kd",
                "w12i: Kimi.Intrinsics.replace(p, with: 3) CallActivationConflict_Kd", "w13i: bumpN(x@uniq) CallReservationConflict_Kd",
            ],
            Problems(UpdateShapes + """
                func w5v()
                    var a: [2 of i32] = [1, 2]
                    Kimi.Intrinsics.swap(a[0]@uniq, a[1]@uniq)
                func w5i()
                    var a: [2 of i32] = [1, 2]
                    Kimi.Intrinsics.swap(a[0]@uniq, a[0]@uniq)
                func w5a()
                    var values: Array<i32> = [1, 2]
                    Kimi.Intrinsics.swap(values[0]@uniq, values[1]@uniq)
                func w6i()
                    var p: i32 = 1
                    let r = p@ref
                    Kimi.Intrinsics.replace(p@uniq, with: 2)
                    inspect(r)
                func w6v()
                    var p: i32 = 1
                    let r = p@ref
                    inspect(r)
                    Kimi.Intrinsics.replace(p@uniq, with: 2)
                func w6x()
                    var p: i32 = 1
                    let r = p@ref
                    let old = Kimi.Intrinsics.exchange(p@uniq, with: 2)
                    inspect(r)
                func w7v()
                    var a = 1
                    var b = 2
                    var slot: ref/i32 = a@ref
                    let old = Kimi.Intrinsics.exchange(slot@uniq, with: b@ref)
                    a = 5
                    inspect(slot)
                func w7i()
                    var a = 1
                    var b = 2
                    var slot: ref/i32 = a@ref
                    let old = Kimi.Intrinsics.exchange(slot@uniq, with: b@ref)
                    a = 5
                    inspect(old)
                func w7j()
                    var a = 1
                    var b = 2
                    var slot: ref/i32 = a@ref
                    let old = Kimi.Intrinsics.exchange(slot@uniq, with: b@ref)
                    b = 5
                    inspect(slot)
                func w8r()
                    var data = Data.init()
                    var other = Data.init()
                    var slot: ref/Data = data@ref
                    let inner = slot@ref
                    Kimi.Intrinsics.replace(slot@uniq, with: other@ref)
                    inspect(inner)
                func w9v()
                    var fs: [2 of Pair] = [Pair.init(), Pair.init()]
                    let r = fs[0].a@ref
                    Kimi.Intrinsics.replace(fs[0].b@uniq, with: 3)
                    inspect(r)
                func w9i()
                    var fs: [2 of Pair] = [Pair.init(), Pair.init()]
                    let r = fs[0].a@ref
                    Kimi.Intrinsics.replace(fs[0].a@uniq, with: 3)
                    inspect(r)
                func w11v<s/T>(value: s/T, p: s/Pair)
                    s is uniq
                    let r = p@follow.a@ref
                    Kimi.Intrinsics.replace(p.b@uniq, with: 3)
                    inspect(r)
                func w11i<s/T>(value: s/T, p: s/Pair)
                    s is uniq
                    let r = p@follow.a@ref
                    Kimi.Intrinsics.replace(p.a@uniq, with: 3)
                    inspect(r)
                func w12v(p: uniq/i32)
                    Kimi.Intrinsics.replace(p, with: 3)
                    let old = Kimi.Intrinsics.exchange(p, with: 4)
                func w12i(p: uniq/i32)
                    let q = p@follow@ref
                    Kimi.Intrinsics.replace(p, with: 3)
                    inspect(q)
                func w13i()
                    var x = 1
                    Kimi.Intrinsics.replace(x@uniq, with: bumpN(x@uniq))
                """));
}
