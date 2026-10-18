// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>The MIR-expressible cases of prototype round 10 (draft/Sketches/A5 Step 0/prototype.md, 246 cases) that the rule tests do not already
/// spell, with the verdicts of the adopted design (its PrefixKill mode, A3 E8; the listed losses as listed), each function named by its case.
/// Left out: the linked-list walks (30a–30d, 35a–35b, 82–83) and 25c, which bind their payloads through a match (F8); the F6 cases (33a–33e,
/// 36a–36g, `preserves results`); 108, which Binding rejects (its Origin relation); 8c and 120b, which have no MIR-problem observable or
/// form; and 75 (PLAN G147).</summary>
public sealed partial class MirBorrowsTest
{
    // SPEC 15.6, 15.6.3, 15.7 (prototype rounds 1–2, round 10's design verdicts): replacing a holder ends its Loans, a Reborrow keeps its
    // parents' Loans after its base is replaced, liveness reaches through references, assumed predecessors join, and moving, exchanging or
    // exclusively borrowing a base while its child lives conflicts there (S1′; 9k and 9l are its listed losses, 5b and 7 P1's: a partial
    // write ends no Loan).
    [Fact]
    public void TheBaseCasesKeepTheirVerdicts()
        => Assert.Equal(
            [
                "c3: data", "c4: data", "c4b: data", "c5: a = 3", "c5b: a = 3", "c5c: a = 3", "c6: x = 3", "c7: a = 3", "c7b: a = 3", "c8a: cursor.children",
                "c8a: cursor.children.clear() CallActivationConflict_Kd", "c9a: x = 3", "c9b: x = 3", "c9c: x = 3", "c9d: x = 3", "c9e: x = 3",
                "c9e2: h.link@follow = 7", "c9f: x = 3", "c9g: r@uniq", "c9g: Kimi.Intrinsics.exchange(r@uniq, with: y@uniq) CallActivationConflict_Kd",
                "c9m: r@uniq", "c9m: Kimi.Intrinsics.exchange(r@uniq, with: y@uniq) CallActivationConflict_Kd", "c9n: r@uniq",
                "c9n: Kimi.Intrinsics.exchange(r@uniq, with: y@uniq) CallActivationConflict_Kd", "c9o: r@uniq",
                "c9o: Kimi.Intrinsics.swap(r@uniq, s@uniq) CallActivationConflict_Kd", "c9h: w", "c9h: steal(w, y@uniq) CallActivationConflict_Kd",
                "c9i: r@ref", "c9j: x = 3", "c9k: r", "c9l: r@uniq", "c11b: x = 3", "c12a: r", "c12b: r", "c13b: slot = y@uniq", "c13e: slot = y@uniq",
                "c14: h"
            ],
            Problems("""
                func consume(d: Data) => ()
                func pass(v: uniq/i32) -> uniq/i32 during v => v
                func steal(slot: uniq/(uniq/i32 during a), value: uniq/i32 during a) -> uniq/i32 during a => Kimi.Intrinsics.exchange(slot, with: value)
                func take(v: uniq/i32) => ()
                struct LR {a}
                    Self is Copy
                    public var left: ref/i32 during a
                    public var right: ref/i32 during a
                    public init(left: ref/i32 during a, right: ref/i32 during a)
                        self.left = left
                        self.right = right
                struct BoxU {a}
                    public var item: uniq/i32 during a
                    public init(item: uniq/i32 during a) => self.item = item
                struct HL {a}
                    public var link: uniq/i32 during a
                    public var n: i32 = 0
                    public init(link: uniq/i32 during a) => self.link = link
                func c1()
                    var a = 1
                    var b = 2
                    var r = a@ref
                    r = b@ref
                    a = 3
                    inspect(r)
                func c3()
                    var data = Data.init()
                    var slot: ref/Data = data@ref
                    let r = slot@ref
                    let copy = r@follow
                    consume(data@move)
                    inspect(copy)
                func c3b()
                    var data = Data.init()
                    var other = Data.init()
                    var slot: ref/Data = data@ref
                    let r = slot@ref
                    let copy = r@follow
                    slot = other@ref
                    inspect(copy)
                func c4()
                    var data = Data.init()
                    var slot: ref/Data = data@ref
                    let r = slot@ref
                    consume(data@move)
                    inspect(r@follow@follow@ref)
                func c4b()
                    var data = Data.init()
                    var slot: ref/Data = data@ref
                    consume(data@move)
                    let r = slot@ref
                    inspect(r@follow@follow@ref)
                func c5(n: isize)
                    var z = 0
                    var a = 1
                    var refs: [2 of ref/i32] = [z@ref, z@ref]
                    refs[n] = a@ref
                    a = 3
                    inspect(refs)
                func c5b()
                    var z = 0
                    var a = 1
                    var refs: [2 of ref/i32] = [z@ref, z@ref]
                    refs[0] = a@ref
                    refs[0] = z@ref
                    a = 3
                    inspect(refs)
                func c5c(n: isize)
                    var z = 0
                    var a = 1
                    var refs: [2 of ref/i32] = [z@ref, z@ref]
                    refs[n] = a@ref
                    refs[n] = z@ref
                    a = 3
                    inspect(refs)
                func c6()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    r = y@uniq
                    x = 3
                    inspect(c)
                func c6b()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    r = y@uniq
                    inspect(c)
                func c7()
                    var a = 1
                    var b = 2
                    var c = 3
                    var p = LR.init(left: a@ref, right: b@ref)
                    var q = p
                    q.left = c@ref
                    a = 3
                    inspect(q)
                func c7b()
                    var a = 1
                    var b = 2
                    var c = 3
                    var p = LR.init(left: a@ref, right: b@ref)
                    var q = p
                    q.left = c@ref
                    a = 3
                    inspect(p)
                func c8a(root: uniq/Tree) -> uniq/Tree during root
                    var cursor = root
                    while cursor.children.length > 0
                        let next = cursor.children[0]@uniq
                        cursor.children.clear()
                        cursor = next
                    return cursor
                func c9a()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@ref
                    let d = c
                    r = y@uniq
                    x = 3
                    inspect(d)
                func c9b()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    var box = BoxU.init(item: c@move)
                    r = y@uniq
                    x = 3
                    inspect(box)
                func c9c()
                    var x = 1
                    var y = 2
                    var z = 4
                    var r = x@uniq
                    var s = r@follow@uniq
                    let d = s@follow@uniq
                    s = z@uniq
                    r = y@uniq
                    x = 3
                    inspect(d)
                func c9d(flag: bool)
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    var c = r@follow@uniq
                    while flag
                        inspect(c)
                        r = y@uniq
                    x = 3
                    inspect(c)
                func c9d2(flag: bool)
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    var c = r@follow@uniq
                    while flag
                        inspect(c)
                        r = y@uniq
                    y = 3
                    inspect(c)
                func c9e()
                    var x = 1
                    var y = 2
                    var h = HL.init(link: x@uniq)
                    let c = h.link@follow@uniq
                    h = HL.init(link: y@uniq)
                    x = 3
                    inspect(c)
                func c9e2()
                    var x = 1
                    var h = HL.init(link: x@uniq)
                    let c = h.link@follow@uniq
                    h.n = 5
                    h.link@follow = 7
                    inspect(c)
                func c9f()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    let d = pass(c)
                    r = y@uniq
                    x = 3
                    inspect(d)
                func c9g()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq)
                    old@follow = 7
                    inspect(c)
                func c9m()
                    var x = 1
                    var y = 2
                    var z = 3
                    var r = x@uniq
                    let c = r@follow@uniq
                    let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq)
                    r = z@uniq
                    old@follow = 7
                    inspect(c)
                func c9n()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq)
                    old@follow = 3
                    c@follow = 4
                func c9o()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    var s = y@uniq
                    let c = r@follow@uniq
                    Kimi.Intrinsics.swap(r@uniq, s@uniq)
                    s@follow = 3
                    c@follow = 4
                func c9h()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let w = r@uniq
                    let c = w@follow@follow@uniq
                    let old = steal(w, y@uniq)
                    old@follow = 7
                    inspect(c)
                func c9i()
                    var x = 1
                    var r = x@uniq
                    let c = r@follow@uniq
                    let z = r@ref
                    let v = z@follow@follow
                    inspect(c)
                func c9j()
                    var x = 1
                    var o = 5
                    var slot = x@uniq
                    var other = o@uniq
                    var r = slot@uniq
                    let c = r@follow@follow@uniq
                    r = other@uniq
                    x = 3
                    inspect(c)
                func c9k()
                    var x = 1
                    var r = x@uniq
                    let c = r@follow@uniq
                    let q = r@move
                    inspect(c)
                    inspect(q)
                func c9l()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    let w = r@uniq
                    w@follow = y@uniq
                    inspect(c)
                func c11b(flag: bool) -> i32
                    var x = 1
                    var y = 2
                    var r = y@ref
                    if flag
                        r = x@ref
                        return 1
                    else
                        r = y@ref
                        return 2
                    x = 3
                    inspect(r)
                    return 0
                func c12a(flag: bool)
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    if flag => take(r@move)
                    r = y@uniq
                    c@follow = 1
                func c12b(flag: bool)
                    var x = 1
                    var r = x@uniq
                    let c = r@follow@uniq
                    if flag => take(r@move)
                    c@follow = 1
                func c13a()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr = slot2@uniq
                    c@follow = 1
                func c13b()
                    var x = 1
                    var y = 2
                    var slot = x@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    slot = y@uniq
                    c@follow = 1
                func c13c()
                    var x = 1
                    var y = 2
                    var slot = x@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr@follow = y@uniq
                    c@follow = 1
                func c13d()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rs = slot@ref
                    let c = rs@follow@follow@ref
                    rs = slot2@ref
                    inspect(c)
                func c13e()
                    var x = 1
                    var y = 2
                    var slot = x@uniq
                    var rs = slot@ref
                    let c = rs@follow@follow@ref
                    slot = y@uniq
                    inspect(c)
                func c14()
                    var x = 1
                    var h = HL.init(link: x@uniq)
                    let c = h.link@follow@uniq
                    let g = h@move
                    c@follow = 1
                    inspect(g)
                """));

