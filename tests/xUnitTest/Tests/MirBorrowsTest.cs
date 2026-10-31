// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>A5's Loan stratum on MIR (SPEC 15.6; design A3 with its 12 errata; docs/dev/DECISIONS.md R4-M57): each rule with valid and
/// invalid programs, through the problems <see cref="MirBorrows"/> adds over every body view of a built program, each as its function and
/// the first line of its subject.</summary>
public sealed partial class MirBorrowsTest(ITestOutputHelper output)
{
    private const string Prelude = """
        func inspect<T>(value: ref/T) => ()
        func pin(slot: ref/(ref/i32 during a)) -> ref/i32 during slot => slot@follow
        func keep(value: ref/i32) -> ref/i32 during value => value
        func put(slot: uniq/(ref/i32 during a), value: ref/i32 during a) => slot@follow = value
        func put4(slot: uniq/(uniq/(uniq/(uniq/(ref/i32 during a) during c) during d) during e), value: ref/i32 during a) => slot@follow@follow@follow@follow = value
        func size(value: ref/i32) -> i32 => value@follow
        func two(a: uniq/Pair, b: ref/Pair) => ()
        func both(a: ref/Pair, b: ref/Pair) => ()
        func split(a: uniq/i32, b: ref/i32) => ()
        func setN(s: uniq/S, n: i32) => s.n = n
        func setM(m: uniq/i32) => m@follow = 1
        func keepT<T>(value: uniq/T) -> uniq/T during value => value
        func give(a: uniq/i32, b: i32) => ()
        func stop() -> bool => false
        func save(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool => true
        struct Pair
            public var a: i32 = 1
            public var b: i32 = 2
            public init() => ()
        struct S {a}
            public var r: ref/i32 during a
            public var n: i32 = 0
            public var m: i32 = 0
            public init(r: ref/i32 during a) => self.r = r
        struct S3 {a}
            public var rs: ref/(uniq/i32 during a) during a
            public init(rs: ref/(uniq/i32 during a) during a) => self.rs = rs
        struct U {a}
            public var p: uniq/i32 during a
            public init(p: uniq/i32 during a) => self.p = p
        struct T39 {o}
            public var p: uniq/Array<i32> during o
            public init(p: uniq/Array<i32> during o) => self.p = p
        struct Task
            public var value: i32
            public init(value: i32) => self.value = value
        struct Inner
            public var v: i32 = 0
            public init() => ()
            public func takeWith(self: uniq/Self, f: i32) -> i32 => f
        struct H {a}
            public var inner: uniq/Inner during a
            public init(inner: uniq/Inner during a) => self.inner = inner
        struct D {a}
            public var r: uniq/i32 during a
            public init(r: uniq/i32 during a) => self.r = r
            drop
                self.r@follow = 0
        struct C {a}
            public var r: uniq/Array<i32> during a
            public init(r: uniq/Array<i32> during a) => self.r = r
            drop
                self.r@follow.clear()
        struct Q {a}
            public var r: uniq/i32 during a
            public var q: ref/i32 during a
            public init(r: uniq/i32 during a, q: ref/i32 during a)
                self.r = r
                self.q = q
            drop
                self.r@follow = 0
        struct R {a}
            public var r: ref/i32 during a
            public init(r: ref/i32 during a) => self.r = r
            drop => ()
        struct Tree
            public var value: i32 = 0
            public var children: Array<Tree> = []
            public init() => ()
            public func child(self: uniq/Self, i: i32) -> uniq/Tree during self => self.children[i]@uniq
        struct Data
            public var value: i32 = 0
            public var count: i32 = 0
            public init() => ()
        enum Packet {a}
            Data(Data)
            Link(ref/Data during a)
            Empty
        struct Holder {a}
            public var saved: ref/Data during a
            public init(saved: ref/Data during a) => self.saved = saved
        enum Choice
            Data(i32)
            Empty
        struct Nest<T>
            public var value: T
            public var next: Option<rc/Nest<(T, T)>> = .None
            public init(value: T) => self.value = value

        """;

    // The callees and Types of the rows on the values an access takes (A3 E13): by-value and named-argument callees, pair-layer callees,
    // a Copy struct with a by-value receiver, a setter, an unbound reference's receiver declared after a parameter, and holders that
    // refer to their own parts.
    private const string Order = """
        func give2(a: i32, b: i32) => ()
        func give2T<T>(a: T, b: i32) => ()
        func giveR(a: ref/i32, b: i32) => ()
        func giveS(a: uniq/i32, b: ref/i32) => ()
        func peek<T>(x: ref/T) -> i32 => 1
        func runF<F>(f: F, n: i32) => ()
        func inner11<s/T>(a: s/T, b: i32) -> ()
            s is ref or uniq
            return
        func inner2<s/T>(a: ref/(s/T), b: s/T) -> ()
            s is ref or uniq
            return
        func inner3<s/T>(a: s/T, b: ref/(s/T)) -> ()
            s is ref or uniq
            return
        func inner9<s/T>(a: s/T, b: s/T) -> ()
            s is ref or uniq
            return
        func innerR<s/T>(a: s/T, b: ref/T) -> ()
            s is ref or uniq
            return
        struct SU {a}
            public var r: uniq/i32 during a
            public var n: i32 = 0
            public init(r: uniq/i32 during a) => self.r = r
        struct Q2 {a}
            public var r: uniq/i32 during a
            public var n: i32 = 0
            public init(r: uniq/i32 during a) => self.r = r
            drop
                self.r@follow = 0
        struct S7 {a}
            public var r: uniq/i32 during a
            public var q: ref/i32 during a
            public init(r: uniq/i32 during a, q: ref/i32 during a)
                self.r = r
                self.q = q
        struct S5 {a}
            public var r: ref/i32 during a
            public init(r: ref/i32 during a) => self.r = r
        struct W5 {a}
            public var p: uniq/S5{b} during a
                origin b.a == a
            public var q: uniq/(ref/i32 during a) during a
            public init(p: uniq/S5{b} during a, q: uniq/(ref/i32 during a) during a)
                origin b.a == a
                self.p = p
                self.q = q
        struct Gauge
            public var level: i32 = 0
                set(value: i32) -> ()
                    storage = value
            public init() => ()
        struct Box
            public var v: i32 = 0
            public init() => ()
            public func f(a: ref/Box, self: ref/Self) => ()
        struct Num
            Self is Copy
            public var v: i32 = 0
            public init() => ()
            public func plus(self: Self, o: Num) -> i32 => 1

        """;

    // The callees of the rows on a call's stores (A3 E16): stores through two and three exclusive layers, a raw pointer and a Field, from
    // one part's reference into another, out of a taken-out layer-1 reference into its own storage, a depth-3 sibling copy; callees that
    // store nothing; loops over an exclusive parameter and a Field, then an append.
    private const string Stores = """
        func put2(slot: uniq/(uniq/(ref/i32 during a) during c), value: ref/i32 during a) => slot@follow@follow = value
        func put3(slot: uniq/(uniq/(uniq/(ref/i32 during a) during c) during d), value: ref/i32 during a) => slot@follow@follow@follow = value
        func putRaw(slot: uniq/(raw/(ref/i32 during a)), value: ref/i32 during a)
            unsafe => slot@follow[0] = value
        func moveIn(pair: uniq/(uniq/(ref/i32 during a) during c, ref/i32 during a)) => pair@follow.0@follow = pair@follow.1
        func keep4(slot: uniq/(ref/(ref/i32 during a) during c), value: ref/i32 during a) => ()
        func peek2(slot: uniq/(uniq/(ref/i32 during q) during c), n: i32 = 1) => ()
        struct Box2<T> {c}
            public var r: uniq/T during c
            public init(r: uniq/T during c) => self.r = r
        func putBox(box: uniq/(Box2<ref/i32 during a> during c), value: ref/i32 during a) => box.r@follow = value
        func takeOut(p: uniq/(Option<uniq/(S during a) during a>) during d)
            match Kimi.Intrinsics.exchange(p@follow@uniq, with: .None)
                .Some(let q) => q.r = q.n@ref
                .None => ()
        func keepOut(p: uniq/(Option<uniq/(S during a) during a>) during d) => ()
        func copyOver(q: uniq/(uniq/(ref/i32 during a) during c, uniq/(ref/i32 during a) during c))
            let v: ref/i32 during a = q@follow.1@follow
            q@follow.0@follow = v
        func copyDeep(p: uniq/(uniq/(uniq/(ref/i32 during a) during c, uniq/(ref/i32 during a) during c) during e) during f)
            copyOver(p@follow@follow@uniq)
        func bumpAll(values: uniq/Array<i32>)
            for v in values
                v@follow = v@follow + 1
            values.append(0)
        struct Bag
            public var items: Array<i32> = []
            public init() => ()
            public func bump(self: uniq/Self)
                for v in self.items
                    _ = v@follow
                self.items.append(0)

        """;

    /// <summary>Gets the variants of the SPEC examples these rules decide: family F3's, SPEC 14.8.3's, 15.6.6's and 15.6.7's, and
    /// 15.6.4's destruction retention (PLAN G264 U1).</summary>
    public static TheoryData<string> SpecVariants => [.. DiagnosticCorpus.SpecCases
        .Where(static x => x.Family == "F3" || x.Section is "14.8.3" or "15.6.6" or "15.6.7" || x.Name == "15.6.4-drop-retention")
        .SelectMany(static x => x.Variants.Select((_, i) => $"{x.Name}/{i}"))];

    // The codes of A5's Loan stratum (A3 5's A6 table, the rows its rules decide).
    private static DiagnosticCode[] LoanCodes => [DiagnosticCode.ComparisonLoanConflict_Kd, DiagnosticCode.CallReservationConflict_Kd,
        DiagnosticCode.CallActivationConflict_Kd, DiagnosticCode.PlacementActivationConflict_Kd];

    // SPEC 15.6, 15.6.2: an access conflicts with a Loan live after it that it hits: a write, a Move or an exclusive borrow with any Loan
    // of an overlapping Place, a read or a shared borrow with an exclusive one; Loans end with their last use, and distinct Fields are
    // disjoint.
    [Fact]
    public void AnAccessConflictsWithALiveLoanItHits()
        => Assert.Equal(
            ["sharedThenWrite: x = 2", "readUnderUnique: x", "twoUnique: x@uniq", "sameField: p.a = 3", "moveWhileShared: s"],
            Problems("""
                func sharedThenWrite()
                    var x = 1
                    let r = x@ref
                    x = 2
                    inspect(r)
                func writeAfterLastUse()
                    var x = 1
                    let r = x@ref
                    inspect(r)
                    x = 2
                func readUnderShared()
                    var x = 1
                    let r = x@ref
                    let y = x
                    inspect(r)
                func readUnderUnique()
                    var x = 1
                    let r = x@uniq
                    let y = x
                    inspect(r)
                func twoUnique()
                    var x = 1
                    let a = x@uniq
                    let b = x@uniq
                    inspect(a)
                func disjointFields()
                    var p = Pair.init()
                    let r = p.a@uniq
                    p.b = 3
                    inspect(r)
                func sameField()
                    var p = Pair.init()
                    let r = p.a@uniq
                    p.a = 3
                    inspect(r)
                func moveWhileShared()
                    let s = "text"
                    let r = s@ref
                    let t = s@move
                    inspect(r)
                """));

    // SPEC 4.3.3, 4.4.2, 15.6 (DECISIONS R4-M81): a fill holds its value's Loans, at a generic length (which may be positive), at a
    // constant one and at length 0 (the owner's Q2: the value is acquired once even then), so a write while the array lives conflicts
    // (fail-before: the generic fill was an Opaque value and the zero-length one had no operand, neither a problem).
    [Fact]
    public void AFillCarriesItsValuesLoans()
        => Assert.Equal(
            ["generic: x = 2", "constant: x = 2", "zero: x = 2"],
            Problems("""
                func generic<length N>()
                    var x = 1
                    let a = [N of x@ref]
                    x = 2
                    let b = a
                func constant()
                    var x = 1
                    let a = [3 of x@ref]
                    x = 2
                    let b = a
                func zero()
                    var x = 1
                    let a = [0 of x@ref]
                    x = 2
                    let b = a
                func afterLastUse()
                    var x = 1
                    let a = [3 of x@ref]
                    let b = a
                    x = 2
                """));

    // SPEC 15.1.6, 15.6 (DECISIONS R5-M38): a pattern binding of a Never payload borrows the subject in place and keeps that Loan, so a
    // write to the subject while the binding lives conflicts, in an arm that never runs too.
    [Fact]
    public void ANeverPayloadBindingKeepsItsLoan()
        => Assert.Equal(
            ["written: s = NeverErr.Ok(1)"],
            Problems("""
                enum NeverErr
                    Ok(i32)
                    Err(Never)
                func written(r: NeverErr) -> i32
                    var s = r@move
                    return match s
                        .Ok(let v) => v
                        .Err(let e)
                            s = NeverErr.Ok(1)
                            _ = e
                            yield 0
                func unwritten(r: NeverErr) -> i32
                    var s = r@move
                    return match s
                        .Ok(let v) => v
                        .Err(let e)
                            _ = e
                            yield 0
                """));

