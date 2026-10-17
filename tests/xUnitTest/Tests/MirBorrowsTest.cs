// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>A5's Loan stratum on MIR (SPEC 15.6; design A3 with its 12 errata; docs/dev/DECISIONS.md R4-M57): each rule with valid and
/// invalid programs, through the problems <see cref="MirBorrows"/> adds over every body view of a built program, each as its function and
/// the first line of its subject.</summary>
public sealed class MirBorrowsTest(ITestOutputHelper output)
{
    private const string Prelude = """
        func inspect<T>(value: ref/T) => ()
        func pin(slot: ref/(ref/i32 during a)) -> ref/i32 during slot => slot@follow
        func keep(value: ref/i32) -> ref/i32 during value => value
        func put(slot: uniq/(ref/i32 during a), value: ref/i32 during a) => slot@follow = value
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

    /// <summary>Gets the variants of the SPEC examples these rules decide: family F3's, SPEC 14.8.3's, 15.6.6's and 15.6.7's.</summary>
    public static TheoryData<string> SpecVariants => [.. DiagnosticCorpus.SpecCases
        .Where(static x => x.Family == "F3" || x.Section is "14.8.3" or "15.6.6" or "15.6.7")
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
    // into the holders its Loans name (P10).
    [Fact]
    public void AStoredValueIsCheckedInTransit()
        => Assert.Equal(
            ["selfStore: x = pin(x@ref)", "partStore: s.r = pin(s.r@ref)", "throughStore: x = 3"],
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
    // borrow, after its receiver's reservation met the same Loan (a formation and an activation, A6's merge: unit 1c).
    [Fact]
    public void ADestroyingCallChecksWhatItDestroys()
        => Assert.Equal(
            [
                "d26a: Kimi.Intrinsics.replace(s@uniq, with: C.init(r: y@uniq))", "d26b: arr", "d26b: arr.clear() CallActivationConflict_Kd",
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
    // the ending; one whose Type observes nothing, or that a shared reference leads past, is shallow.
    [Fact]
    public void AnObservingDestructionIsAnExclusiveDeepUse()
        => Assert.Equal(
            ["o1566: s = D.init(r: y@uniq)", "o24a: s = D.init(r: s.r@follow@uniq)", "o29b: p@follow = D.init(r: p.r@follow@uniq)"],
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
    // Unsupported_Kd there, and no Loan problem; a body without a Loan, which no conflict can depend on, reports neither.
    [Fact]
    public void AnUnclassifiedDestructionOrCallIsUnsupported()
    {
        var c = MirBuildTest.Compile(Prelude + "func deep(n: Nest<ref/i32 during a>)\n    let r = n@ref\n" +
            "func unborrowed(n: Nest<ref/i32 during a>)\n    let r = 1\nfunc made()\n    var y = 1\n    let n = Nest<ref/i32>.init(value: y@ref)\n" +
            "func unlent(x: ref/i32)\n    let n = Nest<ref/i32>.init(value: x)\n");
        var program = Analyze(c);
        Assert.Equal(
            ["Unsupported_Kd func deep(n: Nest<ref/i32 during a>)", "Unsupported_Kd Nest<ref/i32>.init(value: y@ref)"],
            program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0).Select(x => $"{x.Code} {Line(c, x.Subject)}"));
    }

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

    // The SPEC examples of the guard, reservation and destruction sections (tests/diagnostics/spec-cases.json: family F3, SPEC 14.8.3,
    // 15.6.6 and 15.6.7) get exactly the Loan problems their records give, on the MIR (not Landed until the driver runs A5, unit 1b-5).
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
    // an activation from its call on; none in a later chain), or the store that carries the Loan; and an activation's reservation.
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
                "x = 2: Anchor x@ref, Use inspect(r)", "x = pin(x@ref): Anchor x@ref, Store x = pin(x@ref)",
                "two(a@uniq, a@ref): Anchor a@ref, Use two(a@uniq, a@ref), Reservation a@uniq", "let d: Anchor let d",
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
    // and the work and the Loan-set bytes (ν) are dense, not cubic.
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
    public void TheWorkScalesWithTheProgram(string head, string each, string after, bool dense)
    {
        var (work, bytes) = this.Scaled(head, each, after);
        Assert.True(Grows(work, dense ? 5 : 2), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, dense ? 5 : 2), $"bytes {string.Join(", ", bytes)}");
    }

    // A3 12 E8, V16 (review B3): destroying a Move Path ends the Loans of its subtree as one set, made once per path, so N borrow/write pairs
    // of one root make Loan-set bytes (ν) linearly, though their work is dense ((ended points) × holders).
    [Fact]
    public void TheEndedSetsOfAMovePathAreMadeOnce()
    {
        var (_, bytes) = this.Scaled("    var x = 0\n    var r = x@ref\n", "    x = {0}\n    r = x@ref\n    inspect(r)\n", string.Empty);
        Assert.True(Grows(bytes, 2), $"bytes {string.Join(", ", bytes)}");
    }

    // V16: A5 on a program version it checked before allocates nothing: its sets, tables, call rows (an intrinsic's too) and the dataflow
    // keep their storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + "func loopCarry(flag: bool)\n    var x = 1\n    var y = 2\n    var r = y@ref\n    while flag\n" +
            "        x = 3\n        inspect(r)\n        r = x@ref\nfunc stored()\n    var x = 1\n    var y = 2\n    var slot = y@ref\n" +
            "    put(slot@uniq, x@ref)\n    x = 3\n    inspect(slot)\nfunc reserved()\n    var tasks: Array<Task> = []\n    tasks.append(tasks.remove(0))\n" +
            "func written()\n    var x = 1\n    let v = x@ref\n    x += 1\n    inspect(v)\nfunc destroyed()\n    var x = 1\n    var y = 2\n" +
            "    var s = D.init(r: x@uniq)\n    let c = s.r@follow@uniq\n    s = D.init(r: y@uniq)\n    c@follow = 1\nfunc escaped()\n" +
            "    let fallback = Data.init()\n    let packet: Packet = .Data(Data.init())\n    var saved = fallback@ref\n    match packet\n" +
            "        .Data(let d) if save(saved@uniq, d) => ()\n        _ => ()\n    inspect(saved)\nfunc raw()\n    var x = 1\n" +
            "    let p: raw/(ref/i32) = Raw.allocate(1)\n    unsafe => Raw.initialize(p, x@ref)\n    x = 2\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(7, program.Problems.All.ToArray().Count(static x => LoanCodes.Contains(x.Code)));
    }

    // The Loan problems of a program's own bodies: function and the subject's first line, with the code unless it is a comparison's.
    private static string[] Problems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        var program = Analyze(c);
        return [.. program.Problems.All.ToArray().Where(static x => LoanCodes.Contains(x.Code) && x.Subject.Module == 0)
            .Select(x => $"{Function(c.SyntaxOf(x.Subject))}: {Line(c, x.Subject)}{Code(x.Code)}")];

        static string? Function(Koto node) => node is FunctionKoto function ? function.Name : node.Parent is { } parent ? Function(parent) : null;
        static string Code(DiagnosticCode code) => code == DiagnosticCode.ComparisonLoanConflict_Kd ? string.Empty : " " + code;
    }

    private static MirProgram Analyze(Compilation c)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(c.Binding.Issues);
        new Stratum(c.Types).Run(program);
        return program;
    }

    private static string Line(Compilation c, SourceRef site) => c.SyntaxOf(site).ToString().Split('\n')[0].Trim();

    // Whether a measure grows from N = 64 to 128 at most a factor times as much as from 32 to 64, with a constant's slack.
    private static bool Grows(long[] measure, int factor) => measure[2] - measure[1] <= (factor * (measure[1] - measure[0])) + 64;

    // A5's work and Loan-set bytes on a shape's body at N = 32, 64 and 128: its head, then N each lines and N after lines, with no Loan problem.
    private (long[] Work, long[] Bytes) Scaled(string head, string each, string after)
    {
        var (sizes, work, bytes) = (new[] { 32, 64, 128 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder(Prelude).Append("func shape(flag: bool)\n").Append(head);
            for (var pass = 0; pass < 2; pass++)
            {
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, pass == 0 ? each : after, n);
                }
            }

            var c = MirBuildTest.Compile(text.ToString());
            var stratum = new Stratum(c.Types);
            stratum.Run(c.BuildMir());
            Assert.DoesNotContain(c.BuildMir().Problems.All.ToArray(), static x => LoanCodes.Contains(x.Code));
            (work[i], bytes[i]) = (stratum.Borrows.Work, stratum.Borrows.Bytes);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        return (work, bytes);
    }

    // A5 over every body view of a built program, as the driver's step 4 will run it (R4-M57).
    private sealed class Stratum
    {
        private readonly MirDataflow flow = new(new MirReach());
        private readonly MirMoveData moves = new();
        private readonly MirCases cases;

        internal Stratum(TyCtxt types)
        {
            this.cases = new(types);
            this.Borrows = new(types, this.flow, this.moves, this.cases);
        }

        internal MirBorrows Borrows { get; }

        internal void Run(MirProgram program)
        {
            foreach (var body in program.Bodies)
            {
                this.flow.Begin(body);
                this.moves.Build(body, this.flow);
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    this.cases.Select(body, view);
                    this.Borrows.Run(program, body, view);
                }
            }
        }
    }
}