    // SPEC 15.6, 15.6.3 (prototype rounds 3–5): an alias formed through a Reborrow, a Copy, an aggregate or a store keeps the Loan its access
    // reaches, a dead holder's reference still carries its Loans, a partial Move keeps the moved part's, a cursor stored back into its base
    // releases the root only where the root is not used, and one borrow site in a loop conflicts with its own earlier iteration.
    [Fact]
    public void TheAliasAndLoopCasesKeepTheirVerdicts()
        => Assert.Equal(
            [
                "c16a: r@follow = 5", "c16b: r@follow@uniq", "c16c: r@follow = 5", "c16d: r@follow = 5", "c16e: slot@follow = 5", "c17a: data", "c17b: data",
                "c18a: a = 3", "c18b: a = 3", "c19b: root.value = 2", "c20a: v@uniq", "c20b: v@uniq", "c20b: retain(v@uniq) CallActivationConflict_Kd",
                "c20c: v@uniq", "c20d: v@ref", "c20e: r@follow@uniq", "c20f: v@uniq", "c20f: v + 0",
                "c20f: retainN(v@uniq, v + 0) CallActivationConflict_Kd", "c22a: arr[j]", "c22a: arr[j]@uniq",
                "c22a: Kimi.Intrinsics.exchange(arr[j]@uniq, with: z@uniq) CallActivationConflict_Kd"
            ],
            Problems("""
                func consume(d: Data) => ()
                func retain(t: uniq/i32) -> uniq/i32 during t => t
                func retainN(t: uniq/i32, n: i32) -> uniq/i32 during t => t
                struct P2 {a}
                    public var left: ref/i32 during a
                    public var right: ref/i32 during a
                    public init(left: ref/i32 during a, right: ref/i32 during a)
                        self.left = left
                        self.right = right
                struct BoxU {a}
                    public var item: uniq/i32 during a
                    public init(item: uniq/i32 during a) => self.item = item
                func c16a()
                    var x = 1
                    var r = x@uniq
                    let a = r
                    let c = a@follow@uniq
                    r@follow = 5
                    inspect(c)
                func c16b()
                    var x = 1
                    var r = x@uniq
                    let z = r@follow@ref
                    let z2 = z
                    let c = r@follow@uniq
                    inspect(z2)
                    inspect(c)
                func c16c()
                    var x = 1
                    var r = x@uniq
                    let c = r@follow@uniq
                    var h = BoxU.init(item: c)
                    r@follow = 5
                    inspect(h)
                func c16d()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    var s = y@uniq
                    let w = s@uniq
                    w@follow = r
                    r@follow = 5
                    inspect(s@follow@ref)
                func c16e()
                    var x = 1
                    var slot = x@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    slot@follow = 5
                    inspect(c)
                func c17a()
                    var data = Data.init()
                    var slot: ref/Data = data@ref
                    var mid = slot@ref
                    var top = mid@ref
                    consume(data@move)
                    let v = top@follow@follow@follow.value
                func c17b()
                    var data = Data.init()
                    var slot: ref/Data = data@ref
                    var w = slot@uniq
                    consume(data@move)
                    let t = w@follow@ref
                    let v = t@follow@follow.value
                func c18a()
                    var a = 1
                    var b = 2
                    var c = 3
                    var p = P2.init(left: a@ref, right: b@ref)
                    let l = p.left@move
                    p.left = c@ref
                    a = 3
                    inspect(p)
                func c18b()
                    var a = 1
                    var b = 2
                    var p = P2.init(left: a@ref, right: b@ref)
                    let l = p.left@move
                    a = 3
                    inspect(l)
                func c19a(root: uniq/Tree)
                    var cursor = root
                    while cursor.children.length > 0
                        cursor = cursor.children[0]@uniq
                    cursor.value = 1
                func c19b(root: uniq/Tree)
                    var cursor = root
                    while cursor.children.length > 0
                        cursor = cursor.children[0]@uniq
                    root.value = 2
                    cursor.value = 1
                func c20a(flag: bool)
                    var v = 1
                    var w = 2
                    var prev = w@uniq
                    while flag
                        let cur = v@uniq
                        inspect(prev)
                        prev = cur
                func c20b(flag: bool)
                    var v = 1
                    var w = 2
                    var keep = w@uniq
                    while flag
                        let k2 = retain(v@uniq)
                        inspect(keep)
                        keep = k2
                func c20c(flag: bool)
                    var v = 1
                    var z = 0
                    var keep: ref/i32 = z@ref
                    while flag
                        let u = v@uniq
                        u@follow = 1
                        inspect(keep)
                        let a = v@ref
                        keep = a
                func c20d(flag: bool)
                    var v = 1
                    var w = 2
                    var keep = w@uniq
                    while flag
                        let s = v@ref
                        inspect(s)
                        inspect(keep)
                        let a = v@uniq
                        keep = a
                func c20e(flag: bool)
                    var x = 1
                    var w = 2
                    var r = x@uniq
                    var prev = w@uniq
                    while flag
                        let cur = r@follow@uniq
                        inspect(prev)
                        prev = cur
                func c20f(flag: bool)
                    var v = 1
                    var w = 2
                    var keep = w@uniq
                    while flag
                        let k2 = retainN(v@uniq, v + 0)
                        inspect(keep)
                        keep = k2
                func c22a(i: isize, j: isize)
                    var x = 1
                    var y = 2
                    var z = 3
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    let c = arr[i]@follow@uniq
                    let old = Kimi.Intrinsics.exchange(arr[j]@uniq, with: z@uniq)
                    old@follow = 7
                    inspect(c)
                func c22b()
                    var x = 1
                    var y = 2
                    var z = 3
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    let c = arr[0]@follow@uniq
                    let old = Kimi.Intrinsics.exchange(arr[1]@uniq, with: z@uniq)
                    old@follow = 7
                    inspect(c)
                """));