    // SPEC 14.9, 14.10.3 (F10): Loans flow forward over every path, joined at heads, around loops into the next iteration, and into code
    // only the checks reach; a holder reassigned before its next use carries nothing across.
    [Fact]
    public void LoansFlowOverJoinsLoopsAndCheckOnlyCode()
        => Assert.Equal(
            ["loopCarry: x = 3", "joined: x = 3", "afterReturn: x = 2"],
            Problems("""
                func loopCarry(flag: bool)
                    var x = 1
                    var y = 2
                    var r = y@ref
                    while flag
                        x = 3
                        inspect(r)
                        r = x@ref
                func loopReassigned(flag: bool)
                    var x = 1
                    var y = 2
                    var r = y@ref
                    while flag
                        inspect(r)
                        x = 3
                        r = x@ref
                func joined(ready: bool)
                    var x = 1
                    var y = 2
                    let r = if ready => x@ref else => y@ref
                    x = 3
                    inspect(r)
                func afterReturn() -> i32
                    var x = 1
                    let r = x@ref
                    return 1
                    x = 2
                    inspect(r)
                    return 2
                """));

    // SPEC 15.6.3 (R2): a Reborrow carries the Loans of every reference it follows, so its parent's Place stays in use while it lives; a
    // read through the parent is an access of the child's Place; a parameter's referent is storage the body does not own (Φ_E).
    [Fact]
    public void AReborrowCarriesItsParents()
        => Assert.Equal(
            ["parentRead: x", "readThroughParent: h@follow + 1", "parentWrite: h@follow = 5", "parameterReborrow: p@follow = 5", "parameterField: p.n = 3"],
            Problems("""
                func parentRead()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    let v = x
                    c@follow = 2
                func parentReadAfter()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    c@follow = 2
                    let v = x
                func readThroughParent()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    let y = h@follow + 1
                    c@follow = 2
                func copiedThrough()
                    var x = 1
                    let h = x@ref
                    let y = h@follow + 1
                    x = 3
                    inspect(y)
                func parentWrite()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    h@follow = 5
                    c@follow = 2
                func parameterReborrow(p: uniq/i32)
                    let c = p@follow@uniq
                    p@follow = 5
                    c@follow = 2
                func parameterReborrowEnded(p: uniq/i32)
                    let c = p@follow@uniq
                    c@follow = 2
                    p@follow = 5
                func parameterField(p: uniq/S)
                    let v = p.n@ref
                    p.n = 3
                    inspect(v)
                """));

    // SPEC 15.6, 15.6.4 (A3's transit, 25a, 25b, 29c): a value a location stores into a holder live after it is checked with its access,
    // so storing what borrows the destination into it is an error, unless the destination is dead; a write through a reference stores
    // into the holders its Loans name (P10). `pin`'s result is `ref/i32 during slot`, which can hold neither its argument's own value nor
    // a borrow formed through it (`**slot` lies past a second shared layer, SPEC 15.6.3), so it keeps only what a read through the
    // argument carries, never the argument's Loan, and selfStore and partStore store nothing borrowing their destination (the owner's
    // 「SPEC 15.6.4 の読み (iii) と (β)」, DECISIONS R4-M154; dev kept the Loan by the result's Origin).
    [Fact]
    public void AStoredValueIsCheckedInTransit()
        => Assert.Equal(
            ["throughStore: x = 3"],
            Problems("""
                func selfStore(y: ref/i32)
                    var x = y
                    x = pin(x@ref)
                    inspect(x)
                func selfStoreDead(y: ref/i32)
                    var x = y
                    x = pin(x@ref)
                func partStore()
                    var x = 1
                    var s = S.init(r: x@ref)
                    s.r = pin(s.r@ref)
                    inspect(s)
                func partStoreDead()
                    var x = 1
                    var s = S.init(r: x@ref)
                    s.r = pin(s.r@ref)
                func throughStore()
                    var x = 1
                    var y = 2
                    var slot = y@ref
                    let p = slot@uniq
                    p@follow = x@ref
                    x = 3
                    inspect(slot)
                func throughStoreEnded()
                    var x = 1
                    var y = 2
                    var slot = y@ref
                    let p = slot@uniq
                    p@follow = x@ref
                    inspect(slot)
                    x = 3
                """));

    // SPEC 15.6 (Holders, DECISIONS R4-M76 (a)): storage the body does not own follows its holder as Loans do. A value read from a part of
    // a holder that holds a parameter reference derives from it, whichever part holds it, so what a store through that part writes may be
    // used after every point (tupleLocal, tupleOwned; tupleParameter stores through the parameter reference itself). A write through a part
    // reaches every holder that the holder's Loans name (tupleSibling). A Borrow of a local holder writes into that holder (direct), and
    // a holder whose parts are all body-owned keeps the store exact (tupleOwnedOnly).
    [Fact]
    public void StorageTheBodyDoesNotOwnFollowsItsHolder()
        => Assert.Equal(
            ["tupleLocal: v@follow = 9", "tupleParameter: v@follow = 9", "tupleSibling: v@follow = 9", "tupleOwned: v@follow = 9"],
            Problems("""
                func direct<s/T>(value: s/T, other: s/T, x: T)
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var slot = other@move
                    var link = slot@uniq
                    let child = v
                    link@follow = child@move
                    _ = slot@follow
                    v@follow = x
                func tupleLocal(value: uniq/i32 during q, w: uniq/i32 during q, w2: uniq/(uniq/i32 during q) during p)
                    var v = value@move
                    var local = w@move
                    var t = (local@uniq, w2@move)
                    let child = v
                    t.0@follow = child@move
                    _ = local@follow
                    v@follow = 9
                func tupleParameter(value: uniq/i32 during q, w: uniq/i32 during q, w2: uniq/(uniq/i32 during q) during p)
                    var v = value@move
                    var local = w@move
                    var t = (local@uniq, w2@move)
                    let child = v
                    t.1@follow = child@move
                    _ = local@follow
                    v@follow = 9
                func tupleSibling(value: uniq/i32 during q, w: uniq/i32 during q)
                    var v = value@move
                    var local = w@move
                    var other: i32 = 3
                    var loc2 = other@uniq
                    var t = (local@uniq, loc2@uniq)
                    let child = v
                    t.0@follow = child@move
                    _ = local@follow
                    v@follow = 9
                    _ = loc2@follow
                func tupleOwned(value: uniq/i32 during q, w2: uniq/(uniq/i32 during q) during p)
                    var v = value@move
                    var n2: i32 = 0
                    var local = n2@uniq
                    var t = (local@uniq, w2@move)
                    let child = v
                    t.0@follow = child@move
                    _ = local@follow
                    v@follow = 9
                func tupleOwnedOnly(value: uniq/i32 during q)
                    var v = value@move
                    var n2: i32 = 0
                    var n3: i32 = 0
                    var local = n2@uniq
                    var local3 = n3@uniq
                    var t = (local@uniq, local3@uniq)
                    let child = v
                    t.0@follow = child@move
                    _ = local@follow
                    v@follow = 9
                """));

    // SPEC 15.6.4: a call's result and the storage it writes carry the Loans of the inputs its contract lets them retain (TyCtxt.Retains),
    // and nothing else.
    [Fact]
    public void ACallCarriesWhatItRetains()
        => Assert.Equal(
            ["retained: x = 2", "stored: x = 3"],
            Problems("""
                func retained()
                    var x = 1
                    let r = keep(x@ref)
                    x = 2
                    inspect(r)
                func notRetained()
                    var x = 1
                    let n = size(x@ref)
                    x = 2
                    inspect(n)
                func stored()
                    var x = 1
                    var y = 2
                    var slot = y@ref
                    put(slot@uniq, x@ref)
                    x = 3
                    inspect(slot)
                """));

    // SPEC 8.11.3 rule 1 (the adopted proposal Abstract Calls Reach by Paths): an abstract call reaches its inputs' Places by path, as a
    // concrete call does, so an earlier result kept across a later call of the same or another value is no conflict; a LendingIterator
    // item that names `step` keeps the receiver Loan of its call, which the next call meets.
    [Fact]
    public void AnAbstractCallReachesByPaths()
        => Assert.Equal(
            ["lend: it"],
            Problems("""
                contract Take
                    associate Item
                    func take(self: uniq/Self) -> Option<Self.Item>
                func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
                    S is Take
                    let first = source.take()
                    let second = source.take()
                    return (first@move, second@move)
                func zip<I>(left: uniq/I, right: uniq/I) -> (Option<I.Item>, Option<I.Item>)
                    I is Iterator
                    let a = right.next()
                    let b = left.next()
                    return (a@move, b@move)
                func lend<L>(it: uniq/L)
                    L is LendingIterator
                    let first = it.next()
                    let second = it.next()
                    inspect(first)
                """));

    // SPEC 15.6.4 (DECISIONS BU-1): a value stored into writable storage brings its whole Type, so Array<rc/S>.append stores the borrow
    // its element's payload holds into the Array, below the rc layer: a write of the borrowed Place while the Array is used later errs; a
    // read, or a write before the append, does not.
    [Fact]
    public void ACallStoresBelowAnRcLayer()
        => Assert.Equal(
            ["appended: x = 2"],
            Problems("""
                func appended()
                    var x = 1
                    var e: Array<rc/S> = []
                    e.append(Kimi.Intrinsics.makeRc(S.init(r: x@ref)))
                    x = 2
                    inspect(e@ref)
                func appendedRead()
                    var x = 1
                    var e: Array<rc/S> = []
                    e.append(Kimi.Intrinsics.makeRc(S.init(r: x@ref)))
                    inspect(x@ref)
                    inspect(e@ref)
                func writtenBefore()
                    var x = 1
                    var e: Array<rc/S> = []
                    x = 2
                    e.append(Kimi.Intrinsics.makeRc(S.init(r: x@ref)))
                    inspect(e@ref)
                """));

    // SPEC 15.6, 15.6.2, 15.6.4 (A3 E16, DECISIONS R4-M73; PLAN G148): a call's store reaches every holder its argument reaches, past
    // each exclusive layer, a struct's Field, a raw pointer to a local's storage (E) and a parameter's referent (E), as its direct twin's
    // does, so a write of the stored borrow's Place while the route is used errs: through two or three layers, a Box2's Field, a raw
    // pointer, a moved parameter's route and one part's reference into another (acrossLayers); a callee that takes the layer-1 reference
    // out and stores a borrow of its own storage into it (takenOut, the design review's s1) or stores nothing, as its contract lets it
    // (keptOut, s1c); a depth-3 sibling copy (siblingCopy, s2e), a swap (swapped, n1) and a local's borrow stored through a parameter's
    // route (escapes, v1: b's end). Each was accepted while a store reached only refs(val(a_j)). A route's own Loan the store returns to
    // its holder errs at the holder's next use (inspect(w)), as SPEC 15.6.4 decides per argument. Valid: a shared reborrow's parent past
    // the guard (sharedParent), reads through the argument's own holder (peeked, peekedGiven), a loop over an exclusive parameter or a Field
    // then an append (bumped, bagged: their elements hold no Loan) and Milestone24's writer, whose Loan<uniq/u8> refers to Loan-free
    // storage (the guard stops at it).
    [Fact]
    public void ACallStoresIntoEveryHolderItsArgumentReaches()
        => Assert.Equal(
            [
                "twoLayers: b = 3", "twoLayers: inspect(w)", "threeLayers: b = 3", "threeLayers: inspect(w)", "hiddenLayer: b = 3",
                "hiddenLayer: inspect(w)", "rawPointee: b = 3", "parameterRoute: b = 3", "acrossLayers: z = 5", "acrossLayers: inspect(w)",
                "takenOut: s.n = 5", "takenOut: s.r@follow", "keptOut: s.n = 5", "keptOut: s.r@follow", "siblingCopy: x2 = 5",
                "siblingCopy: w1@follow", "swapped: w2 = x1@ref", "swapped: w1@follow", "escapes: var b = 2",
            ],
            Problems(Stores + """
                func twoLayers()
                    var x = 1
                    var b = 2
                    var w = x@ref
                    var s = w@uniq
                    put2(s@uniq, b@ref)
                    b = 3
                    inspect(w)
                func threeLayers()
                    var x = 1
                    var b = 2
                    var w = x@ref
                    var s = w@uniq
                    var t = s@uniq
                    put3(t@uniq, b@ref)
                    b = 3
                    inspect(w)
                func hiddenLayer()
                    var x = 1
                    var b = 2
                    var w = x@ref
                    var h = Box2<ref/i32>.init(r: w@uniq)
                    putBox(h@uniq, b@ref)
                    b = 3
                    inspect(w)
                func rawPointee()
                    var x = 1
                    var b = 2
                    var w = x@ref
                    var p = w@raw
                    putRaw(p@uniq, b@ref)
                    b = 3
                    unsafe => inspect(p[0])
                func parameterRoute(q: uniq/(ref/i32 during a))
                    var b = 2
                    var s = q@move
                    put2(s@uniq, b@ref)
                    b = 3
                func acrossLayers()
                    var x = 1
                    var z = 3
                    var w = x@ref
                    var t = (w@uniq, z@ref)
                    moveIn(t@uniq)
                    z = 5
                    inspect(w)
                func takenOut()
                    var z = 0
                    var s = S.init(r: z@ref)
                    var o: Option<uniq/S> = .Some(s@uniq)
                    takeOut(o@uniq)
                    s.n = 5
                    inspect(s.r@follow)
                func keptOut()
                    var z = 0
                    var s = S.init(r: z@ref)
                    var o: Option<uniq/S> = .Some(s@uniq)
                    keepOut(o@uniq)
                    s.n = 5
                    inspect(s.r@follow)
                func siblingCopy()
                    var x1 = 1
                    var x2 = 2
                    var w1 = x1@ref
                    var w2 = x2@ref
                    var u = (w1@uniq, w2@uniq)
                    var s = u@uniq
                    copyDeep(s@uniq)
                    x2 = 5
                    inspect(w1@follow)
                func swapped()
                    var x1 = 1
                    var x2 = 2
                    var w1 = x1@ref
                    var w2 = x2@ref
                    var s1 = w1@uniq
                    var s2 = w2@uniq
                    Kimi.Intrinsics.swap(s1@uniq, s2@uniq)
                    w2 = x1@ref
                    inspect(w1@follow)
                func escapes(q: uniq/(ref/i32 during a))
                    var b = 2
                    var t = (q@move, b@ref)
                    moveIn(t@uniq)
                func sharedParent()
                    var x = 1
                    var b = 2
                    var w = x@ref
                    var s = w@uniq
                    var r = s@follow@ref
                    keep4(r@uniq, b@ref)
                    b = 3
                    inspect(w)
                func peeked()
                    var x = 1
                    var w = x@ref
                    var s = w@uniq
                    peek2(s@uniq)
                    inspect(s@follow@follow)
                    inspect(s@follow@follow)
                func peekedGiven()
                    var x = 1
                    var w = x@ref
                    var s = w@uniq
                    peek2(s@uniq, 2)
                    inspect(s@follow@follow)
                    inspect(s@follow@follow)
                func bumped()
                    var a: Array<i32> = []
                    a.append(1)
                    bumpAll(a@uniq)
                    inspect(a)
                func bagged()
                    var b = Bag.init()
                    b.bump()
                func written()
                    var bytes = [64 of 0@u8]
                    var buffer = Text.fixed(bytes@uniq)
                    var writer = Text.writer(buffer@uniq)
                    _ = $tryWrite(writer@uniq, "x")
                    _ = buffer.text()
                """));

