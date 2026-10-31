// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The shared acceptance tests of PLAN G264 U1 (WORKFLOW Q11; written from the problem statement before either candidate):
/// a destruction retains as a call of each <c>drop</c> it runs, whose <c>self</c> is a <c>uniq/Self</c> receiver referring to the
/// destroyed part, and a published operation's cleanup is the destruction its row lists, composed at the call (SPEC 15.6.4, 16.3.1,
/// 16.3.3, 16.4, 4.9.6). Storage the body does not own follows the whole holder (SPEC 15.6, the owner's 「(B)」 of 2026-10-30, DECISIONS
/// BU-67): a value an untied constructor made reaches it through every raw Field, so a Writer's drop that stores its q through its p
/// stores y's Loan there, where it may be used after every point. Each row names the destruction site of the G264 design's §5.2 it
/// covers. The Bad rows of the reassignment, scope, discard and part sites are accepted on dev (A5 composes no drop at a Drop) and fail
/// by design until U1; every other row holds on dev and must hold after.</summary>
public sealed partial class MirBorrowsTest
{
    // The forms' Types: Writer stores its q through its raw slot p when destroyed (SPEC 15.6.4's example), Quiet holds the same Fields
    // without a drop; Outer holds a Writer by value and OuterD's own drop rewrites its inner Writer's slot (two drops, both composed;
    // no row observes their order); BaseW's drop runs for a Derived's base part; Holder<T> is a generic Type with the drop; Setter's drop writes its
    // borrowed value through its borrowed target (no raw layer, so nothing reaches storage the body does not own); ELeaf is a sealed leaf
    // with Writer's drop under an open EBase without one (a handle to EBase destroys erased, SPEC 16.3.3); WriterS states its raw layers'
    // Origin through a Loan of the slot it writes (BU's p6 Lens spelling, SPEC 5.7), so its drop stores only into that slot; WKey is a
    // Dictionary key with Writer's drop.
    private const string DropShapes = """
        func inspect<T>(value: ref/T) => ()
        func usep<T>(p: raw/T) => ()
        func consume<T>(x: T) => ()
        struct Writer
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        struct Quiet
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
        struct Outer
            public var inner: Writer
            public var n: i32
            public init(q: raw/u8)
                self.inner = Writer.init(q: q)
                self.n = 0
        struct OuterD
            public var inner: Writer
            public var r: raw/(raw/u8)
            public init(q: raw/u8)
                self.inner = Writer.init(q: q)
                self.r = null
            drop
                self.inner.p = self.r
        open struct BaseW
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        struct Derived : BaseW
            public init(q: raw/u8) : base(q: q) => ()
        struct Holder<T>
            public var item: T
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(item: T, q: raw/u8)
                self.item = item@move
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        struct Setter {a}
            public var target: uniq/(ref/i32 during a) during a
            public var value: ref/i32 during a
            public init(target: uniq/(ref/i32 during a) during a, value: ref/i32 during a)
                self.target = target
                self.value = value
            drop
                self.target@follow = self.value
        open struct EBase
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
        struct ELeaf : EBase
            public init(q: raw/u8) : base(q: q) => ()
            drop
                unsafe => *self.p = self.q
        struct WriterS {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public var p: raw/(raw/u8) during a
            public var q: raw/u8 during a
            public init(target: uniq/(raw/u8) during a)
                self.p = target@follow@raw
                self.loan = Loan.init(target)
                self.q = null
            drop
                unsafe => *self.p = self.q
        struct WKey
            Self is Equatable
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public var n: i32
            public init(n: i32, q: raw/u8)
                self.p = null
                self.q = q
                self.n = n
            public func equals(self: ref/Self, other: ref/Self) -> bool => self.n == other.n
            drop
                unsafe => *self.p = self.q

        """;