    // SPEC 15.6.3 (A3 E8; the case 23a review's 50–94 with 23b, 34b and 37): replacing an outer holder, a constant element or a Field ends
    // the children formed through its old value, whose parent Loans protect the old route; a runtime index, another element or Field, a
    // Move, exchange or swap ends none; an obj handle's replacement ends its payload's children, an rc one's release may run a drop that
    // writes through them.
    [Fact]
    public void TheOuterReplacementCasesKeepTheirVerdicts()
        => Assert.Equal(
            [
                "c23b: q@follow@follow = 5", "c34b: arr[i]", "c34b: arr[i]@follow@follow = 7", "c50: q.p@follow@uniq", "c51: q", "c52: q.p@follow@uniq",
                "c53: q", "c53: keepT(q) CallActivationConflict_Kd", "c55: s.pa@follow = 9", "c56: q.pa@follow@uniq", "c57: rr@follow@follow = 7",
                "c59: slot2@uniq", "c60: rr@follow@follow = 7", "c62: rr", "c63: rr@uniq",
                "c63: Kimi.Intrinsics.exchange(rr@uniq, with: slot2@uniq) CallActivationConflict_Kd", "c64: rr@uniq",
                "c64: Kimi.Intrinsics.swap(rr@uniq, rr2@uniq) CallActivationConflict_Kd", "c66: rr@follow@follow = 7", "c68: arr[0]@follow@follow = 7",
                "c69: arr[j]", "c69: arr[0]@follow@follow = 7", "c70: arr[j]", "c70: arr[i]", "c70: arr[i]@follow@follow = 7", "c72: m[i]",
                "c72: m[i][0]@follow@follow = 7", "c74: p.pa@follow@follow = 7", "c77: slot@follow = 5", "c79: slot@follow = 5",
                "c80: h = Kimi.Intrinsics.makeRc(HRD.init(r: slot2@uniq))", "c81: q@follow@follow = 5", "c84: rr = slot2@uniq CallReservationConflict_Kd",
                "c85: rr = slot2@uniq CallReservationConflict_Kd", "c86: c@follow = 1", "c87: slot@follow = 5", "c88: arr[j]", "c88: arr[j]@follow = 7",
                "c90: slot@follow = 5", "c91: t = TTD.init(r: slot2@uniq, c: t.r@follow@follow@uniq)", "c94: slot@follow@uniq"
            ],
            Problems("""
                func stashU(v: uniq/(uniq/i32 during a)) => ()
                struct UV {a}
                    public var p: uniq/i32 during a
                    public init(p: uniq/i32 during a) => self.p = p
                    public func view(self: ref/Self) -> ref/Self during self => self
                struct AB {a}
                    public var pa: uniq/i32 during a
                    public var pb: uniq/i32 during a
                    public init(pa: uniq/i32 during a, pb: uniq/i32 during a)
                        self.pa = pa
                        self.pb = pb
                struct PA {a}
                    public var pa: uniq/(uniq/i32 during a) during a
                    public var pb: uniq/(uniq/i32 during a) during a
                    public init(pa: uniq/(uniq/i32 during a) during a, pb: uniq/(uniq/i32 during a) during a)
                        self.pa = pa
                        self.pb = pb
                struct HR {a}
                    public var r: uniq/(uniq/i32 during a) during a
                    public init(r: uniq/(uniq/i32 during a) during a) => self.r = r
                struct HRD {a}
                    public var r: uniq/(uniq/i32 during a) during a
                    public init(r: uniq/(uniq/i32 during a) during a) => self.r = r
                    drop
                        self.r@follow@follow = 0
                struct TT {a}
                    public var r: uniq/(uniq/i32 during a) during a
                    public var c: uniq/i32 during a
                    public init(r: uniq/(uniq/i32 during a) during a, c: uniq/i32 during a)
                        self.r = r
                        self.c = c
                struct TTD {a}
                    public var r: uniq/(uniq/i32 during a) during a
                    public var c: uniq/i32 during a
                    public init(r: uniq/(uniq/i32 during a) during a, c: uniq/i32 during a)
                        self.r = r
                        self.c = c
                    drop
                        self.r@follow@follow = 0
                struct KI
                    public var v: i32 = 0
                    public init() => ()
                    public func keepWith(self: uniq/Self, f: i32) -> uniq/KI during self => self
                func c23b(q: uniq/(uniq/i32 during a), s2: uniq/(uniq/i32 during a))
                    var q2 = q
                    let c = q2@follow@follow@uniq
                    q2 = s2
                    q@follow@follow = 5
                    inspect(c)
                func c34b(i: isize, s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var arr: [2 of uniq/(uniq/i32 during a)] = [t1@uniq, t2@uniq]
                    let c = arr[i]@follow@follow@uniq
                    arr[0] = t3@uniq
                    arr[i]@follow@follow = 7
                    c@follow = 1
                func c37()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let t = rr@follow@uniq
                    let c = t@follow@follow@uniq
                    rr = slot2@uniq
                    rr@follow@follow = 7
                    c@follow = 1
                func c50()
                    var t = 1
                    var u = 2
                    var s = U.init(p: t@uniq)
                    var o = U.init(p: u@uniq)
                    var q = s@uniq
                    let w = q@follow@ref
                    let c = q.p@follow@uniq
                    q = o@uniq
                    c@follow = 5
                    inspect(w.p@follow@ref)
                func c51()
                    var t = 1
                    var u = 2
                    var s = UV.init(p: t@uniq)
                    var o = UV.init(p: u@uniq)
                    var q = s@uniq
                    let c = q.p@follow@uniq
                    let w = q.view()
                    q = o@uniq
                    c@follow = 5
                    inspect(w.p@follow@ref)
                func c52()
                    var t = 1
                    var u = 2
                    var s = UV.init(p: t@uniq)
                    var o = UV.init(p: u@uniq)
                    var q = s@uniq
                    let w = q.view()
                    let c = q.p@follow@uniq
                    q = o@uniq
                    c@follow = 5
                    inspect(w.p@follow@ref)
                func c53()
                    var t = 1
                    var u = 2
                    var s = U.init(p: t@uniq)
                    var o = U.init(p: u@uniq)
                    var q = s@uniq
                    let c = q.p@follow@uniq
                    let w = keepT(q)
                    q = o@uniq
                    w.p@follow = 9
                    c@follow = 5
                func c54()
                    var x = 1
                    var y = 2
                    var x2 = 3
                    var y2 = 4
                    var s = AB.init(pa: x@uniq, pb: y@uniq)
                    var t = AB.init(pa: x2@uniq, pb: y2@uniq)
                    var q = s@uniq
                    let c1 = q.pa@follow@uniq
                    let c2 = q.pb@follow@uniq
                    q = t@uniq
                    q.pa@follow = 7
                    q.pb@follow = 8
                    c1@follow = 1
                    c2@follow = 2
                func c55()
                    var x = 1
                    var y = 2
                    var x2 = 3
                    var y2 = 4
                    var s = AB.init(pa: x@uniq, pb: y@uniq)
                    var t = AB.init(pa: x2@uniq, pb: y2@uniq)
                    var q = s@uniq
                    let c1 = q.pa@follow@uniq
                    let c2 = q.pb@follow@uniq
                    q = t@uniq
                    s.pa@follow = 9
                    c1@follow = 1
                    c2@follow = 2
                func c56()
                    var x = 1
                    var y = 2
                    var x2 = 3
                    var y2 = 4
                    var s = AB.init(pa: x@uniq, pb: y@uniq)
                    var t = AB.init(pa: x2@uniq, pb: y2@uniq)
                    var q = s@uniq
                    let c1 = q.pa@follow@uniq
                    let c2 = q.pa@follow@uniq
                    q = t@uniq
                    c1@follow = 1
                    c2@follow = 2
                func c57(cond: bool)
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    if cond => rr = slot2@uniq
                    rr@follow@follow = 7
                    c@follow = 1
                func c58(cond: bool)
                    var x = 1
                    var z = 3
                    var w = 4
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var slot3 = w@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    if cond => rr = slot2@uniq else => rr = slot3@uniq
                    rr@follow@follow = 7
                    c@follow = 1
                func c59(flag: bool)
                    var x = 1
                    var z = 3
                    var k = 0
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    var keep = k@uniq
                    while flag
                        let c = rr@follow@follow@uniq
                        keep@follow = 1
                        keep = c
                        rr = slot2@uniq
                func c60(flag: bool, p: bool)
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    while flag
                        c@follow = 1
                        if p => rr = slot2@uniq
                    rr@follow@follow = 7
                    c@follow = 2
                func c61(flag: bool)
                    var x = 1
                    var z = 3
                    var w = 4
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var slot3 = w@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr = slot2@uniq
                    while flag
                        rr@follow@follow = 7
                        c@follow = 1
                        rr = slot3@uniq
                    c@follow = 2
                func c62()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    stashU(rr@move)
                    rr = slot2@uniq
                    c@follow = 1
                func c63()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    let old = Kimi.Intrinsics.exchange(rr@uniq, with: slot2@uniq)
                    old@follow@follow = 7
                    c@follow = 1
                func c64()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    var rr2 = slot2@uniq
                    let c = rr@follow@follow@uniq
                    Kimi.Intrinsics.swap(rr@uniq, rr2@uniq)
                    rr2@follow@follow = 7
                    c@follow = 1
                func c65()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    Kimi.Intrinsics.replace(rr@uniq, with: slot2@uniq)
                    rr@follow@follow = 7
                    c@follow = 1
                func c66()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    Kimi.Intrinsics.replace(rr@follow@uniq, with: z@uniq)
                    rr@follow@follow = 7
                    c@follow = 1
                func c67(s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var arr: [2 of uniq/(uniq/i32 during a)] = [t1@uniq, t2@uniq]
                    let c = arr[0]@follow@follow@uniq
                    arr[0] = t3@uniq
                    arr[0]@follow@follow = 7
                    c@follow = 1
                func c68(s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var arr: [2 of uniq/(uniq/i32 during a)] = [t1@uniq, t2@uniq]
                    let c = arr[0]@follow@follow@uniq
                    arr[1] = t3@uniq
                    arr[0]@follow@follow = 7
                    c@follow = 1
                func c69(j: isize, s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var arr: [2 of uniq/(uniq/i32 during a)] = [t1@uniq, t2@uniq]
                    let c = arr[0]@follow@follow@uniq
                    arr[j] = t3@uniq
                    arr[0]@follow@follow = 7
                    c@follow = 1
                func c70(i: isize, j: isize, s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var arr: [2 of uniq/(uniq/i32 during a)] = [t1@uniq, t2@uniq]
                    let c = arr[i]@follow@follow@uniq
                    arr[j] = t3@uniq
                    arr[i]@follow@follow = 7
                    c@follow = 1
                func c71(i: isize, s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a, s4: uniq/i32 during a, s5: uniq/i32 during a, s6: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var t4: uniq/i32 during a = s4
                    var t5: uniq/i32 during a = s5
                    var t6: uniq/i32 during a = s6
                    var m: [2 of [2 of uniq/(uniq/i32 during a)]] = [[t1@uniq, t2@uniq], [t3@uniq, t4@uniq]]
                    let c = m[0][i]@follow@follow@uniq
                    m[0] = [t5@uniq, t6@uniq]
                    m[0][i]@follow@follow = 7
                    c@follow = 1
                func c72(i: isize, s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a, s4: uniq/i32 during a, s5: uniq/i32 during a, s6: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var t4: uniq/i32 during a = s4
                    var t5: uniq/i32 during a = s5
                    var t6: uniq/i32 during a = s6
                    var m: [2 of [2 of uniq/(uniq/i32 during a)]] = [[t1@uniq, t2@uniq], [t3@uniq, t4@uniq]]
                    let c = m[i][0]@follow@follow@uniq
                    m[0] = [t5@uniq, t6@uniq]
                    m[i][0]@follow@follow = 7
                    c@follow = 1
                func c73(s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var p = PA.init(pa: t1@uniq, pb: t2@uniq)
                    let c = p.pa@follow@follow@uniq
                    p.pa = t3@uniq
                    p.pa@follow@follow = 7
                    c@follow = 1
                func c74(s1: uniq/i32 during a, s2: uniq/i32 during a, s3: uniq/i32 during a)
                    var t1: uniq/i32 during a = s1
                    var t2: uniq/i32 during a = s2
                    var t3: uniq/i32 during a = s3
                    var p = PA.init(pa: t1@uniq, pb: t2@uniq)
                    let c = p.pa@follow@follow@uniq
                    p.pb = t3@uniq
                    p.pa@follow@follow = 7
                    c@follow = 1
                func c76()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var h = HR.init(r: slot@uniq)@obj
                    let c = h@follow.r@follow@follow@uniq
                    h = HR.init(r: slot2@uniq)@obj
                    h@follow.r@follow@follow = 7
                    c@follow = 1
                func c77()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var h = HR.init(r: slot@uniq)@obj
                    let c = h@follow.r@follow@follow@uniq
                    h = HR.init(r: slot2@uniq)@obj
                    slot@follow = 5
                    c@follow = 1
                func c78()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var h = Kimi.Intrinsics.makeRc(HR.init(r: slot@uniq))
                    let h2 = Kimi.Intrinsics.clone(h@ref)
                    let c = h@follow.r@follow@follow@ref
                    h = Kimi.Intrinsics.makeRc(HR.init(r: slot2@uniq))
                    inspect(h2@follow.r@follow@follow@ref)
                    inspect(c)
                func c79()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var h = Kimi.Intrinsics.makeRc(HR.init(r: slot@uniq))
                    let c = h@follow.r@follow@follow@ref
                    h = Kimi.Intrinsics.makeRc(HR.init(r: slot2@uniq))
                    slot@follow = 5
                    inspect(c)
                func c80()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var h = Kimi.Intrinsics.makeRc(HRD.init(r: slot@uniq))
                    let h2 = Kimi.Intrinsics.clone(h@ref)
                    let c = h@follow.r@follow@follow@ref
                    h = Kimi.Intrinsics.makeRc(HRD.init(r: slot2@uniq))
                    inspect(c)
                func c81(q: uniq/(uniq/i32 during a), out: uniq/(uniq/i32 during a), s2: uniq/(uniq/i32 during a))
                    var q2 = q
                    let c = q2@follow@follow@uniq
                    out@follow = c@move
                    q2 = s2
                    q@follow@follow = 5
                func c84() -> i32
                    var x = KI.init()
                    var z = KI.init()
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let res = rr@follow@follow.keepWith(label w: do
                        rr = slot2@uniq
                        exit to w 0
                    )
                    rr@follow@follow.v = 7
                    return res.v
                func c85()
                    var x = KI.init()
                    var z = KI.init()
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let res = rr@follow@follow.keepWith(label w: do
                        rr = slot2@uniq
                        exit to w 0
                    )
                    rr@follow@follow.v = 7
                func c86()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr = slot2@uniq
                    let d = c@follow@uniq
                    c@follow = 1
                    d@follow = 2
                func c87()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr = slot2@uniq
                    slot@follow = 5
                    c@follow = 1
                func c88(i: isize, j: isize)
                    var x = 1
                    var y = 2
                    var arr: Array<uniq/i32> = [x@uniq, y@uniq]
                    let c = arr[i]@follow@uniq
                    arr[j]@follow = 7
                    c@follow = 1
                func c89()
                    var x = 1
                    var y = 2
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var t = TT.init(r: slot@uniq, c: y@uniq)
                    t = TT.init(r: slot2@uniq, c: t.r@follow@follow@uniq)
                    t.r@follow@follow = 7
                    t.c@follow = 1
                func c90()
                    var x = 1
                    var y = 2
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var t = TT.init(r: slot@uniq, c: y@uniq)
                    t = TT.init(r: slot2@uniq, c: t.r@follow@follow@uniq)
                    slot@follow = 5
                    t.c@follow = 1
                func c91()
                    var x = 1
                    var y = 2
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var t = TTD.init(r: slot@uniq, c: y@uniq)
                    t = TTD.init(r: slot2@uniq, c: t.r@follow@follow@uniq)
                    t.c@follow = 1
                func c94()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr = slot2@uniq
                    let t = slot@follow@uniq
                    t@follow = 5
                    c@follow = 1
                """));