    // SPEC 15.6.4, 15.6 (A3 E16, DECISIONS R4-M73, R4-M147): retention is decided per argument, never per Origin, and E may be used after
    // every point, so a call's store reaches the holders of its argument's route with every Loan it stores, the route's own included;
    // rustc accepts each of these, and SPEC's letter rejects them (no loss; PLAN's G148-X precision candidate would recover the first
    // three). Each is a Language problem: a self-row returning a route's own Loan to its holder, read twice (twoLayersRead,
    // acrossLayersRead); and through an iterator, whose stored raw layer steps Exclusive and passes the guard through its members' referents
    // (R4-M147), an exclusive iteration over a local collection of shared references, then reads (iterated), or over exclusive elements,
    // then a shared one (exclusiveElements, MemberThroughLayersTest's program, with the ends of b and the array, whose Loans the iterator's
    // stores carry to E: its value names b's exclusive Loan, b holding none, so it carries Φ_E by the untracked rule, R4-M116 PQ-2), and an
    // exclusive parameter's iteration whose stores reach E, then an append (bumpRefs, its Loan met once, where the loop's Borrow of values
    // is formed, R4-M68).
    [Fact]
    public void ACallStoreFollowsSpecsPerArgumentRule()
    {
        var c = MirBuildTest.Compile(Prelude + Stores + """
                func twoLayersRead()
                    var x = 1
                    var b = 2
                    var w = x@ref
                    var s = w@uniq
                    put2(s@uniq, b@ref)
                    inspect(w@follow@ref)
                    inspect(w@follow@ref)
                func acrossLayersRead()
                    var x = 1
                    var z = 3
                    var w = x@ref
                    var t = (w@uniq, z@ref)
                    moveIn(t@uniq)
                    inspect(w@follow@ref)
                    inspect(w@follow@ref)
                func iterated()
                    var x = 1
                    var y = 2
                    var refs: Array<ref/i32> = []
                    refs.append(x@ref)
                    for e in refs@uniq
                        e@follow = y@ref
                    inspect(refs@ref)
                    inspect(refs@ref)
                func exclusiveElements()
                    var b = Data.init()
                    var exclusive: [1 of uniq/Data] = [b@uniq]
                    for v in exclusive@uniq
                        v.value += 10
                    for v in exclusive
                        inspect(v)
                func bumpRefs(values: uniq/Array<ref/i32 during a>, y: ref/i32 during a)
                    for v in values
                        v@follow = y
                    values.append(y)
                """);
        Assert.Equal(
            [
                "twoLayersRead: w@follow@ref", "twoLayersRead: w@follow@ref", "acrossLayersRead: w@follow@ref", "acrossLayersRead: w@follow@ref",
                "iterated: refs@ref", "iterated: refs@ref", "exclusiveElements: exclusive",
                "exclusiveElements: var exclusive: [1 of uniq/Data] = [b@uniq]", "exclusiveElements: var b = Data.init()", "bumpRefs: values",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // G147 (1), G160-3 (SPEC 5.7, 15.6.4, 15.6 Addresses): a Dictionary literal places each entry through placeEntry, whose handle
    // `raw/Dictionary<K, V>` admits K and V and addresses the Dictionary's holder (dictionaryStorage's handle), so an element keeps its
    // Loan there: writing the borrowed Place while the Dictionary is used afterwards errs, and once it is dead the write is valid.
    [Fact]
    public void ADictionaryLiteralKeepsItsElementsLoans()
        => Assert.Equal(
            ["literal: value = 99"],
            Problems("""
                func literal()
                    var value = 42
                    let entries = [1: value@ref]
                    value = 99
                    inspect(entries)
                func deadLiteral()
                    var value = 42
                    let entries = [1: value@ref]
                    value = 99
                """));

    // SPEC 15.6, 15.6.4 (A3 E14, review B4): an intrinsic's call retains as its library declaration says. Raw.initialize stores its value
    // into the raw storage, which the body does not own (E), so its Loans are checked at every later point: a write or an exclusive use of
    // the borrowed Place after it errs, and a local's borrow outlives the local (its end errs) unless a write ended it first; a parameter's
    // referent may still be read, or written before. clone keeps its payload's Loans (Content), not the borrow of its handle, which may then
    // be moved (135b, 135c); an object creation keeps its value's Loans.
    [Fact]
    public void AnIntrinsicRetainsAsItsDeclarationSays()
        => Assert.Equal(
            ["rawStored: x = 2", "rawStoredLocal: var x = 1", "rawStoredParameter: y@follow = 2", "rawStoredUniq: y@follow", "clonedPayload: x = 5",
                "made: x = 2", "madeArc: x = 2"],
            Problems("""
                func rawStored()
                    var x = 1
                    let p: raw/(ref/i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(p, x@ref)
                    x = 2
                    unsafe
                        let q: ref/i32 = p[0]
                        inspect(q)
                        Raw.release(p)
                func rawStoredLocal()
                    var x = 1
                    let p: raw/(ref/i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(p, x@ref)
                    inspect(x)
                func rawStoredParameter(y: uniq/i32)
                    let p: raw/(ref/i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(p, y@follow@ref)
                    y@follow = 2
                func rawStoredParameterWrittenBefore(y: uniq/i32)
                    let p: raw/(ref/i32) = Raw.allocate(1)
                    y@follow = 2
                    unsafe => Raw.initialize(p, y@follow@ref)
                func rawStoredParameterRead(y: uniq/i32)
                    let p: raw/(ref/i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(p, y@follow@ref)
                    let v = y@follow
                func rawStoredUniq(y: uniq/i32)
                    let p: raw/(uniq/i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(p, y@follow@uniq)
                    let v = y@follow
                func cloned()
                    var x = 1
                    let owner = Kimi.Intrinsics.makeRc(S.init(r: x@ref))
                    let c = Kimi.Intrinsics.clone(owner@ref)
                    _ = owner@move
                    inspect(c@ref)
                func clonedPayload()
                    var x = 1
                    let owner = Kimi.Intrinsics.makeRc(S.init(r: x@ref))
                    let c = Kimi.Intrinsics.clone(owner@ref)
                    _ = owner@move
                    x = 5
                    inspect(c@ref)
                func made()
                    var x = 1
                    let h = Kimi.Intrinsics.makeObj(S.init(r: x@ref))
                    x = 2
                    inspect(h@ref)
                func madeArc()
                    var x = 1
                    let h = Kimi.Intrinsics.makeArc(S.init(r: x@ref))
                    x = 2
                    inspect(h@ref)
                """));

    // SPEC 15.6.3 (S1, A3 E3, PLAN G110): a Borrow past a shared Deref forms no Loan; it carries the Loans of the layers from its last
    // shared reference on, so its source may be moved or written while it lives, but not what it refers to (39a–39f, 39l–39n).
    [Fact]
    public void ABorrowPastASharedDerefFormsNoLoan()
        => Assert.Equal(
            ["g39b: x = 5", "g39d: x = 5", "g39f: x = 5", "g39l: slot@follow = 5", "g39n: t = [4]"],
            Problems("""
                func g39a()
                    var x = 1
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    let t = s@move
                    inspect(v)
                func g39b()
                    var x = 1
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    let t = s@move
                    x = 5
                    inspect(v)
                func g39c()
                    var x = 1
                    var s = S.init(r: x@ref)
                    var p = s@uniq
                    let v = p.r@follow@ref
                    s.n = 1
                    inspect(v)
                func g39d()
                    var x = 1
                    var s = S.init(r: x@ref)
                    var p = s@uniq
                    let v = p.r@follow@ref
                    x = 5
                    inspect(v)
                func g39e()
                    var x = 1
                    var y = 2
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    s.r = y@ref
                    inspect(v)
                func g39f()
                    var x = 1
                    var y = 2
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    s.r = y@ref
                    x = 5
                    inspect(v)
                func g39l()
                    var x = 1
                    var slot = x@uniq
                    var s = S3.init(rs: slot@ref)
                    var p = s@uniq
                    let v = p.rs@follow@follow@ref
                    slot@follow = 5
                    inspect(v)
                func g39m()
                    var x = 1
                    var o = 2
                    var other = o@uniq
                    var slot = x@uniq
                    var s = S3.init(rs: slot@ref)
                    var p = s@uniq
                    let v = p.rs@follow@follow@ref
                    s.rs = other@ref
                    inspect(v)
                func g39n()
                    var t: Array<i32> = [1, 2, 3]
                    var s = T39.init(p: t@uniq)
                    let q = s@ref
                    let e = q.p@follow@ref
                    t = [4]
                    inspect(e@follow)
                """));

    // SPEC 15.6 ("a value is also used wherever…"; A3 E5, 39o, 100): a borrow is checked with the states of the holders its own Loans name,
    // so borrowing a holder that refers to itself conflicts with that reference though the holder is not used afterwards.
    [Fact]
    public void ABorrowsOwnLoansSeedTheClosure()
        => Assert.Equal(
            ["e39o: s@uniq", "e100: r@follow@uniq"],
            Problems("""
                func e39o()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    var p = s@uniq
                    p.n = 5
                    inspect(p.r@follow)
                func e100()
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    let q = r@follow@uniq
                    q.n = 5
                    inspect(q.r@follow)
                func e100b()
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    let q = r@follow@ref
                    inspect(q.r@follow)
                """));

    // SPEC 4.8.1, 4.8.2 (A3 E6, 42a, 42b, 42d): a Slice element is its handle's referent, so a borrow of one carries the backing's Loan
    // and none on the handle variable.
    [Fact]
    public void ASliceIndexIsASharedDeref()
        => Assert.Equal(
            ["s42a: arr[0] = 5"],
            Problems("""
                func s42a(i: isize)
                    var arr: [3 of i32] = [1, 2, 3]
                    var arr2: [1 of i32] = [4]
                    var s = arr[..]
                    var other = arr2[..]
                    let e = s[i]@ref
                    s = other
                    arr[0] = 5
                    inspect(e)
                func s42b(i: isize)
                    var arr: [3 of i32] = [1, 2, 3]
                    var arr2: [1 of i32] = [4]
                    var s = arr[..]
                    var other = arr2[..]
                    let e = s[i]@ref
                    s = other
                    inspect(e)
                func first<T>(s: Slice<T>) -> ref/T during s.source
                    return s[0]@ref
                """));

    // SPEC 15.6.3 (A3 E8; 23a, 34a, 34c, 29a): destroying a Move Path in place ends every Loan whose path it exactly prefixes, across
    // borrow Derefs: replacing a holder ends the children through it, but a constant index ends nothing borrowed at a runtime one (so
    // k34a's later selection guard, which lends arr shared where it locates arr[i] (G94), meets c as its write does), and a deep borrow of
    // a holder still meets its children.
    [Fact]
    public void DestroyingAMovePathEndsTheLoansItPrefixes()
        => Assert.Equal(
            ["k29a: q@follow@ref", "k34a: arr[i]", "k34a: arr[i]@follow = 7"],
            Problems("""
                func k23a()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr = slot2@uniq
                    rr@follow@follow = 7
                    c@follow = 1
                func k29a()
                    var t = 1
                    var u = 2
                    var s = U.init(p: t@uniq)
                    var o = U.init(p: u@uniq)
                    var q = s@uniq
                    let c = q.p@follow@uniq
                    let w = q@follow@ref
                    q = o@uniq
                    let a = w.p@follow
                    c@follow = 5
                    inspect(w.p@follow)
                func k34a(i: isize)
                    var x = 1
                    var y = 2
                    var z = 3
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    let c = arr[i]@follow@uniq
                    arr[0] = z@uniq
                    arr[i]@follow = 7
                    c@follow = 1
                func k34c(i: isize)
                    var x = 1
                    var y = 2
                    var z = 3
                    var w = 4
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    let c = arr[i]@follow@uniq
                    arr = [z@uniq, w@uniq]
                    arr[i]@follow = 7
                    c@follow = 1
                """));

    // SPEC 15.6.7, 15.7.1, 4.9.6 (A3 err(activate); 10a–10d, 28a–28d, 31a, 31b): a reservation is shared until its call activates it, as
    // an exclusive borrow checked against the holders live into the call but its own, so a read during it is valid, and so are disjoint or
    // shared arguments and two parameters; an overlapping reservation or shared argument, or a live view, conflicts at the activation, a
    // stored exclusive borrow where the shared argument is formed.
    [Fact]
    public void AReservationIsSharedUntilItsActivation()
        => Assert.Equal(
            [
                "r10b: tasks.remove(0) CallReservationConflict_Kd", "r10d: Kimi.Intrinsics.swap(p@uniq, p@uniq) CallReservationConflict_Kd",
                "r28a: two(a@uniq, a@ref) CallActivationConflict_Kd", "r28d: a@ref", "r31b: r31a(x@uniq, x@uniq) CallReservationConflict_Kd",
                "sorted: values.sort() CallActivationConflict_Kd",
            ],
            Problems("""
                func r10a()
                    var p: i32 = 1
                    Kimi.Intrinsics.replace(p@uniq, with: p + 1)
                func r10b()
                    var tasks: Array<Task> = []
                    tasks.append(tasks.remove(0))
                func r10c()
                    var tasks: Array<Task> = []
                    tasks.insert(tasks.length, Task.init(value: 9))
                func r10d()
                    var p: i32 = 0
                    Kimi.Intrinsics.swap(p@uniq, p@uniq)
                func r28a()
                    var a = Pair.init()
                    two(a@uniq, a@ref)
                func r28b()
                    var a = Pair.init()
                    both(a@ref, a@ref)
                func r28c()
                    var a = Pair.init()
                    split(a.a@uniq, a.b@ref)
                func r28d()
                    var a = Pair.init()
                    let r = a@uniq
                    two(r, a@ref)
                func r31a(a: uniq/i32, b: uniq/i32) => Kimi.Intrinsics.swap(a, b)
                func r31b()
                    var x = 1
                    r31a(x@uniq, x@uniq)
                func sorted()
                    var values: Array<i32> = [3, 1, 2]
                    let view = values[..]
                    values.sort()
                    let first = view[0]
                """));

    // SPEC 15.6.7 (DECISIONS R4-M68): the target is checked once, so each Loan and reservation are one problem. An activation conflicts only
    // with a Loan shared in the holder it is found in (f3, a live view, an overlapping reservation); an exclusive Loan that hits the
    // reservation is reported where the later Borrow met the other: at the reservation's formation, a call's (f1, i2), a writeback's (f4) or
    // a destroying call's (i4), or at its own formation or inner activation while the reservation waits (issuedDuring, a reservation then).
    [Fact]
    public void AReservationMeetsEachLoanOnce()
        => Assert.Equal(
            [
                "f1: x@uniq", "f3: give(x@uniq, 1) CallActivationConflict_Kd", "f4: x += 1", "i2: x@uniq", "i4: s@uniq",
                "sortView: values.sort() CallActivationConflict_Kd", "issuedDuring: takeU(arr[0]@uniq) CallReservationConflict_Kd",
                "swapSame: Kimi.Intrinsics.swap(p@uniq, p@uniq) CallReservationConflict_Kd", "f6: giveTwo(x@uniq, x@uniq) CallReservationConflict_Kd",
            ],
            Problems("""
                func giveTwo(a: uniq/i32, b: uniq/i32) => ()
                func takeU(a: uniq/i32) -> i32 => a@follow
                func f1()
                    var x = 1
                    let c = x@uniq
                    give(x@uniq, 1)
                    inspect(c)
                func f3()
                    var x = 1
                    let r = x@ref
                    give(x@uniq, 1)
                    inspect(r)
                func f4()
                    var x = 1
                    let c = x@uniq
                    x += 1
                    inspect(c)
                func i2()
                    var x = 1
                    let c = x@uniq
                    giveTwo(x@uniq, c)
                func i4()
                    var s: Array<i32> = [1]
                    let k = s@uniq
                    Kimi.Intrinsics.replace(s@uniq, with: [2])
                    inspect(k)
                func sortView()
                    var values: Array<i32> = [3, 1]
                    let view = values.slice()
                    values.sort()
                    inspect(view)
                func issuedDuring()
                    var arr: Array<i32> = [1, 2]
                    arr.append(takeU(arr[0]@uniq))
                func swapSame()
                    var p = 1
                    Kimi.Intrinsics.swap(p@uniq, p@uniq)
                func f6()
                    var x = 1
                    giveTwo(x@uniq, x@uniq)
                """));

    // SPEC 7.7.3, 15.6.7 (A3 5's use of the callee operand): an Exclusive call takes its Closure as uniq/Self, so a Value callee's receiver
    // reservation activates at its Call as an argument's does, also through a reference and beside arguments; a Shared call borrows its
    // Closure shared, and a read of the Closure while the arguments are evaluated precedes the activation.
    [Fact]
    public void AValueCalleesReceiverActivatesAtItsCall()
        => Assert.Equal(
            ["called: bump() CallActivationConflict_Kd", "throughUniq: u() CallActivationConflict_Kd", "withArgument: bump(2) CallActivationConflict_Kd"],
            Problems("""
                func called()
                    var value: i32 = 1
                    let r = value@uniq
                    var bump = func [r] () => r@follow += 1
                    let v = bump@ref
                    bump()
                    inspect(v)
                func throughUniq()
                    var value: i32 = 1
                    let r = value@uniq
                    var bump = func [r] () => r@follow += 1
                    let u = bump@uniq
                    let v = u@ref
                    u()
                    inspect(v)
                func withArgument()
                    var value: i32 = 1
                    let r = value@uniq
                    var bump = func [r] (n: i32) => r@follow += n
                    let v = bump@ref
                    bump(2)
                    inspect(v)
                func sharedCall()
                    var value: i32 = 1
                    let r = value@ref
                    var show = func [r] () => inspect(r)
                    let v = show@ref
                    show()
                    inspect(v)
                func readInArgument()
                    var value: i32 = 1
                    let r = value@uniq
                    var bump = func [r] (n: i32) => r@follow += n
                    bump(label w: do
                        let v = bump@ref
                        inspect(v)
                        exit to w 1
                    )
                """));

    // SPEC 13.7.2, 15.6 (A3's writeback row; 109, 121): a compound assignment reserves its destination and activates it at the write, which
    // conflicts with a live shared Loan of it, also one the holder's own state carries (E5).
    [Fact]
    public void TheWritebackActivatesItsReservation()
        => Assert.Equal(
            ["shared: x += 1 PlacementActivationConflict_Kd", "w109: s.n += 1 PlacementActivationConflict_Kd", "w121: r.n += 1 PlacementActivationConflict_Kd"],
            Problems("""
                func counter()
                    var x = 1
                    x += 1
                func shared()
                    var x = 1
                    let v = x@ref
                    x += 1
                    inspect(v)
                func field()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.n += 1
                func w109()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    s.n += 1
                func w121()
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    r.n += 1
                """));

    // SPEC 15.6.7 (A3 E1, PLAN G119; 36h, 38a–38c, 38e–38i, 107): while a reservation waits for its call, a write or destruction of its
    // Place's prefix reaches it across references, a destroying activation included, but neither a write of what is not on its route nor of
    // a distinct constant index; replacing a cursor ends the Loan of its old route (38f, SPEC 15.6.3's deepest).
    [Fact]
    public void AWriteOfAReservationsRouteReachesIt()
        => Assert.Equal(
            [
                "e36h: cur = b CallReservationConflict_Kd", "e38a: r = y@uniq CallReservationConflict_Kd",
                "e38b: cur.inner = i2@uniq CallReservationConflict_Kd", "e38c: cur@follow = H.init(inner: i2@uniq) CallReservationConflict_Kd",
                "e38g: Kimi.Intrinsics.replace(cur@uniq, with: b) CallReservationConflict_Kd", "e38i: arr[j] = z@uniq CallReservationConflict_Kd",
                "e107: p@follow = h2@uniq CallReservationConflict_Kd",
            ],
            Problems("""
                func e36h(a: uniq/H, b: uniq/H) -> i32
                    var cur = a
                    let first = cur.inner.takeWith(label w: do
                        cur = b
                        exit to w 0
                    )
                    return first
                func e38a()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    Kimi.Intrinsics.replace(r@follow@uniq, with: label w: do
                        r = y@uniq
                        exit to w 5
                    )
                    inspect(r)
                func e38b()
                    var i1 = Inner.init()
                    var i2 = Inner.init()
                    var h = H.init(inner: i1@uniq)
                    let cur = h@uniq
                    let first = cur.inner.takeWith(label w: do
                        cur.inner = i2@uniq
                        exit to w 0
                    )
                    inspect(first)
                func e38c()
                    var i1 = Inner.init()
                    var i2 = Inner.init()
                    var h = H.init(inner: i1@uniq)
                    let cur = h@uniq
                    let first = cur.inner.takeWith(label w: do
                        cur@follow = H.init(inner: i2@uniq)
                        exit to w 0
                    )
                    inspect(first)
                func e38e()
                    var i1 = Inner.init()
                    var i2 = Inner.init()
                    var h1 = H.init(inner: i1@uniq)
                    var h2 = H.init(inner: i2@uniq)
                    var a = h1@uniq
                    var b = h2@uniq
                    var cur = a
                    let first = cur.inner.takeWith(label w: do
                        a = b
                        exit to w 0
                    )
                    inspect(first)
                func e38f(root: uniq/Tree) -> uniq/Tree during root
                    var cursor = root
                    while cursor.children.length > 0
                        let next = cursor.children[0]@uniq
                        cursor = next
                    return cursor
                func e38g(a: uniq/H, b: uniq/H) -> i32
                    var cur = a
                    let first = cur.inner.takeWith(label w: do
                        Kimi.Intrinsics.replace(cur@uniq, with: b)
                        exit to w 0
                    )
                    return first
                func e38h()
                    var x = 1
                    var y = 2
                    var z = 3
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    Kimi.Intrinsics.replace(arr[0]@follow@uniq, with: label w: do
                        arr[1] = z@uniq
                        exit to w 5
                    )
                    inspect(arr)
                func e38i(i: isize, j: isize)
                    var x = 1
                    var y = 2
                    var z = 3
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    Kimi.Intrinsics.replace(arr[i]@follow@uniq, with: label w: do
                        arr[j] = z@uniq
                        exit to w 5
                    )
                    inspect(arr)
                func e107()
                    var i1 = Inner.init()
                    var i2 = Inner.init()
                    var h1 = H.init(inner: i1@uniq)
                    var h2 = H.init(inner: i2@uniq)
                    var slot = h1@uniq
                    let p = slot@uniq
                    let first = p@follow.inner.takeWith(label w: do
                        p@follow = h2@uniq
                        exit to w 0
                    )
                    inspect(first)
                """));

    // SPEC 15.6.6, 15.6.3, 15.7.1 (A3 err(destroy), E8, E11; 26a, 26b, 123, 123c, 15a, 15b): a call that destroys its target in place
    // reads the target where it is reserved and writes it at activation, and its old value's observing destruction uses the Places past it,
    // before the call ends the Loans it prefixes (SPEC 15.6.3's replace keeps c's parent Loan, 15b); a `clear` activates as an exclusive
    // borrow, and its receiver's reservation met c's Loan at its formation, which reports the pair (R4-M68, as for s2 and in
    // AReservationMeetsEachLoanOnce).
    [Fact]
    public void ADestroyingCallChecksWhatItDestroys()
        => Assert.Equal(
            [
                "d26a: Kimi.Intrinsics.replace(s@uniq, with: C.init(r: y@uniq))", "d26b: arr",
                "d123: Kimi.Intrinsics.replace(s@uniq, with: Q.init(r: y@uniq, q: z@ref))",
                "d123c: Kimi.Intrinsics.replace(s@uniq, with: Q.init(r: y@uniq, q: z@ref))", "d15b: x = 3",
            ],
            Problems("""
                func d26a()
                    var x: Array<i32> = [1, 2]
                    var y: Array<i32> = [3]
                    var s = C.init(r: x@uniq)
                    let c = s.r@follow[0]@uniq
                    Kimi.Intrinsics.replace(s@uniq, with: C.init(r: y@uniq))
                    inspect(c@follow)
                func d26b()
                    var x = 1
                    var arr: Array<D> = [D.init(r: x@uniq)]
                    let c = arr[0].r@follow@uniq
                    arr.clear()
                    inspect(c@follow)
                func d123()
                    var x = 1
                    var y = 2
                    var z = 0
                    var s = Q.init(r: x@uniq, q: z@ref)
                    s.q = s.r@follow@ref
                    Kimi.Intrinsics.replace(s@uniq, with: Q.init(r: y@uniq, q: z@ref))
                func d123c()
                    var x = 1
                    var y = 2
                    var z = 0
                    var s = Q.init(r: x@uniq, q: z@ref)
                    s.q = s.r@follow@ref
                    Kimi.Intrinsics.replace(s@uniq, with: Q.init(r: y@uniq, q: z@ref))
                    inspect(s)
                func d123v()
                    var x = 1
                    var y = 2
                    var z = 0
                    var s = Q.init(r: x@uniq, q: z@ref)
                    Kimi.Intrinsics.replace(s@uniq, with: Q.init(r: y@uniq, q: z@ref))
                    inspect(s)
                func d15a()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    Kimi.Intrinsics.replace(r@uniq, with: y@uniq)
                    c@follow = 3
                func d15b()
                    var x = 1
                    var y = 2
                    var r = x@uniq
                    let c = r@follow@uniq
                    Kimi.Intrinsics.replace(r@uniq, with: y@uniq)
                    x = 3
                    c@follow = 1
                """));

    // SPEC 15.6.6 (A3 obs; 24a, 24b, 27, 29b's store through a reference): a destruction that may observe a Loan, the old value's at `=`
    // or a holder's at its scope end, is an exclusive use of its Place and of what it reaches through exclusive references, checked before
    // the ending, with the value it destroys (A3 E13 (1d), SPEC 15.6: so s's own end in 29b, whose new value carries p's Loan on s as its
    // reborrow's parent, SPEC 15.6.3); one whose Type observes nothing, or that a shared reference leads past, is shallow.
    [Fact]
    public void AnObservingDestructionIsAnExclusiveDeepUse()
        => Assert.Equal(
            [
                "o1566: s = D.init(r: y@uniq)", "o24a: s = D.init(r: s.r@follow@uniq)", "o29b: p@follow = D.init(r: p.r@follow@uniq)",
                "o29b: var s = D.init(r: x@uniq)",
            ],
            Problems("""
                func o1566()
                    var x = 1
                    var y = 2
                    var s = D.init(r: x@uniq)
                    let c = s.r@follow@uniq
                    s = D.init(r: y@uniq)
                    c@follow = 1
                func o24a()
                    var x = 1
                    var s = D.init(r: x@uniq)
                    s = D.init(r: s.r@follow@uniq)
                    inspect(s)
                func o24b()
                    var x = 1
                    var s = U.init(p: x@uniq)
                    s = U.init(p: s.p@follow@uniq)
                    inspect(s)
                func o27()
                    var x = 1
                    var other = 2
                    var v = other@ref
                    label b: do
                        let s = R.init(r: x@ref)
                        v = s.r@follow@ref
                    inspect(v)
                func o29b()
                    var x = 1
                    var s = D.init(r: x@uniq)
                    let p = s@uniq
                    p@follow = D.init(r: p.r@follow@uniq)
                """));

    // SPEC 3.6, 15.6.6 (DECISIONS R4-M129): a call's result that borrows its argument's temporary and observes it in its destruction is
    // destroyed before that temporary, so the Loan its destruction observes is still valid (fail-before: the argument's storage ended first,
    // a conflict at each argument).
    [Fact]
    public void AResultIsDestroyedBeforeTheTemporaryItBorrows()
        => Assert.Empty(Problems("func lend(value: ref/i32) -> R => R.init(r: value)\nfunc t()\n    inspect(lend(1 + 1))\n    inspect(lend(size(3)))\n"));

    // SPEC 14.2.1, 16.2.2 step 3, 15.6.6 (DECISIONS R4-M129): an if clause's, a match arm's and a do's body is its own Body, so the
    // temporaries of its result end with it, and a result that borrows one and observes it in its destruction outlives it: an error at
    // the temporary (fail-before: an if clause's and a do's temporaries were the enclosing statement's, destroyed after the result).
    [Fact]
    public void AnArmsResultDoesNotOutliveItsTemporaries()
        => Assert.Equal(
            ["t1: 1 + 1", "t2: 1 + 1", "t3: 1 + 1"],
            Problems("""
                func lend(value: ref/i32) -> R => R.init(r: value)
                func t1(c: bool)
                    inspect(if c => lend(1 + 1) else => return)
                func t2(c: bool)
                    inspect(match c
                        true => lend(1 + 1)
                        false => return
                    )
                func t3()
                    inspect(do => lend(1 + 1))
                """));

    // SPEC 8.15.1, 15.6, 15.6.6, 3.6 (PLAN10-X1 and abstract temporaries; the R5 plan's Q1 programs, its Q5 and ask A-3): a generic body is
    // checked for every instance, so a holder of an abstract Type may observe its Loans when it is destroyed: replacing `local` while the
    // iterator it lent is live conflicts in `hold<C>`, as in its twin over a Bin whose iterator's drop observes the source, while the twin
    // over Array<i32>, whose iterator observes nothing, is valid; and a borrow of an abstract call's temporary ends with its full
    // expression, so a later use errs, as in its twin over Array<i32>. hold<C>'s abstract iterator passes the guard (an abstract part may
    // be an exclusive reference, the owner's Q1 (a), DECISIONS R4-M147), so its conflict is the Language problem its twin's is.
    [Fact]
    public void AnAbstractDestructionOrTemporaryIsCheckedInItsDefinition()
    {
        var c = MirBuildTest.Compile(Prelude + """
                struct Bin
                    Self is Iterable
                    associate Iterable.IteratorType(source) is BinIterator during source
                    var items: Array<i32>
                    public init(items: Array<i32>) => self.items = items@move
                    public func iterate(self: ref/Self during source) -> BinIterator during source => BinIterator.init(self.items.iterate())
                struct BinIterator {source}
                    Self is Iterator
                    associate Iterator.Item is i32
                    var inner: ArrayIterator<i32> during source
                    public init(inner: ArrayIterator<i32> during source) => self.inner = inner@move
                    public func next(self: uniq/Self) -> Option<i32>
                        match self.inner.next()
                            .Some(let v) => return .Some(v@follow)
                            .None => return .None
                    drop
                        match self.inner.next()
                            .Some(let v) => Console.writeLine("drop sees \(v@follow)")
                            .None => Console.writeLine("drop sees none")
                func hold<C>(first: C, second: C) -> ()
                    C is Iterable
                    var local = first@move
                    var it = local.iterate()
                    _ = it.next()
                    local = second@move
                func holdBin(first: Bin, second: Bin) -> ()
                    var local = first@move
                    var it = local.iterate()
                    _ = it.next()
                    local = second@move
                func holdArray(first: Array<i32>, second: Array<i32>) -> ()
                    var local = first@move
                    var it = local.iterate()
                    _ = it.next()
                    local = second@move
                func keep<T>(make: () -> T) -> ()
                    let r = make()@ref
                    inspect(r)
                func keepArray(make: () -> Array<i32>) -> ()
                    let r = make()@ref
                    inspect(r)
                """);
        Assert.Equal(
            ["hold: local = second@move", "holdBin: local = second@move", "keep: make()", "keepArray: make()"],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 12.2, 15.6 (A3 E13 (1a), (2); prototype round 13's 136-137, n1-n2, s1-s2, fr-rs1, 131 and 130g): an operation evaluates its
    // operands in order and uses each one's value after its access, so operand i's access is checked with the values of the operands up to
    // it and the holders of the later ones: a Place read before a later operand reads through an exclusive reference to it errs, read
    // after it is valid, whether the operands are a call's (named ones, an unbound reference's receiver supplied after a parameter, a
    // setter's value before its receiver, a Value callee before its arguments), a by-value receiver's, an aggregate's or an operator's.
    [Fact]
    public void AnOperandIsCheckedWithTheValuesEvaluatedAroundIt()
        => Assert.Equal(
            [
                "o136b: give2(x, r@follow)", "o136d: (x, r@follow)", "o136f: x + r@follow", "o136g: x.plus(r@follow)", "o137b: (x@uniq, x)",
                "o137c: (r, x)", "o137e: x", "n2: x", "s2: g", "rs1: x",
                "o131a: give(r, x)", "o131b: giveR(r, x)", "o131c: x", "o130g: bump(value)",
            ],
            Problems(Order + """
                func o136a()
                    var x = 1
                    let r = x@uniq
                    give2(r@follow, x)
                func o136b()
                    var x = 1
                    let r = x@uniq
                    give2(x, r@follow)
                func o136c()
                    var x = 1
                    let r = x@uniq
                    let t = (r@follow, x)
                    inspect(t)
                func o136d()
                    var x = 1
                    let r = x@uniq
                    let t = (x, r@follow)
                    inspect(t)
                func o136e()
                    var x = 1
                    let r = x@uniq
                    let s = r@follow + x
                func o136f()
                    var x = 1
                    let r = x@uniq
                    let s = x + r@follow
                func o136g()
                    var x = Num.init()
                    let r = x@uniq
                    _ = x.plus(r@follow)
                func o136h()
                    var x = Num.init()
                    let r = x@uniq
                    _ = r@follow.plus(x)
                func o137a()
                    var x = 1
                    _ = (x, x@uniq)
                func o137b()
                    var x = 1
                    let t = (x@uniq, x)
                    inspect(t)
                func o137c()
                    var x = 1
                    let r = x@uniq
                    _ = (r, x)
                func o137d()
                    var x = 1
                    let r = x@ref
                    _ = (r, x)
                func o137e()
                    var x = 1
                    let r = x@uniq
                    _ = (x, r)
                func n1()
                    var x = 1
                    let r = x@uniq
                    give2(b: r@follow, a: x)
                func n2()
                    var x = 1
                    let r = x@uniq
                    give2(b: x, a: r@follow)
                func s1()
                    var g = Gauge.init()
                    let r = g@uniq
                    g.level = r@follow.level
                func s2()
                    var g = Gauge.init()
                    let r = g@uniq
                    g.level = r@follow.level
                    inspect(r)
                func rs1()
                    var b = Box.init()
                    var x = b@ref
                    let r = x@uniq
                    Box.f(x, r@follow)
                func o131a()
                    var x = 1
                    let r = x@uniq
                    give(r, x)
                func o131b()
                    var x = 1
                    let r = x@uniq
                    giveR(r, x)
                func o131c()
                    var x = 1
                    let r = x@uniq
                    giveS(r, x)
                func o131d()
                    var x = 1
                    give(x@uniq, x)
                func o131e()
                    var x = 1
                    let r = x@uniq
                    give(r, r@follow)
                func o131f()
                    var z = 0
                    var s = S.init(r: z@ref)
                    setN(s@uniq, s.n)
                func o130g()
                    var value: i32 = 1
                    let r = value@uniq
                    var bump = func [r] (n: i32) => r@follow += n
                    bump(value)
                """));

    // SPEC 15.6 (A3 E13 (1a); prototype 140, u1-u2, c1): an operand's own value is used by its operation after its access, an rvalue's
    // single one's too, so moving or reading a holder that carries a Loan on its own part errs where it is evaluated, whether the value is
    // used later or not; a holder whose Loan is on another root does not.
    [Fact]
    public void AnOperandIsCheckedWithItsOwnValue()
        => Assert.Equal(
            ["o140a: s", "o140b: s", "o140c: s", "o140e: s", "o140f: s", "u1: give2(s.n, 1)", "u2: s.n", ": func [s@move]() => ()"],
            Problems(Order + """
                func o140a()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    give2T(s@move, 1)
                func o140b()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    _ = (s@move, 1)
                func o140c()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    let t = (s@move, 1)
                    inspect(t)
                func o140d()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    give2(s.n, s.n)
                func o140e()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    let t = s@move
                func o140f()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    let t = s@move
                    inspect(t)
                func o140g()
                    var z = 0
                    var y = 0
                    var s = S.init(r: z@ref)
                    let r2 = y@ref
                    s.r = r2
                    let t = s@move
                func u1()
                    var z = 0
                    var s = SU.init(r: z@uniq)
                    s.r = s.n@uniq
                    give2(s.n, 1)
                func u2()
                    var z = 0
                    var s = SU.init(r: z@uniq)
                    s.r = s.n@uniq
                    let t = s.n
                func c1()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    let c = func [s@move] () => ()
                """));

    // SPEC 15.6, 15.6.3, 15.6.7 (A3 E13 (1b), (1c), (1d); prototype a1-a2, rf1-rf2, b1-b2, 41o, d1-d3): an activation takes the other
    // operands' values, not the holders live into its call, so a reference read before the call and not used after it does not conflict;
    // a borrow takes its R2 parents within E3's bound, whatever its destination's liveness, and E5's seed; an observing destruction takes
    // the value it destroys, so a holder that carries a Loan on its own part errs where it is destroyed.
    [Fact]
    public void AnActivationABorrowAndADestructionTakeTheirValues()
        => Assert.Equal(
            [
                "a2: give(y@uniq, r@follow) CallActivationConflict_Kd", "rf1: s.r@follow@uniq", "rf2: s.r@follow@uniq", "b2: w.p@follow.r@follow@ref",
                "o41o: s@uniq", "d1: var q = Q2.init(r: z@uniq)", "d3: q = Q2.init(r: z2@uniq)",
            ],
            Problems(Order + """
                func a1()
                    var y = 1
                    let r = y@ref
                    give(y@uniq, r@follow)
                func a2()
                    var y = 1
                    let r = y@ref
                    give(y@uniq, r@follow)
                    inspect(r)
                func rf1()
                    var z = 0
                    var w = 0
                    var s = S7.init(r: z@uniq, q: w@ref)
                    s.q = s.r@follow@ref
                    let t = s.r@follow@uniq
                func rf2()
                    var z = 0
                    var w = 0
                    var s = S7.init(r: z@uniq, q: w@ref)
                    s.q = s.r@follow@ref
                    let t = s.r@follow@uniq
                    inspect(t)
                func b1()
                    var y = 0
                    var o = y@ref
                    var s = S5.init(r: y@ref)
                    var w = W5.init(p: s@uniq, q: o@uniq)
                    w.q = w.p@follow.r@uniq
                    let t = w.p@follow.r@follow@ref
                    inspect(t)
                func b2()
                    var y = 0
                    var o = y@ref
                    var s = S5.init(r: y@ref)
                    var w = W5.init(p: s@uniq, q: o@uniq)
                    w.q = w.p@follow.r@uniq
                    let t = w.p@follow.r@follow@ref
                    inspect(t)
                    inspect(w.q)
                func o41o()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    let p = s@uniq
                    p.n = 5
                    inspect(p.r@follow)
                func d1()
                    var z = 0
                    var q = Q2.init(r: z@uniq)
                    q.r = q.n@uniq
                func d2()
                    var z = 0
                    var y = 0
                    var q = Q2.init(r: z@uniq)
                    q.r = y@uniq
                func d3()
                    var z = 0
                    var z2 = 0
                    var q = Q2.init(r: z@uniq)
                    q.r = q.n@uniq
                    q = Q2.init(r: z2@uniq)
                """));

    // SPEC 8.14, 15.6.7 (A3 E13 (3), (4), review N3; prototype 132, 134, 138, 139): a pair-layer acquisition is its own Assign's borrow in its
    // marker's mode: an invocation's input reserves in its exclusive case, activated at its Call, so a shared borrow of its Place before
    // the Call is valid; elsewhere (a local, a tuple's element, a capture) it is a live exclusive borrow; and in a view where the pair
    // layer is a reference its step is a borrow Deref's, so a borrow through the layer meets it. Each body errs in its uniq view, p139d at
    // `x@ref`, which no store causes (an Unsupported_Kd until R4-M147 removed P13's body hold).
    [Fact]
    public void APairLayerAcquisitionBorrowsAsItsMarkerSays()
    {
        var c = MirBuildTest.Compile(Prelude + Order + """
                func p132a<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    inner2(v@ref, v)
                func p132c<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    inner3(v, v@ref)
                func p132h<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    inner9(v, v)
                func p132m<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    let u = v@follow@ref
                    innerR(v, u)
                func p134a<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    let u = v@follow@ref
                    _ = (v, u)
                func p134c<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    inner11(do => v, peek(v@follow@ref))
                func p134e<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    let t = (v, peek(v@follow@ref))
                func p138a<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    let u = v@follow@ref
                    let c = func [v, u] () => ()
                func p138c<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    let u = v@follow@ref
                    let c = func [u, v] () => ()
                func p138d<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    runF(func [v] () => (), peek(v@follow@ref))
                func p139a<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    inner11(v, peek(v@follow@ref))
                func p139c<s/T>(p: s/T, item: T) -> ()
                    s is ref or uniq
                    T is Copy
                    var x = item
                    inner11(x@(s/T), peek(x@ref))
                func p139d<s/T>(p: s/T, item: T) -> ()
                    s is ref or uniq
                    T is Copy
                    var x = item
                    let a = x@(s/T)
                    let k = peek(x@ref)
                    inner11(a, k)
                func pN3<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    let a = v
                    let k = peek(v@follow@ref)
                    inner11(a, k)
                func pIn<s/T>(p: s/T) -> ()
                    s is ref or uniq
                    var v = p
                    inner11(v, 1)
                """);
        Assert.Equal(
            [
                "p132a: inner2(v@ref, v) CallActivationConflict_Kd", "p132c: inner3(v, v@ref) CallActivationConflict_Kd",
                "p132h: inner9(v, v) CallReservationConflict_Kd", "p132m: innerR(v, u) CallActivationConflict_Kd", "p134a: v",
                "p134c: v@follow@ref", "p134e: v@follow@ref", ": func [v, u]() => ()", ": func [u, v]() => ()", "p138d: v@follow@ref",
                "p139d: x@ref", "pN3: v@follow@ref",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // A3 E5, CS_call (100d, 101, 110, 111): an activation is checked with the states of the holders its reservation's own Loans name, so
    // reserving a holder that refers to itself conflicts at the call though the holder is not used afterwards; a sibling Field does not.
    [Fact]
    public void AnActivationIsCheckedWithItsReservationsHolders()
        => Assert.Equal(
            ["e100d: keepT(r@follow@uniq) CallActivationConflict_Kd", "e101: setN(s@uniq, 5) CallActivationConflict_Kd"],
            Problems("""
                func e100d()
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    let q = keepT(r@follow@uniq)
                    q.n = 5
                    inspect(q.r@follow)
                func e101()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    setN(s@uniq, 5)
                func e110()
                    var y = 0
                    var x = y@ref
                    inspect(x@uniq)
                func e111()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    setM(s.m@uniq)
                """));

    // R4-M36, R4-M51, A3 5: a Drop whose Type's destruction walk passes its depth bound and a call whose contract Binding does not
    // instantiate (CallRetention.Limit: storage nested past its depth bound, as a growing recursive Type's) are not classified:
    // OwnershipDepthLimit_Kd there (the owner's 「G272: (1)」, R4-M153), and no Loan problem; a body without a Loan, which no conflict can
    // depend on, reports neither. The parameter's Drop is located at its parameter (DECISIONS BU-48's locations, settled (a): a parameter's
    // record at its Type occurrence, R4-M90 D6's rule extended).
    [Fact]
    public void AnUnclassifiedDestructionOrCallIsUnsupported()
    {
        var c = MirBuildTest.Compile(Prelude + "func deep(n: Nest<ref/i32 during a>)\n    let r = n@ref\n" +
            "func unborrowed(n: Nest<ref/i32 during a>)\n    let r = 1\nfunc made()\n    var y = 1\n    let n = Nest<ref/i32>.init(value: y@ref)\n" +
            "func unlent(x: ref/i32)\n    let n = Nest<ref/i32>.init(value: x)\n");
        var program = Analyze(c);
        Assert.Equal(
            ["OwnershipDepthLimit_Kd Nest<ref/i32 during a>", "OwnershipDepthLimit_Kd Nest<ref/i32>.init(value: y@ref)"], // The parameter's at its Type (BU-48 (a)).
            program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0).Select(x => $"{x.Code} {Line(c, x.Subject)}"));
    }

    // R4-M70 (SPEC 23.1.2, R4-M10): an Opaque site's value is {Φ_E}. The site rests on a recorded error, so a conflict found only through it
    // is derived and never arises (the 1c design's op6; R5's Q21 review N3: a failed member access whose operand's Loan a kept temp holds,
    // `get(x@ref).missing` and `(p@uniq).missing`), while a Loan another holder keeps is checked: op5's p, o7's r, and j1's h on the path
    // that does not replace it.
    [Fact]
    public void AnOpaqueValueCarriesNoLoan()
        => Assert.Equal(
            ["op5: x = 2", "o7: x = 2", "j1: x = 2"],
            RejectedProblems("""
                func get(a: ref/i32) -> ref/i32 during a => a
                func op5()
                    var x = 1
                    let p = x@ref
                    let r = bogus(p)
                    x = 2
                    inspect(r)
                    inspect(p)
                func op6()
                    var x = 1
                    let p = x@ref
                    let r: ref/i32 = bogus(p)
                    x = 2
                    inspect(r)
                func o7()
                    var x = 1
                    let r = x@ref
                    let s: ref/i32 = undefinedFn(r)
                    x = 2
                    inspect(s)
                    inspect(r)
                func j1(c: bool)
                    var x = 1
                    let r = x@ref
                    var h: ref/i32 = r
                    if c
                        h = undefinedFn(0)
                    x = 2
                    inspect(h)
                func kept()
                    var x = 1
                    let r: ref/i32 = get(x@ref).missing
                    x = 5
                    _ = r@follow
                func keptUnique()
                    var p = Pair.init()
                    let r: uniq/Pair = (p@uniq).missing
                    _ = p.a
                    _ = r.a
                """));

    // SPEC 4.8.2 (A3 E6, ret): a Slice handle is a borrow, so a call retaining it by its own Origin also carries what a read through it
    // reaches, and one retaining only its elements' Origins (toArray) keeps their Loans but not the handle's (SliceToArrayTest's References);
    // a view of a view keeps the outer view's own Loan beside its elements' (P4, Milestone27:100–101).
    [Fact]
    public void ASliceHandleIsABorrowForRetention()
        => Assert.Equal(
            ["elementsKept: number = 5", "m27: references = [copied@ref]"],
            Problems("""
                func copiedThenWritten()
                    var first = 4
                    var second = 7
                    var values: [2 of ref/i32] = [first@ref, second@ref]
                    let copied = values.slice().toArray()
                    values[0] = second@ref
                    inspect(copied)
                func copiedThenReplaced()
                    var number = 42
                    var copied = 1
                    var references: [_ of ref/i32] = [number@ref]
                    let retained = references.slice().toArray()
                    references = [copied@ref]
                    inspect(retained)
                func elementsKept()
                    var number = 42
                    var references: [_ of ref/i32] = [number@ref]
                    let retained = references.slice().toArray()
                    number = 5
                    inspect(retained)
                func m27()
                    var number = 42
                    var copied = 1
                    var references: [_ of ref/i32] = [number@ref]
                    let retained = references.sliceUniq().slice().toArray()
                    references = [copied@ref]
                    inspect(retained)
                """));

    // SPEC 14.8.3 (A3 E7; 43a, 92, 102, 103, 104, 44a–44c): a guard candidate's guard-local Origin ends at the guard's cleanup, so a value
    // that keeps it, stored into a holder used afterwards or through a live reference, escapes the guard at the candidate; a value read or
    // copied out of the candidate does not, nor one overwritten before its next use; another EndBorrow, which a transfer abandoning a
    // reservation or a comparison operand ends on its edge, checks nothing. The builder names every candidate through the Subject's
    // temporary, past its shared Deref (so 92g, 93 and 93g are 92 in MIR).
    [Fact]
    public void AGuardCandidatesOriginEndsAtItsCleanup()
        => Assert.Equal(
            ["g43a: let d", "g92: let d", "g102: let d"],
            Problems("""
                func g43a()
                    let fallback = Data.init()
                    let packet: Packet = .Data(Data.init())
                    var saved = fallback@ref
                    match packet
                        .Data(let d) if save(saved@uniq, d) => ()
                        .Link(let r) if save(saved@uniq, r@follow@copy) => ()
                        _ => ()
                    inspect(saved)
                func g92()
                    var x: Choice = .Data(1)
                    var z: Choice = .Empty
                    var other = 0
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    var saved = other@ref
                    match rr@follow@follow
                        .Data(let d) if (label w: do
                            saved = d@follow@ref
                            rr = slot2@uniq
                            exit to w true
                        ) => ()
                        _ => ()
                    inspect(saved)
                func g102()
                    let fallback = Data.init()
                    let packet: Packet = .Data(Data.init())
                    var holder = Holder.init(saved: fallback@ref)
                    let w = holder@uniq
                    match packet
                        .Data(let d) if (label g: do
                            w.saved = d
                            exit to g true
                        ) => ()
                        _ => ()
                    inspect(w)
                func g103()
                    let packet: Packet = .Data(Data.init())
                    var n = 0
                    match packet
                        .Data(let d) if (label g: do
                            n = d@follow.count
                            exit to g true
                        ) => ()
                        _ => ()
                    inspect(n)
                func g104(c: bool)
                    let other = Data.init()
                    let packet: Packet = .Data(Data.init())
                    var tmp = other@ref
                    while c
                        match packet
                            .Data(let d) if (label g: do
                                tmp = d
                                exit to g true
                            ) => ()
                            _ => ()
                        tmp = other@ref
                    inspect(tmp)
                func g44a(p: bool) -> i32
                    var x = 1
                    give(x@uniq, label w: do
                        if p => return 0
                        exit to w 1
                    )
                    inspect(x)
                    return 1
                func g44b(p: bool) -> i32
                    var x = 1
                    var y = 2
                    let same = x == label w: do
                        if p => return 0
                        exit to w y
                    x = 3
                    inspect(same)
                    return 1
                func g44c(root: uniq/Tree)
                    var cursor = root
                    while cursor.children.length > 0
                        cursor = cursor.child(label w: do
                            if stop() => exit
                            exit to w 0
                        )
                    cursor.value = 1
                """));

    // SPEC 14.8.3 (A3 E7; 1c design review item 4): a guard candidate's guard-local Origin needs no Loan, so a view without one still checks
    // its guards: a value that keeps the candidate in a holder used afterwards, or in the result, escapes at the candidate; a guard that only
    // reads the candidate does not.
    [Fact]
    public void AGuardIsCheckedInAViewWithoutALoan()
        => Assert.Equal(
            ["localNoLoan: let d", "pickNoLoan: let d"],
            Problems("""
                func localNoLoan(packet: ref/Packet, fallback: ref/Data)
                    var saved = fallback
                    match packet@follow
                        .Data(let d) if (label g: do
                            saved = d
                            exit to g true
                        ) => ()
                        _ => ()
                    inspect(saved)
                func pickNoLoan(packet: ref/Packet during p, fallback: ref/Data during p) -> ref/Data during p
                    var saved = fallback
                    match packet@follow
                        .Data(let d) if (label g: do
                            saved = d
                            exit to g true
                        ) => ()
                        _ => ()
                    return saved
                func localValid(packet: ref/Packet, fallback: ref/Data)
                    var saved = fallback
                    match packet@follow
                        .Data(let d) if (label g: do
                            inspect(d)
                            exit to g true
                        ) => ()
                        _ => ()
                    inspect(saved)
                """));

    // The SPEC examples of the guard, reservation and destruction sections (tests/diagnostics/spec-cases.json: family F3, SPEC 14.8.3,
    // 15.6.6, 15.6.7 and 15.6.4's drop retention) get exactly the Loan problems their records give, from A5 run on the MIR here;
    // VerifiedSpecExampleTest checks the driver's records of the landed families (F1 and F3, unit 1b-5b-1).
    [Theory]
    [MemberData(nameof(SpecVariants))]
    public void TheSpecExamplesGetTheirLoanCodes(string name)
    {
        var example = DiagnosticCorpus.SpecCases.Single(x => x.Name == name[..name.LastIndexOf('/')]);
        var variant = example.Variants[int.Parse(name[(name.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture)];
        var source = DiagnosticCorpus.Source(example, variant);
        var c = CompilationTestHelper.ParseSuccess(source, "Hello.kimi");
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        var program = c.BuildMir();
        new Stratum(c.Types).Run(program);
        Assert.Equal(
            variant.Expected.Where(static x => LoanCodes.Contains(Enum.Parse<DiagnosticCode>(x.Code)))
                .Select(x => $"{x.Code} {source.AsSpan(0, new SyntaxLocation(x.Line).Resolve(source).Start).Count('\n') + 1}").Order(StringComparer.Ordinal),
            program.Problems.All.ToArray().Where(static x => LoanCodes.Contains(x.Code) && x.Subject.Module == 0)
                .Select(x => $"{x.Code} {source.AsSpan(0, c.SyntaxOf(x.Subject).Span.Start).Count('\n') + 1}").Order(StringComparer.Ordinal));
    }

    // A3 6.4, R4-M3a: a problem's facts are its Loan's or guard candidate's anchor and its witness: the holder's next use in its chain (for
    // an activation from its call on; none in a later chain), or the store that carries the Loan (TheWarningCarriesItsFacts's `Store b`); and an
    // activation's reservation. selfStore is valid (AStoredValueIsCheckedInTransit; DECISIONS R4-M154).
    [Fact]
    public void AProblemNamesItsLoanAndWitness()
    {
        var c = MirBuildTest.Compile(Prelude + """
            func sharedThenWrite()
                var x = 1
                let r = x@ref
                x = 2
                inspect(r)
            func selfStore(y: ref/i32)
                var x = y
                x = pin(x@ref)
                inspect(x)
            func activated()
                var a = Pair.init()
                two(a@uniq, a@ref)
            func escaped()
                let fallback = Data.init()
                let packet: Packet = .Data(Data.init())
                var saved = fallback@ref
                match packet
                    .Data(let d) if save(saved@uniq, d) => ()
                    _ => ()
                inspect(saved)
            """);
        var program = Analyze(c);
        var problems = program.Problems.All.ToArray();
        Assert.Equal(
            [
                "x = 2: Anchor x@ref, Use inspect(r)", "two(a@uniq, a@ref): Anchor a@ref, Use two(a@uniq, a@ref), Reservation a@uniq",
                "let d: Anchor let d",
            ],
            problems.Select((x, i) => (x, i)).Where(static x => LoanCodes.Contains(x.x.Code)).Select(x =>
                $"{Line(c, x.x.Subject)}: {string.Join(", ", program.Problems.Facts(x.i).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)}"))}"));
    }

    // V16 (A3 6.3, shapes 1–6): N Copies of one reference, N loops with three live references and N writes through one reference keep the
    // work linear; a straight line of N borrowed locals, N borrow/write pairs of one root and N borrow/write pairs in loops grow it within
    // the bound's dense terms, quadratically at most: each write, StorageLive and StorageDead ends its Loans in every holder, each borrow's
    // temporary included ((ended points) × holders), and N references used at the end make each borrow scan the live holders (accesses ×
    // live nodes). N loops that each write a root, borrow it again into one reference and read it (review V1) leave k Loans in the
    // reference in loop k, an output quadratic in N: the worklist settles each loop before the next (R4-M65), so the passes stay constant
    // and the work and the Loan-set bytes (ν) are dense, not cubic. One operation with N operands, each carrying a Loan and each an
    // access of a borrowed root, gathers its check sets once (A3 E13 (5)), so it stays linear. N calls storing through a four-layer chain
    // write every layer (A3 E16), each accumulating every call's Loan, which the next call's walk scans: dense too (R4-M73). N addressed
    // holders read through their live pointers across N writes (each write scans the live holders, the addressed ones among them) and a
    // raw chain of depth N (each pointer addressing the one before, read through at the end) stay within the dense bound (R4-M116, B §7).
    [Theory]
    [InlineData("    var x = 1\n    let r = x@ref\n", "    var x{0} = {0}\n    let r{0} = x{0}@ref\n    inspect(r{0})\n", "", true)]
    [InlineData("    var x = 1\n    let r = x@ref\n", "    var x{0} = {0}\n    let r{0} = x{0}@ref\n", "    inspect(r{0})\n", true)]
    [InlineData("    var x = 1\n    let r = x@ref\n", "    let c{0} = r\n", "    inspect(c{0})\n", false)]
    [InlineData(
        "    var x = 1\n    let r = x@ref\n    var y = 2\n    let u = y@ref\n    var z = 3\n    var p = z@uniq\n",
        "    while flag\n        inspect(r)\n        inspect(u)\n        p@follow = {0}\n",
        "",
        false)]
    [InlineData("    var z = 3\n    var p = z@uniq\n", "    p@follow = {0}\n", "", false)]
    [InlineData("    var x = 0\n    var r = x@ref\n", "    x = {0}\n    r = x@ref\n    inspect(r)\n", "", true)]
    [InlineData(
        "    var t = 0\n    var x = 1\n    let r = x@ref\n",
        "    while flag\n        let a{0} = t@uniq\n        a{0}@follow = {0}\n        t = {0}\n        inspect(r)\n",
        "",
        true)]
    [InlineData("    var x = 0\n    var r = x@ref\n", "    while flag\n        x = {0}\n        r = x@ref\n        inspect(r)\n", "", true)]
    [InlineData("    var x = 1\n    let r = x@ref\n    let rr = r@ref\n", "", "", false, "r")]
    [InlineData("    var x = 1\n    var b = 2\n    var w = x@ref\n    var s1 = w@uniq\n    var s2 = s1@uniq\n    var s3 = s2@uniq\n", "    put4(s3@uniq, b@ref)\n", "", true)]
    [InlineData(
        "    var y = 1\n    var z = 0\n", "    var w{0}: ref/i32 = y@ref\n    let p{0} = w{0}@raw\n    z = {0}\n", "    unsafe => inspect(*p{0})\n", true)]
    [InlineData("    var y = 1\n    var w: ref/i32 = y@ref\n    let p0 = w@raw\n", "    let p{1} = p{0}@raw\n", "    _ = p{1} == null\n", true)]
    public void TheWorkScalesWithTheProgram(string head, string each, string after, bool dense, string operand = "")
    {
        var (work, bytes) = this.Scaled(head, each, after, operand);
        Assert.True(Grows(work, dense ? 5 : 2), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, dense ? 5 : 2), $"bytes {string.Join(", ", bytes)}");
    }

    // V16 and A3 E13 (5) (G135): a location reads each (holder, node) entry once however many operands take it, its entries by holder, and
    // each set's content and an output's referents once, so N copies of a holder carrying K Loans in one operation, a call moving N
    // arguments with K holders live across it, N retention rows of one argument carrying K Loans and N rows into one output carrying K cost
    // N + K, not N × K. N × K is linear in each, so the rule is the mixed difference over N = 32, 128 and K = 32, 64, which cancels the
    // terms in N or K alone: within a quarter step per (N, K) pair. The work and ν are also linear in N at K = 32 and, where the shape
    // builds its K holders linearly (the copies), in K at N = 32.
    [Theory]
    [InlineData(
        "func shape()\n«K:    var x{0} = {0}\n»    let t = («K:x{0}@ref, »1)\n    var z = 1\n    let q = z@ref\n    _ = («N:t, »z)\n    inspect(q)\n",
        false)]
    [InlineData(
        "func takeN(«N:p{0}: uniq/i32, »n: i32) => ()\nfunc shape(«N:s{0}: uniq/i32, »m: i32)\n    var x = 1\n    let r = x@ref\n«K:    let c{0} = r\n»" +
        "    var z = 1\n    let q = z@ref\n    takeN(«N:s{0}@move, »z)\n    inspect(q)\n«K:    inspect(c{0})\n»",
        true)]
    [InlineData(
        "func keepN(«N:v{0}: ref/i32 during a, »n: i32) -> ref/i32 during a => v0\nfunc shape(flag: bool)\n«K:    var x{0} = {0}\n»" +
        "    var r = x0@ref\n«K:    if flag\n        r = x{0}@ref\n»    let y = keepN(«N:r, »0)\n    inspect(y)\n",
        false)]
    [InlineData(
        "func fillN(out: uniq/(ref/i32 during a), «N:v{0}: ref/i32 during a, »n: i32) => ()\n" +
        "func shape(flag: bool, v: ref/i32 during a, «K:o{0}: uniq/(ref/i32 during a), »m: i32)\n    var p = o0@follow@uniq\n" +
        "«K:    if flag\n        p = o{0}@follow@uniq\n»    fillN(p, «N:v, »0)\n",
        false)]
    public void ALocationReadsEachEntryOnce(string template, bool linearInK)
    {
        var at = new Dictionary<(int N, int K), (long Work, long Bytes)>();
        foreach (var (n, k) in new[] { (32, 32), (64, 32), (128, 32), (32, 64), (128, 64), (32, 128) })
        {
            at[(n, k)] = this.Measured(Prelude + Expand(template, n, k));
        }

        var mixed = at[(128, 64)].Work - at[(128, 32)].Work - at[(32, 64)].Work + at[(32, 32)].Work;
        long[] work = [at[(32, 32)].Work, at[(64, 32)].Work, at[(128, 32)].Work];
        long[] bytes = [at[(32, 32)].Bytes, at[(64, 32)].Bytes, at[(128, 32)].Bytes];
        long[] inK = [at[(32, 32)].Work, at[(32, 64)].Work, at[(32, 128)].Work];
        output.WriteLine($"N at K 32: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        output.WriteLine($"K at N 32: work {string.Join(", ", inK)}; mixed {mixed}");
        Assert.True(mixed <= 96 * 32 / 4, $"mixed {mixed}");
        Assert.True(Grows(work, 2) && Grows(bytes, 2), $"work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(!linearInK || Grows(inK, 2), $"work in K {string.Join(", ", inK)}");
    }

    // V16 (A3 6.2, 6.3, shape 7, K1): a copy chain of K holders in a loop, copied against the chain's order, carries the Loan one holder
    // further per pass, so the passes grow with K and the work with K × K, the bound's (passes − 1) × locations term; the holders share two
    // Loans, so the Loan-set bytes (ν) stay constant.
    [Fact]
    public void AnInLoopCopyChainTakesAPassPerHolder()
    {
        const string Chain = "func shape(flag: bool)\n    var x = 1\n    var y = 2\n    var r0 = x@ref\n    let q = y@ref\n«K:    var r{1} = q\n»" +
            "    while flag\n«R:        r{1} = r{0}\n»«K:    inspect(r{1})\n»";
        var runs = new[] { 32, 64, 128 }.Select(k => this.Measured(Prelude + Expand(Chain, 0, k))).ToArray();
        long[] work = [.. runs.Select(static x => x.Work)];
        long[] bytes = [.. runs.Select(static x => x.Bytes)];
        output.WriteLine($"K 32, 64, 128: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, 2), $"bytes {string.Join(", ", bytes)}");
    }

    // V16 (A3 6.2, 6.3, shape 8): K holders whose nodes overlap (each joins the one before with one more Loan) live across N writes of a
    // disjoint Field, each a candidate access that scans the distinct live nodes, K × K / 2 entries: the work is linear in N at K = 32, and
    // the cost of 32 more writes is quadratic in K, the bound's accesses × node sizes term (building the nested nodes is cubic there too,
    // each store a candidate scanning the nodes before it, and its ν quadratic, the live Loans per point).
    [Fact]
    public void AnAccessScansTheDistinctLiveNodes()
    {
        const string Overlap = "func shape(flag: bool)\n    var p0 = Pair.init()\n«K:    var p{1} = Pair.init()\n»    let h0 = p0.a@ref\n" +
            "«K:    let h{1} = if flag => h{0} else => p{1}.a@ref\n»«N:    p0.b = {0}\n»«K:    inspect(h{1})\n»";
        long[] inN = [.. new[] { 32, 64, 128 }.Select(n => this.Measured(Prelude + Expand(Overlap, n, 32)).Work)];
        long[] writes = [.. new[] { 32, 64, 128 }.Select(k =>
            this.Measured(Prelude + Expand(Overlap, 64, k)).Work - this.Measured(Prelude + Expand(Overlap, 32, k)).Work)];
        output.WriteLine($"N at K 32: work {string.Join(", ", inN)}; 32 writes at K 32, 64, 128: {string.Join(", ", writes)}");
        Assert.True(Grows(inN, 2), $"work in N {string.Join(", ", inN)}");
        Assert.True(Grows(writes, 5), $"writes in K {string.Join(", ", writes)}");
    }

    // A3 12 E8, V16 (review B3): destroying a Move Path ends the Loans of its subtree as one set, made once per path, so N borrow/write pairs
    // of one root make Loan-set bytes (ν) linearly, though their work is dense ((ended points) × holders).
    [Fact]
    public void TheEndedSetsOfAMovePathAreMadeOnce()
    {
        var (_, bytes) = this.Scaled("    var x = 0\n    var r = x@ref\n", "    x = {0}\n    r = x@ref\n    inspect(r)\n", string.Empty);
        Assert.True(Grows(bytes, 2), $"bytes {string.Join(", ", bytes)}");
    }

    // V16 (re-review): a view's work is its own: after a body of 512 Loans, a small body takes the steps it takes alone, since a view clears
    // only the checks' stamps it uses.
    [Fact]
    public void AViewsWorkIsItsOwn()
    {
        var text = new StringBuilder(Prelude).Append("func big()\n    var x = 0\n");
        for (var n = 0; n < 512; n++)
        {
            text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "    let r{0} = x@ref\n", n);
        }

        var c = MirBuildTest.Compile(text.Append("    inspect(r0)\nfunc small(a: i32)\n    var y = a\n    let q = y@ref\n    inspect(q)\n").ToString());
        var program = c.BuildMir();
        var (alone, after) = (new Stratum(c.Types), new Stratum(c.Types));
        alone.Run(program, Body(program, "small"));
        after.Run(program, Body(program, "big"));
        var work = after.Borrows.Work;
        after.Run(program, Body(program, "small"));
        Assert.Equal(alone.Borrows.Work, after.Borrows.Work - work);
    }

    // V16: A5 on a program version it checked before allocates nothing: its sets, tables, call rows (an intrinsic's too), addresses (a raw
    // Ref's, a `during self` layer's), a Chain row's closure, the own-step Warning's tags and the dataflow keep their storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + ChainShapes + "func chained()\n    var y = 1\n    var w: ref/i32 = y@ref\n" +
            "    var h: raw/u8 = w@raw@raw/u8\n    var h2: raw/(raw/u8) = h@raw\n    let r = getRRR(h2@raw)\n    usep(r)\n" +
            "func addressed()\n    var y = 1\n    var w: ref/i32 = y@ref\n    let p = w@raw\n    y = 2\n" +
            "    _ = p == null\nfunc selfRead()\n    var y = 1\n    var b = Buf<ref/i32>.init(4)\n    b.push(y@ref)\n    let p = b.data\n    y = 2\n" +
            "    _ = p == null\nfunc loopCarry(flag: bool)\n    var x = 1\n    var y = 2\n    var r = y@ref\n    while flag\n" +
            "        x = 3\n        inspect(r)\n        r = x@ref\nfunc stored()\n    var x = 1\n    var y = 2\n    var slot = y@ref\n" +
            "    put(slot@uniq, x@ref)\n    x = 3\n    inspect(slot)\nfunc reserved()\n    var tasks: Array<Task> = []\n    tasks.append(tasks.remove(0))\n" +
            "func written()\n    var x = 1\n    let v = x@ref\n    x += 1\n    inspect(v)\nfunc destroyed()\n    var x = 1\n    var y = 2\n" +
            "    var s = D.init(r: x@uniq)\n    let c = s.r@follow@uniq\n    s = D.init(r: y@uniq)\n    c@follow = 1\nfunc escaped()\n" +
            "    let fallback = Data.init()\n    let packet: Packet = .Data(Data.init())\n    var saved = fallback@ref\n    match packet\n" +
            "        .Data(let d) if save(saved@uniq, d) => ()\n        _ => ()\n    inspect(saved)\nfunc raw()\n    var x = 1\n" +
            "    let p: raw/(ref/i32) = Raw.allocate(1)\n    unsafe => Raw.initialize(p, x@ref)\n    x = 2\nfunc departed()\n    var b = Buf<i32>.init(4)\n" +
            "    let p = b.data\n    let c = b@move\n    _ = p == null\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(9, program.Problems.All.ToArray().Count(static x => LoanCodes.Contains(x.Code)));
        Assert.Single(program.Problems.All.ToArray(), static x => x.Code == DiagnosticCode.OwnStepDeparture_Kd && x.Subject.Module == 0);
    }

    // Design §8 (DECISIONS R4-M154): the Loan stratum's second pass over a program allocates nothing, whichever role its two swapped Loan
    // arrays then hold (MirBorrows.Order keeps them one capacity; Milestone7 regrew 32 Loans before). The standard window
    // (AllocationMeasurement) warms up before it measures, which absorbs a second-pass regrow, so this pass is measured on its own.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void TheLoanStratumsSecondPassAllocatesNothing()
    {
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone7");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        _ = stratum.Run(program);
        Assert.Equal(0, stratum.Run(program));
    }

    // V16 (DECISIONS R4-M140): the holding memo answers for one program version; a rebind's version clears it, and A5's views over the new
    // version fill it again within the capacity it keeps, so a warm rebind, its MIR build and A5 over a program and the library allocate
    // nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ARebindRefillsTheHoldingMemoWithoutAllocating()
    {
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone34");
        var stratum = new Stratum(c.Types);
        stratum.Run(c.BuildMir());
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            stratum.Run(c.BuildMir());
        }));
    }

    // G308 D3, SPEC 13.4: the left operand of a comparison of a DAG Tuple with string leaves is inspected through a shared Loan live while
    // the right operand is evaluated, so moving it there is rejected; comparing it with another value is accepted.
    [Fact]
    public void ADagComparisonsInspectionLoanRejectsAMoveOfItsOperand()
    {
        const string Tuple = "(((\"a\", 1), (\"b\", 2)), ((\"c\", 3), (\"d\", 4)))";
        Assert.Equal(
            ["bad: t"],
            Problems($"func take<T>(value: T) -> T => value@move\nfunc bad() -> bool\n    let t = {Tuple}\n    return t == take(t@move)\n" +
                $"func good() -> bool\n    let t = {Tuple}\n    let other = {Tuple}\n    return t == other\n"));
    }

    // The Loan problems of a program's own bodies.
    private static string[] Problems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        return Problems(c, Analyze(c), LoanCodes);
    }

    // The Loan problems of a program Binding rejects, whose failed nodes the MIR builds as Opaque values (SPEC 23.1.2).
    private static string[] RejectedProblems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        Assert.NotEmpty(c.Binding.Issues);
        var program = c.BuildMir();
        new Stratum(c.Types).Run(program);
        return Problems(c, program, LoanCodes);
    }

    // A program's problems of some codes in its own bodies: function and the subject's first line, with the code unless it is a comparison's.
    private static string[] Problems(Compilation c, MirProgram program, DiagnosticCode[] codes)
    {
        return [.. program.Problems.All.ToArray().Where(x => codes.Contains(x.Code) && x.Subject.Module == 0)
            .Select(x => $"{Function(c.SyntaxOf(x.Subject))}: {Line(c, x.Subject)}{Code(x.Code)}")];

        static string Code(DiagnosticCode code) => code == DiagnosticCode.ComparisonLoanConflict_Kd ? string.Empty : " " + code;
    }

    // The function a node lies in: the nearest enclosing declaration's name.
    private static string? Function(Koto node) => node is FunctionKoto function ? function.Name : node.Parent is { } parent ? Function(parent) : null;

    private static MirProgram Analyze(Compilation c) => AnalyzeUnsupported(c, []);

    // A5 over a program whose only Binding records are the located Unsupported_Kd of its Borrows of static Places, which Binding reports so
    // until P42 (R4-M90 PQ-1, DECISIONS BU-25), at the Borrows (an implicit one's operand) whose texts the test lists once per Borrow.
    private static MirProgram AnalyzeUnsupported(Compilation c, string[] borrows)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.All(c.Binding.Issues, static x => Assert.Equal(DiagnosticCode.Unsupported_Kd, x.Code));
        Assert.Equal(borrows.Order(StringComparer.Ordinal), c.Binding.Issues.Select(static x => $"{x.Node}").Order(StringComparer.Ordinal));
        new Stratum(c.Types).Run(program);
        return program;
    }

    private static MirBody Body(MirProgram program, string name) => program.Bodies.ToArray().Single(x => x.Key.Decl?.Name == name);

    private static string Line(Compilation c, SourceRef site) => c.SyntaxOf(site).ToString().Split('\n')[0].Trim();

    // A shape's source at N and K: each «N:text» or «K:text» run repeated N or K times, «R:text» K times in reverse order, {0} the index
    // and {1} the index plus one.
    private static string Expand(string template, int n, int k)
    {
        MatchEvaluator runs = x =>
        {
            var indices = Enumerable.Range(0, x.Groups[1].Value == "N" ? n : k);
            return string.Concat((x.Groups[1].Value == "R" ? indices.Reverse() : indices)
                .Select(i => string.Format(System.Globalization.CultureInfo.InvariantCulture, x.Groups[2].Value, i, i + 1)));
        };
        return Regex.Replace(template, "«([NKR]):(.*?)»", runs, RegexOptions.Singleline);
    }

    // Whether a measure grows from N = 64 to 128 at most a factor times as much as from 32 to 64, with a constant's slack.
    private static bool Grows(long[] measure, double factor) => measure[2] - measure[1] <= (factor * (measure[1] - measure[0])) + 64;

    // A5's work and Loan-set bytes on a shape's body at N = 32, 64 and 128: its head, then N each lines, a Tuple of N operands where one is
    // given, and N after lines ({0} the index, {1} the index plus one), with no Loan problem.
    private (long[] Work, long[] Bytes) Scaled(string head, string each, string after, string operand = "")
    {
        var (sizes, work, bytes) = (new[] { 32, 64, 128 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder(Prelude).Append("func shape(flag: bool)\n").Append(head);
            for (var pass = 0; pass < 2; pass++)
            {
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, pass == 0 ? each : after, n, n + 1);
                }

                if (pass == 0 && operand.Length > 0)
                {
                    text.Append("    _ = (").AppendJoin(", ", Enumerable.Repeat(operand, sizes[i])).Append(")\n");
                }
            }

            (work[i], bytes[i]) = this.Measured(text.ToString());
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        return (work, bytes);
    }

    // A5's work and Loan-set bytes on a program with no Loan problem.
    private (long Work, long Bytes) Measured(string source)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(c.Binding.Issues);
        var stratum = new Stratum(c.Types);
        stratum.Run(program);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => LoanCodes.Contains(x.Code));
        return (stratum.Borrows.Work, stratum.Borrows.Bytes);
    }

    // A5 over every view of every body of a built program, as the driver's step 4 runs it over the views A1 and A2 completed (R4-M57),
    // after its own A4's summaries and keys of the program version (the driver's steps 2 and 3, once per version; R4-M92). BuildMir has
    // already run the driver, so a run here adds only the problems of views the driver left out and merges the others into the driver's
    // (MirReport keeps the first problem and its facts); Work and Bytes count this instance's A5 runs only.
    private sealed class Stratum
    {
        private readonly MirDataflow flow = new(new MirReach());
        private readonly MirMoveData moves = new();
        private readonly MirReport obligations = new(); // A4's obligation problems, which A5 does not read.
        private readonly MirCases cases;
        private readonly MirEffects effects;
        private readonly MirOrigins origins; // The Origin stratum, which judges each body first, as the driver's step 4 does (rule W).
        private ulong version; // The program version A4 last solved.

        internal Stratum(TyCtxt types)
        {
            this.cases = new(types);
            this.effects = new(types, this.cases, this.flow);
            this.origins = new(types, this.cases);
            this.Borrows = new(types, this.flow, this.moves, this.cases, this.effects, this.origins);
        }

        internal MirBorrows Borrows { get; }

        // Each run returns what the Loan stratum's runs allocated on the thread (the Origin stratum's Judge apart).
        internal long Run(MirProgram program)
        {
            long allocated = 0;
            foreach (var body in program.Bodies)
            {
                allocated += this.Run(program, body);
            }

            return allocated;
        }

        internal long Run(MirProgram program, MirBody body)
        {
            this.Summaries(program);
            this.flow.Begin(body);
            this.moves.Build(body, this.flow);
            this.origins.Judge(body, MirCases.Count(body), program.Problems);
            long allocated = 0;
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                this.cases.Select(body, view);
                var before = GC.GetAllocatedBytesForCurrentThread();
                this.Borrows.Run(program, body, view);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        }

        // A4 over a program version it has not solved: each body's views' local facts, then the summaries and keys.
        private void Summaries(MirProgram program)
        {
            if (program.Version == this.version)
            {
                return;
            }

            this.version = program.Version;
            foreach (var body in program.Bodies)
            {
                this.flow.Begin(body);
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    this.cases.Select(body, view);
                    this.effects.Local(program, body, view);
                }
            }

            this.obligations.Clear();
            this.effects.Run(program, this.obligations);
        }
    }
}