    // The design's forms (R4 notes g264\probes: d2src d1-sites, d2-parts, d3-arrays, d4-generic, d5-borrow, d10-escape; d2bu d93-drop2;
    // r5 lit; new k1-key), with Ok twins valid under SPEC 15.6's whole-holder text where one exists, and the published destroying
    // calls' null twins. Bad writes y before the value a drop stored y's Loan into is used; Ok stores nothing that carries y, or has no
    // later access.
    private const string DropForms = """
        func localBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = Writer.init(q: null)
            y = 5
            usep(slot)
        func localReachBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = Writer.init(q: null)
            usep(slot)
            y = 5
        func quietOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Quiet.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = Quiet.init(q: null)
            y = 5
            usep(slot)
        func nullQOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            x = Writer.init(q: null)
            y = 5
            usep(slot)
        func scopeBad(b: bool)
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            if b
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
            y = 5
            usep(slot)
        func endReachBad()
            var slot: raw/u8 = null
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
        func endParamOk(y: uniq/i32)
            let w = y@follow@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
        func discardBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            _ = x@move
            y = 5
            usep(slot)
        func setterScopeBad(b: bool)
            var z: i32 = 0
            var t: ref/i32 = z@ref
            var y: i32 = 1
            if b
                var s = Setter.init(target: t@uniq, value: z@ref)
                s.value = y@ref
            y = 5
            inspect(t@ref)
        func setterScopeContentBad(b: bool)
            var z: i32 = 0
            var t: ref/i32 = z@ref
            var y: i32 = 1
            if b
                var s = Setter.init(target: t@uniq, value: z@ref)
                s.value = y@ref
            inspect(t@ref)
            y = 5
        func escapeBad(p: raw/(raw/u8))
            var y: i32 = 1
            let w = y@ref
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = p
        func escapeOk(p: raw/(raw/u8))
            var x = Writer.init(q: null)
            x.p = p
        func fieldBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o = Outer.init(q: w@raw@raw/u8)
            o.inner.p = slot@raw
            o = Outer.init(q: null)
            y = 5
            usep(slot)
        func orderBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o = OuterD.init(q: w@raw@raw/u8)
            o.r = slot@raw
            o = OuterD.init(q: null)
            y = 5
            usep(slot)
        func baseBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var d = Derived.init(q: w@raw@raw/u8)
            d.p = slot@raw
            d = Derived.init(q: null)
            y = 5
            usep(slot)
        func baseOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var d = Derived.init(q: null)
            d.p = slot@raw
            d = Derived.init(q: null)
            y = 5
            usep(slot)
        func tupleBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var t = (x@move, 1)
            t = (Writer.init(q: null), 2)
            y = 5
            usep(slot)
        func optionBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var o: Option<Writer> = .Some(x@move)
            o = .None
            y = 5
            usep(slot)
        func holderBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var h = Holder<i32>.init(item: 1, q: w@raw@raw/u8)
            h.p = slot@raw
            h = Holder<i32>.init(item: 2, q: null)
            y = 5
            usep(slot)
        func closureBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            do
                let f = func [x@move] () => ()
            y = 5
            usep(slot)
        func arrReassignBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a = []
            y = 5
            usep(slot)
        func litScopeBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            do
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                let a: Array<Writer> = [x@move]
            y = 5
            usep(slot)
        func tempOnlyBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            inspect(Writer.init(q: w@raw@raw/u8))
            y = 5
        func tempOnlyNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            inspect(Writer.init(q: null))
            y = 5
        func statedBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var spare: raw/u8 = null
            var x = WriterS.init(target: slot@uniq)
            x.q = w@follow@raw@raw/u8
            x = WriterS.init(target: spare@uniq)
            y = 5
            usep(slot)
        func statedSlotBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var spare: raw/u8 = null
            var x = WriterS.init(target: slot@uniq)
            x.q = w@follow@raw@raw/u8
            x = WriterS.init(target: spare@uniq)
            usep(slot)
            y = 5
        func sealedLateBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o: obj/ELeaf = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))
            o.q = w@raw@raw/u8
            o.p = slot@raw
            o = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))
            y = 5
            usep(slot)
        func sealedLateNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o: obj/ELeaf = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))
            o.p = slot@raw
            o = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))
            y = 5
            usep(slot)
        func erasedLateBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o: obj/ELeaf = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))
            o.q = w@raw@raw/u8
            o.p = slot@raw
            var b: obj/EBase = o@move@obj/EBase
            b = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))@obj/EBase
            y = 5
            usep(slot)
        func erasedLateNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o: obj/ELeaf = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))
            o.p = slot@raw
            var b: obj/EBase = o@move@obj/EBase
            b = Kimi.Intrinsics.makeObj<ELeaf>(ELeaf.init(q: null))@obj/EBase
            y = 5
            usep(slot)
        func rcCellBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var cell: raw/u8 = null
            var x = Writer.init(q: null)
            x.q = cell@raw@raw/u8
            x.p = slot@raw
            var h = x@move@rc
            cell = w@raw@raw/u8
            h = Writer.init(q: null)@rc
            y = 5
            usep(slot)
        func rcCellQuietOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var cell: raw/u8 = null
            var x = Quiet.init(q: null)
            x.q = cell@raw@raw/u8
            x.p = slot@raw
            var h = x@move@rc
            cell = w@raw@raw/u8
            h = Quiet.init(q: null)@rc
            y = 5
            usep(slot)
        func rcCellNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var cell: raw/u8 = null
            var x = Writer.init(q: null)
            x.q = cell@raw@raw/u8
            x.p = slot@raw
            var h = x@move@rc
            h = Writer.init(q: null)@rc
            y = 5
            usep(slot)
        func dictReassignBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var d: Dictionary<i32, Writer> = [:]
            _ = d.tryInsert(1, x@move)
            d = [:]
            y = 5
            usep(slot)
        func dictReassignNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var d: Dictionary<i32, Writer> = [:]
            _ = d.tryInsert(1, x@move)
            d = [:]
            y = 5
            usep(slot)
        func dictKeyReassignBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var k = WKey.init(n: 1, q: w@raw@raw/u8)
            k.p = slot@raw
            var d: Dictionary<WKey, i32> = [:]
            _ = d.tryInsert(k@move, 1)
            d = [:]
            y = 5
            usep(slot)
        func dictKeyReassignNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var k = WKey.init(n: 1, q: null)
            k.p = slot@raw
            var d: Dictionary<WKey, i32> = [:]
            _ = d.tryInsert(k@move, 1)
            d = [:]
            y = 5
            usep(slot)
        func fixedReassignBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var f: [2 of Writer] = [x@move, Writer.init(q: null)]
            f = [Writer.init(q: null), Writer.init(q: null)]
            y = 5
            usep(slot)
        func fixedReassignNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var f: [2 of Writer] = [x@move, Writer.init(q: null)]
            f = [Writer.init(q: null), Writer.init(q: null)]
            y = 5
            usep(slot)
        func arrClearBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.clear()
            y = 5
            usep(slot)
        func replaceBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            Kimi.Intrinsics.replace(x@uniq, with: Writer.init(q: null))
            y = 5
            usep(slot)
        func keyParamBad(d: uniq/Dictionary<WKey, i32>)
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var k = WKey.init(n: 1, q: w@raw@raw/u8)
            k.p = slot@raw
            _ = d.insertOrReplace(k@move, 2)
            y = 5
            usep(slot)
        func clearNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.clear()
            y = 5
            usep(slot)
        func truncateNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.truncate(0)
            y = 5
            usep(slot)
        func removeAllNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.removeAll(matching: func (v) => true)
            y = 5
            usep(slot)
        func replaceNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            Kimi.Intrinsics.replace(x@uniq, with: Writer.init(q: null))
            y = 5
            usep(slot)
        func keyParamNullOk(d: uniq/Dictionary<WKey, i32>)
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var k = WKey.init(n: 1, q: null)
            k.p = slot@raw
            _ = d.insertOrReplace(k@move, 2)
            y = 5
            usep(slot)

        """;