    // SPEC 15.6.2, 15.6.3, 15.6.7, 4.7.4 (prototype round 10: the rule designs' 38j–38k, 39g–39k, 40c–40h, 41c–41q, 42c and the design
    // reviews' 100c–127b): a reservation's route meets a write through an alias or another name, a Borrow past a shared or objref Deref keeps
    // only the Loans from that layer on, a pair layer is a borrow Deref only in its uniq view, a selection lends its receiver while the index is
    // evaluated, and a self-referential holder conflicts where it is borrowed, moved, stored or replaced.
    [Fact]
    public void TheRuleDesignCasesKeepTheirVerdicts()
        => Assert.Equal(
            [
                "c38j: cur.inner", "c38k: hh.inner = s2", "c39h: item", "c39ij: h", "c40g: refs[j] = s3 CallReservationConflict_Kd", "c41d: x = 5",
                "c41g: x = 5", "c41i: split(x@uniq, s.r@follow@ref) CallActivationConflict_Kd", "c41k: s.p = t2@uniq", "c41m: item.count = 5",
                "c41n: s.n = 5", "c41p: x = 5", "c42c: u", "c100c: r", "c105: x = 5",
                "c106: Kimi.Intrinsics.exchange(cur@uniq, with: b) CallReservationConflict_Kd", "c112: reset(values@uniq) CallActivationConflict_Kd",
                "c120: r@follow@uniq", "c122: stashT(h@uniq, r@follow@uniq) CallActivationConflict_Kd", "c124: r@follow@uniq", "c126: let d",
                "c127: s.n = 5", "c127: s.r@follow"
            ],
            Problems("""
                func gS(a: uniq/S, b: ref/i32) => ()
                func reset(v: uniq/[2 of i32]) -> isize => 0
                func peek2(v: ref/[2 of i32]) -> isize => 0
                struct HO {a}
                    public var inner: uniq/Inner during a
                    public var other: i32 = 0
                    public init(inner: uniq/Inner during a) => self.inner = inner
                struct NN
                    public var value: i32 = 0
                    public init() => ()
                    public func next(self: uniq/Self) -> uniq/NN during self => self
                struct HV {a}
                    public var view: objref/Data during a
                    public init(view: objref/Data during a) => self.view = view
                struct Hb {a}
                    public var a: i32 = 0
                    public var b: uniq/S{c} during a
                        origin c.a == a
                    public init(b: uniq/S{c} during a)
                        origin c.a == a
                        self.b = b
                struct HA {a}
                    public var a: i32 = 0
                    public var saved: ref/Data during a
                    public init(saved: ref/Data during a) => self.saved = saved
                func stashT<T>(into: uniq/(uniq/T during a), item: uniq/T during a) => into@follow = item
                func c38j(a: uniq/H, b: uniq/H) -> i32
                    var cur = a
                    let pc = cur@uniq
                    let first = cur.inner.takeWith(label w: do
                        pc@follow = b
                        exit to w 0
                    )
                    return first
                func c38k(s1: uniq/Inner during a, s2: uniq/Inner during a) -> i32
                    var hh = H.init(inner: s1)
                    var rr = hh@uniq
                    let first = rr.inner.takeWith(label w: do
                        hh.inner = s2
                        exit to w 0
                    )
                    return first
                func c39g()
                    var item = Data.init()@obj
                    var other = Data.init()@obj
                    var o = item@objref
                    let v = o.count@ref
                    o = other@objref
                    inspect(v)
                func c39h()
                    var item = Data.init()@obj
                    let o = item@objref
                    let v = o.count@ref
                    let t = item@move
                    inspect(v)
                func c39ij<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    T is Copy
                    var h = (p@move, 0)
                    let v = h.0@follow@ref
                    let t = h@move
                    inspect(v)
                func c39k()
                    var x = 1
                    var s = S.init(r: x@ref)
                    gS(s@uniq, s.r@follow@ref)
                func c40c(a: uniq/HO) -> i32
                    var cur = a
                    let first = cur.inner.takeWith(label w: do
                        cur.other = 5
                        exit to w 0
                    )
                    return first
                func c40d(a: uniq/HO) -> i32
                    var cur = a
                    let first = cur.inner.takeWith(label w: do
                        inspect(cur@ref)
                        exit to w 0
                    )
                    return first
                func c40e(a: uniq/HO, b: uniq/HO) -> i32
                    var cur = a
                    var cur2 = cur
                    let first = cur2.inner.takeWith(label w: do
                        cur = b
                        exit to w 0
                    )
                    return first
                func c40f(head: uniq/NN)
                    var node = head
                    node = node.next()
                    node.value = 1
                func c40g(i: isize, j: isize, s1: uniq/Inner during a, s2: uniq/Inner during a, s3: uniq/Inner during a) -> i32
                    var refs: [2 of uniq/Inner during a] = [s1, s2]
                    return refs[i]@follow.takeWith(label w: do
                        refs[j] = s3
                        exit to w 0
                    )
                func c40h(s1: uniq/Inner during a, s2: uniq/Inner during a, s3: uniq/Inner during a) -> i32
                    var refs: [2 of uniq/Inner during a] = [s1, s2]
                    return refs[0]@follow.takeWith(label w: do
                        refs[1] = s3
                        exit to w 0
                    )
                func c41c()
                    var x = 1
                    var s = S.init(r: x@ref)
                    var p = s@uniq
                    let v = p.r@follow@ref
                    let t = s@move
                    inspect(v)
                func c41d()
                    var x = 1
                    var y = 2
                    var s = S.init(r: x@ref)
                    var p = s@uniq
                    let v = p.r@follow@ref
                    p.r = y@ref
                    x = 5
                    inspect(v)
                    inspect(p)
                func c41e()
                    var x = 1
                    var y = 2
                    var s = S.init(r: x@ref)
                    var p = s@uniq
                    let v = p.r@follow@ref
                    p.r = y@ref
                    inspect(v)
                    inspect(p)
                func c41f()
                    var x = 1
                    var y = 2
                    var r = x@ref
                    let v = r@follow@ref
                    let w = r@uniq
                    w@follow = y@ref
                    inspect(v)
                func c41g()
                    var x = 1
                    var y = 2
                    var r = x@ref
                    let v = r@follow@ref
                    let w = r@uniq
                    w@follow = y@ref
                    x = 5
                    inspect(v)
                func c41i()
                    var x = 1
                    var s = S.init(r: x@ref)
                    split(x@uniq, s.r@follow@ref)
                func c41k()
                    var t: Array<i32> = [1, 2, 3]
                    var t2: Array<i32> = [5]
                    var s = T39.init(p: t@uniq)
                    let q = s@ref
                    let e = q.p@follow@ref
                    s.p = t2@uniq
                    inspect(e@follow)
                func c41l()
                    var item = Data.init()@obj
                    var h = HV.init(view: item@objref)
                    let f = h.view@follow.count@ref
                    let g = h@move
                    inspect(f)
                func c41m()
                    var item = Data.init()@obj
                    var h = HV.init(view: item@objref)
                    let f = h.view@follow.count@ref
                    item.count = 5
                    inspect(f)
                func c41n()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    let v = s.r@follow@ref
                    s.n = 5
                    inspect(v)
                func c41p(flag: bool)
                    var x = 1
                    var y = 2
                    var z = 0
                    var r = x@ref
                    var keep = z@ref
                    while flag
                        keep = r@follow@ref
                        r = y@ref
                    x = 5
                    inspect(keep)
                func c41q()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    let q = s@ref
                    inspect(q.r@follow@ref)
                func c42c(i: isize, j: isize)
                    var arr: Array<i32> = [1, 2, 3]
                    var u = arr.sliceUniq()
                    let m = u[i]@uniq
                    inspect(u[j]@ref)
                    m@follow = 1
                func c100c()
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    let q = r@move
                    q.n = 5
                    inspect(q.r@follow@ref)
                func c105(i: isize)
                    var x = 1
                    var y = 2
                    var arr: [2 of ref/i32] = [x@ref, y@ref]
                    var s = arr[..]
                    let e = s[i]
                    x = 5
                    inspect(e)
                func c106(a: uniq/H, b: uniq/H) -> i32
                    var cur = a
                    let first = cur.inner.takeWith(label w: do
                        let old = Kimi.Intrinsics.exchange(cur@uniq, with: b)
                        exit to w 0
                    )
                    return first
                func c112() -> i32
                    var values: [2 of i32] = [1, 2]
                    return values[reset(values@uniq)]
                func c112b() -> i32
                    var values: [2 of i32] = [1, 2]
                    return values[peek2(values@ref)]
                func c120()
                    var z = 0
                    var w = 0
                    var s = S.init(r: z@ref)
                    var s2 = S.init(r: w@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    var h = Hb.init(b: s2@uniq)
                    let p = h.a@ref
                    h.b = r@follow@uniq
                    inspect(p@follow@ref)
                func c122()
                    var z = 0
                    var w = 0
                    var s = S.init(r: z@ref)
                    var s2 = S.init(r: w@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    var h = s2@uniq
                    stashT(h@uniq, r@follow@uniq)
                    h@follow.n = 5
                    inspect(h@follow.r@follow@ref)
                func c124(flag: bool)
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    while flag
                        let q = r@follow@uniq
                        q.n = 5
                        inspect(q.r@follow@ref)
                        r.r = r.n@ref
                func c125(flag: bool)
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    while flag
                        let q = r@follow@ref
                        inspect(q.r@follow@ref)
                        r.r = r.n@ref
                func c126()
                    let fallback = Data.init()
                    let packet: Packet = .Data(Data.init())
                    var holder = HA.init(saved: fallback@ref)
                    let p = holder.a@ref
                    match packet
                        .Data(let d) if (label g: do
                            holder.saved = d
                            exit to g true
                        ) => ()
                        _ => ()
                    inspect(p)
                func c127()
                    var z = 0
                    var s = S.init(r: z@ref)
                    var s2 = S.init(r: z@ref)
                    var r = s@uniq
                    r.r = r.n@ref
                    r = s2@uniq
                    s.n = 5
                    let v = s.r@follow
                func c127b()
                    var z = 0
                    var s = S.init(r: z@ref)
                    var s2 = S.init(r: z@ref)
                    var r = s@uniq
                    r.r = r.n@ref
                    r = s2@uniq
                    s.n = 5
                """));

    // SPEC 15.6.7, 7.3.3 (G92b's fold, DECISIONS R4-M52; prototype 38d, 38l–38n): on a reserved input's path a written exclusive borrow
    // the path continues through is not formed, so the input has one reservation at its lending point: a shared read or Loan of a sibling
    // during it is valid, a live Loan of the reserved Place conflicts at the activation, and a write of the reference the path follows
    // reaches the reservation.
    [Fact]
    public void AFoldedInputHasOneReservation()
        => Assert.Equal(
            [
                "f38d: h = y@uniq CallReservationConflict_Kd", "f38n: holder@uniq.items.append(1) CallActivationConflict_Kd"
            ],
            Problems("""
                struct Bag
                    public var items: Array<i32> = []
                    public var count: i32 = 0
                    public init() => ()
                struct Box2
                    public var c: Inner = Inner.init()
                    public init() => ()
                func f38d()
                    var x = Box2.init()
                    var y = Box2.init()
                    var h = x@uniq
                    let n = h@follow@uniq.c.takeWith(label w: do
                        h = y@uniq
                        exit to w 1
                    )
                func f38l()
                    var holder = Bag.init()
                    holder@uniq.items.append(holder.count)
                func f38m()
                    var holder = Bag.init()
                    let r = holder.count@ref
                    holder@uniq.items.append(1)
                    inspect(r)
                func f38n()
                    var holder = Bag.init()
                    let v = holder.items@ref
                    holder@uniq.items.append(1)
                    inspect(v)
                """));
}