    // SPEC 15.6.4, 16.3.1-16.3.3, 16.4, 4.9.6 and 15.6 (the owner's 「(B)」): each destruction site of the design's §5.2 composes the
    // drop of every destroyed part as a call over a `uniq/Self` receiver. A Writer from `Writer.init` reaches storage the body does not
    // own through every raw Field, so its drop stores q (which addresses w, which carries y's Loan) into slot and there, where it may be
    // used after every point: a later write of y errs at the write, and with no write at y's storage end (its declaration, as A5 locates
    // a storage end). One row per rule and site, each Bad with a valid twin where SPEC gives one:
    // - Drop at `=`: localBad (also localDrop2Bad, reset and k2's resetSpecBad, the same source); localReachBad uses slot first and errs
    //   all the same by the whole-holder text (as Raw's viaOk); quietOk has no drop, nullQOk's drop stores null; statedBad and
    //   statedSlotBad: WriterS's stated raw layers reach only the slot its Loan names, so the drop's store into that holder decides `y = 5`
    //   (their difference); the store retains as the borrow `slot@uniq` would (5.7 Calls), with its own Loan by the per-argument rule, so
    //   `usep(slot)` errs in both.
    // - A temporary's Drop: tempOnlyBad (no slot: only that storage).
    // - Scope end: scopeBad; endReachBad, y's storage end after x's drop; escapeBad adds a raw layer that comes from a parameter (SPEC
    //   15.6; escapeOk stores null); endParamOk is SPEC 15.6.4's valid shape (a parameter referent's Loan there, no later access).
    // - `_ = x@move`: discardBad.
    // - A drop writing through a borrow Field (Setter, no raw layer): retention is per argument (SPEC 15.6.4), so the receiver's Content,
    //   its own `t@uniq` Loan included, reaches t, and `t@ref` errs after it (setterScopeBad, setterScopeContentBad).
    // The stated and Setter rows read one rule, a destruction giving what its drop's body called as a `uniq/Self` method gives: PLAN
    // G338's open reading of 5.7 Calls ("that storage retains as the borrow would", asked for `put`'s argument) is BU's; if it extends
    // to a borrow held inside the receiver and excludes that borrow's own Loan, statedSlotBad and setterScopeContentBad become valid and
    // their Bad twins keep only `y = 5`, with the call forms (one rule).
    // - Parts: a Field (fieldBad); an outer drop and its Field's drop both composed (orderBad: the inner Writer reaches that storage
    //   whether or not OuterD's drop rewrites its slot first, so no verdict observes SPEC 16.3.2's order); a base (baseBad respelled
    //   `: base(q: q)`); Tuple and Option payloads; a generic Type's drop (holderBad); a Closure capture (closureBad respelled: the
    //   Closure ends with its `do` block); a Sealed handle payload walked on and an erased destruction of a non-Sealed handle's payload
    //   (sealedLateBad, erasedLateBad: q is set after makeObj, whose by-value call would decide the verdict by itself); an `rc` payload
    //   at a possibly final release (SPEC 16.3.3; rcCellBad: q addresses a local cell written only after `@rc`, whose conversion would
    //   otherwise decide the verdict by itself; rcCellQuietOk has no drop, rcCellNullOk never writes the cell); Array elements
    //   (arrReassignBad, also scopeDrop2Bad; litScopeBad adds the Array literal's own construction, R5-M129); Dictionary values and keys
    //   (dictReassignBad, dictKeyReassignBad); fixed-array elements (fixedReassignBad).
    [Theory]
    [InlineData("localBad", "localBad: y = 5")] // Drop at `=`
    [InlineData("localReachBad", "localReachBad: y = 5")] // Drop at `=`
    [InlineData("quietOk", "")] // Drop at `=`
    [InlineData("nullQOk", "")] // Drop at `=`
    [InlineData("statedBad", "statedBad: y = 5; statedBad: usep(slot)")] // Drop at `=`, stated raw layers
    [InlineData("statedSlotBad", "statedSlotBad: usep(slot)")] // Drop at `=`, stated raw layers
    [InlineData("tempOnlyBad", "tempOnlyBad: y = 5")] // a temporary's Drop
    [InlineData("tempOnlyNullOk", "")] // a temporary's Drop
    [InlineData("scopeBad", "scopeBad: y = 5")] // scope end
    [InlineData("endReachBad", "endReachBad: var y: i32 = 1")] // scope end
    [InlineData("endParamOk", "")] // scope end
    [InlineData("escapeBad", "escapeBad: var y: i32 = 1")] // scope end, a parameter's raw layer
    [InlineData("escapeOk", "")] // scope end, a parameter's raw layer
    [InlineData("discardBad", "discardBad: y = 5")] // `_ = x@move`
    [InlineData("setterScopeBad", "setterScopeBad: y = 5; setterScopeBad: t@ref")] // scope end, a borrow Field's drop
    [InlineData("setterScopeContentBad", "setterScopeContentBad: t@ref")] // scope end, a borrow Field's drop
    [InlineData("fieldBad", "fieldBad: y = 5")] // Field part
    [InlineData("orderBad", "orderBad: y = 5")] // an outer drop and its Field's drop
    [InlineData("baseBad", "baseBad: y = 5")] // base part
    [InlineData("baseOk", "")] // base part
    [InlineData("tupleBad", "tupleBad: y = 5")] // Tuple element
    [InlineData("optionBad", "optionBad: y = 5")] // Case payload
    [InlineData("holderBad", "holderBad: y = 5")] // generic Type's drop
    [InlineData("closureBad", "closureBad: y = 5")] // Closure capture
    [InlineData("sealedLateBad", "sealedLateBad: y = 5")] // Sealed handle payload
    [InlineData("sealedLateNullOk", "")] // Sealed handle payload
    [InlineData("erasedLateBad", "erasedLateBad: y = 5")] // erased destruction (non-Sealed handle payload)
    [InlineData("erasedLateNullOk", "")] // erased destruction (non-Sealed handle payload)
    [InlineData("rcCellBad", "rcCellBad: y = 5")] // rc payload at a possibly final release
    [InlineData("rcCellQuietOk", "")] // rc payload at a possibly final release
    [InlineData("rcCellNullOk", "")] // rc payload at a possibly final release
    [InlineData("arrReassignBad", "arrReassignBad: y = 5")] // Array elements at `=`
    [InlineData("litScopeBad", "litScopeBad: y = 5")] // Array literal's elements at a scope end
    [InlineData("dictReassignBad", "dictReassignBad: y = 5")] // Dictionary values at `=`
    [InlineData("dictReassignNullOk", "")] // Dictionary values at `=`
    [InlineData("dictKeyReassignBad", "dictKeyReassignBad: y = 5")] // Dictionary keys at `=`
    [InlineData("dictKeyReassignNullOk", "")] // Dictionary keys at `=`
    [InlineData("fixedReassignBad", "fixedReassignBad: y = 5")] // fixed-array elements at `=`
    [InlineData("fixedReassignNullOk", "")] // fixed-array elements at `=`
    public void ADestructionRetainsAsItsDropsCall(string form, string expected)
    {
        var c = MirBuildTest.Compile(DropShapes + Form(DropForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]))}]");
    }

    // SPEC 4.9.6, 15.6.4, 16.3.3: a published destroying call composes the destruction its row lists at the call, after its own rows,
    // over the pre-call state: `clear` of an Array, `replace`'s old target, `insertOrReplace`'s unused key (keyParamBad: a parameter's
    // Dictionary, so 4.9.6's key dependencies also reach storage the body does not own); `truncate` and `removeAll` have their Bad rows in
    // ATruncateOrRemoveAllWhoseReferentMayObserveKeepsW. Each Bad errs at `y = 5` today through the referent guard (DECISIONS R4-M143) or
    // the key's dependencies, and must after through the composition; each null twin (the destroyed element's q is null, so its drop
    // stores nothing that carries y) is valid, so no guard or composition may add a Loan the drop does not store.
    [Theory]
    [InlineData("arrClearBad", "arrClearBad: y = 5")]
    [InlineData("clearNullOk", "")]
    [InlineData("truncateNullOk", "")]
    [InlineData("removeAllNullOk", "")]
    [InlineData("replaceBad", "replaceBad: y = 5")]
    [InlineData("replaceNullOk", "")]
    [InlineData("keyParamBad", "keyParamBad: y = 5")]
    [InlineData("keyParamNullOk", "")]
    public void APublishedDestroyingCallComposesItsRowsDestruction(string form, string expected)
    {
        var c = MirBuildTest.Compile(DropShapes + Form(DropForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]))}]");
    }

    // V16 (the design's §5.7): N locals whose drops store a parameter referent's Loan through their raw slot, each destroyed by a
    // reassignment (N = 8, 16, 32; SPEC 15.6.4's valid shape). Each destruction composes its drop's rows once, so the work grows at most
    // 3 times as much from 16 to 32 as from 8 to 16 (dev, which composes none: 102,043, 106,611, 116,179, a factor 2.09; quadratic is 4).
    [Fact]
    public void DestructionsWorkStaysLinearInTheirNumber()
    {
        const string Shape = "func shape(y: uniq/i32)\n    var slot: raw/u8 = null\n" +
            "«N:    let w{0} = y@follow@ref\n    var x{0} = Writer.init(q: w{0}@raw@raw/u8)\n    x{0}.p = slot@raw\n    x{0} = Writer.init(q: null)\n»" +
            "    usep(slot)\npublic func main() => ()\n";
        long[] work = [.. new[] { 8, 16, 32 }.Select(n => this.Measured(DropShapes + Expand(Shape, n, 0)).Work)];
        output.WriteLine($"N 8, 16, 32: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 3), $"work {string.Join(", ", work)}");
    }

    // V16: A5 on a program version it checked before allocates nothing at destructions: every site's composition, the published
    // destroying calls, a drop writing through a borrow Field and one through stated raw layers.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmDestructionAllocatesNothing()
    {
        string[] forms = ["localBad", "tempOnlyBad", "scopeBad", "discardBad", "escapeBad", "setterScopeBad", "statedBad", "fieldBad", "orderBad",
            "baseBad", "tupleBad", "optionBad", "holderBad", "closureBad", "sealedLateBad", "erasedLateBad", "rcCellBad", "arrReassignBad",
            "litScopeBad", "dictReassignBad", "dictKeyReassignBad", "fixedReassignBad", "arrClearBad", "replaceBad", "keyParamBad"];
        var c = MirBuildTest.Compile(DropShapes + string.Concat(forms.Select(static x => Form(DropForms, x))) + "public func main() => ()\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.NotEmpty(Problems(c, program, LoanCodes));
    }
}
