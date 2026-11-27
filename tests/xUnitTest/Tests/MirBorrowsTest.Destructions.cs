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
    // without a drop, and QuietU8 holds them with p a raw/u8; Outer holds a Writer by value and OuterD's own drop rewrites its inner
    // Writer's slot (two drops, both composed; no row observes their order); BaseW's drop runs for a Derived's base part; Holder<T> is a
    // generic Type with the drop; Setter's drop writes its
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
        struct QuietU8
            public var p: raw/u8
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
        struct RWD {a}
            public var p: raw/(ref/i32 during a)
            public var r: ref/i32 during a
            public init(r: ref/i32 during a)
                self.p = null
                self.r = r
            drop
                unsafe => *self.p = self.r
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
        func quietCtorBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Quiet.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = Quiet.init(q: null)
            y = 5
            usep(slot)
        func quietCtorU8Bad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: u8 = 0
            var x = QuietU8.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = QuietU8.init(q: null)
            y = 5
            usep(slot@raw)
        func quietFieldOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Quiet.init(q: null)
            x.q = w@raw@raw/u8
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
        func derefBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            let r = x@uniq
            r@follow = Writer.init(q: null)
            y = 5
            usep(slot)
        func derefOk()
            var x = Writer.init(q: null)
            let r = x@uniq
            r@follow = Writer.init(q: null)
            inspect(x@ref)
        func derefFieldOk()
            var x = Writer.init(q: null)
            var h = (x@uniq, 0)
            h.0@follow = Writer.init(q: null)
            inspect(x@ref)
        func rr2v()
            var x = Writer.init(q: null)
            var r = x@uniq
            let rr = r@uniq
            rr@follow@follow = Writer.init(q: null)
            inspect(x@ref)
        func rr3v()
            var x = Writer.init(q: null)
            var r = x@uniq
            var rr = r@uniq
            let r3 = rr@uniq
            r3@follow@follow@follow = Writer.init(q: null)
            inspect(x@ref)
        func rr2fv()
            var x = Writer.init(q: null)
            var h = (x@uniq, 0)
            let rr = h@uniq
            rr@follow.0@follow = Writer.init(q: null)
            inspect(x@ref)
        func rr2i(y: uniq/i32)
            let w = y@follow@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var r = x@uniq
            let rr = r@uniq
            rr@follow@follow = Writer.init(q: null)
            y@follow = 5
            usep(slot)
            inspect(x@ref)
        func rr2td(y: uniq/i32)
            let w = y@follow@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var h = (x@uniq, w@raw@raw/u8)
            let rr = h@uniq
            rr@follow.0@follow = Writer.init(q: null)
            y@follow = 5
            usep(slot)
            inspect(x@ref)
        func adX2v()
            var z: i32 = 0
            var slot: ref/i32 = z@ref
            var y: i32 = 1
            let v = y@ref
            var k: i32 = 3
            var x = RWD.init(r: v)
            x.p = slot@raw
            var h = (x@uniq, v)
            let rr = h@uniq
            rr@follow.0@follow = RWD.init(r: k@ref)
            y = 5
            inspect(slot@ref)
        func dictIndexBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var d: Dictionary<i32, Writer> = [:]
            _ = d.insertOrReplace(1, x@move)
            d[1] = Writer.init(q: null)
            y = 5
            usep(slot)
        func dictIndexOk()
            var d: Dictionary<i32, Writer> = [:]
            _ = d.insertOrReplace(1, Writer.init(q: null))
            d[1] = Writer.init(q: null)
        func uniqElemOk()
            var a: Array<Writer> = [Writer.init(q: null)]
            match a.tryGetUniq(0)
                .Some(let r) => r@follow = Writer.init(q: null)
                .None => ()
        func derefInnerOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o = Outer.init(q: null)
            o.inner.p = slot@raw
            let r = o@uniq
            r.inner = Writer.init(q: null)
            y = 5
            usep(slot)
            inspect(o@ref)
        func setDerefBad(b: bool)
            var z: i32 = 0
            var t: ref/i32 = z@ref
            var t2: ref/i32 = z@ref
            var y: i32 = 1
            if b
                var s = Setter.init(target: t@uniq, value: y@ref)
                let r = s@uniq
                r@follow = Setter.init(target: t2@uniq, value: z@ref)
            y = 5
            inspect(t@ref)
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
        func replaceDerefOk()
            var x = Writer.init(q: null)
            let r = x@uniq
            Kimi.Intrinsics.replace(r@follow@uniq, with: Writer.init(q: null))
            inspect(x@ref)
        func replaceSlotOk()
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            let r = x@uniq
            var n = Writer.init(q: null)
            n.p = slot@raw
            Kimi.Intrinsics.replace(r@follow@uniq, with: n@move)
            inspect(x@ref)
            usep(slot)
        func replaceFieldOk()
            var o = Outer.init(q: null)
            let r = o@uniq
            Kimi.Intrinsics.replace(r.inner@uniq, with: Writer.init(q: null))
            inspect(o@ref)
        func replaceElementOk()
            var a: Array<Writer> = [Writer.init(q: null)]
            Kimi.Intrinsics.replace(a[0]@uniq, with: Writer.init(q: null))
            inspect(a@ref)
        func replaceValueBad(y: uniq/i32)
            let w = y@follow@ref
            var x = Writer.init(q: null)
            let r = x@uniq
            Kimi.Intrinsics.replace(r@follow@uniq, with: Writer.init(q: w@raw@raw/u8))
            y@follow = 5
            inspect(x@ref)
        func replaceDerefBad(y: uniq/i32)
            let w = y@follow@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            let r = x@uniq
            Kimi.Intrinsics.replace(r@follow@uniq, with: Writer.init(q: null))
            y@follow = 5
            usep(slot)
            inspect(x@ref)
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
    //   all the same by the whole-holder text (as Raw's viaOk); quietCtorBad errs at its construction, not at a drop (Quiet has none):
    //   Quiet.init may put its own pointer in p and store q through it in storage the body does not own (15.6.4 "a nominal Type's Fields
    //   included", the owner's O-365 (a); PLAN G841 (3)); so does quietCtorU8Bad, whose p is a raw/u8: p is a layer QuietU8 states, and
    //   through such a layer the call may store what a raw layer it writes retains, whatever its pointee (SPEC 5.7 Calls and its example
    //   throughField; R4-M241); quietFieldOk is the valid twin for the destruction rule: the constructors receive only null, w's address
    //   enters x by a body write and the old x is replaced without a drop; nullQOk's drop stores null; statedBad and
    //   statedSlotBad: WriterS's stated raw layers reach only the slot its Loan names, so the drop's store into that holder decides `y = 5`
    //   (their difference); the store retains as the borrow `slot@uniq` would (5.7 Calls), with its own Loan by the per-argument rule, so
    //   `usep(slot)` errs in both: the argument is the `uniq/Self` receiver, whose referent holds the keeper, so a read through it carries
    //   the keeper's Loan (a keeper an argument holds by value is that argument's own value: AByValueKeeperIsItsArgumentsOwnValue).
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
    // - `=` through an exclusive reference (D-918): the destroyed part's receiver is the reference's Reborrow, the input the drop writes
    //   through, so the drop's store keeps what reads through it carry (derefBad's q, which addresses w: `y = 5` errs) and never the
    //   reference's own Loan (rule A, SPEC 15.6.4): derefOk, derefFieldOk (a reference a Tuple holds), uniqElemOk (tryGetUniq's) and a
    //   Dictionary's indexed replacement through indexUniq's reference (dictIndexOk; dictIndexBad keeps its `y = 5`) are valid, as their
    //   method twin `r.flush()` is; on dev each kept r's Loan, an error at its referent's storage end and later uses. A Field past the
    //   reference takes the same receiver (derefInnerOk: `r.inner = ...`), and so does a drop row of the Content kind (setDerefBad:
    //   Setter's, whose records are setterScopeBad's).
    // - `=` through two or more exclusive Derefs (D-935; their call forms are AChainRowIsAReadThroughTheArgument's): the receiver also
    //   carries the parent Loans, which a read through the intermediate reference's holder gives back, the own value the drop's store
    //   never keeps (rule A): rr2v, rr3v (three) and rr2fv (a Field between) are valid; rr2i and rr2td keep what the read at the last
    //   Deref carries (q's, and h.1's address of w: `y@follow = 5`), and adX2v keeps v's Loan, which the old x holds though h.1 holds it
    //   too (`y = 5`; `var z` and the new x's `var k` are its own records on dev too).
    [Theory]
    [InlineData("localBad", "localBad: y = 5")] // Drop at `=`
    [InlineData("localReachBad", "localReachBad: y = 5")] // Drop at `=`
    [InlineData("quietCtorBad", "quietCtorBad: y = 5")] // Drop at `=`, no drop: the construction's own pointer at p (O-365 (a))
    [InlineData("quietCtorU8Bad", "quietCtorU8Bad: y = 5")] // Drop at `=`, no drop: the construction's own pointer at p, a layer QuietU8 states (SPEC 5.7 Calls)
    [InlineData("quietFieldOk", "")] // Drop at `=`, no drop: w's address enters x by a body write
    [InlineData("nullQOk", "")] // Drop at `=`
    [InlineData("statedBad", "statedBad: y = 5; statedBad: usep(slot); statedBad: var spare: raw/u8 = null; statedBad: var slot: raw/u8 = null")] // Drop at `=`, stated raw layers; the storage ends are true records (5.7 Calls: a stated layer keeps what it admits; D-757)
    [InlineData("statedSlotBad", "statedSlotBad: usep(slot); statedSlotBad: y = 5; statedSlotBad: var spare: raw/u8 = null; statedSlotBad: var slot: raw/u8 = null")] // Drop at `=`, stated raw layers; true records as statedBad's (D-757)
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
    [InlineData("derefBad", "derefBad: y = 5")] // `=` through an exclusive reference: the drop's store keeps q, not r's Loan (D-918)
    [InlineData("derefOk", "")] // `=` through an exclusive reference (D-918)
    [InlineData("derefFieldOk", "")] // `=` through a reference a Tuple holds (D-918)
    [InlineData("dictIndexBad", "dictIndexBad: y = 5")] // a Dictionary's indexed replacement, through indexUniq's reference (D-918)
    [InlineData("dictIndexOk", "")] // a Dictionary's indexed replacement (D-918)
    [InlineData("uniqElemOk", "")] // `=` through tryGetUniq's reference (D-918)
    [InlineData("derefInnerOk", "")] // `=` of a Field past an exclusive reference (D-918)
    [InlineData("setDerefBad", "setDerefBad: y = 5; setDerefBad: t@ref")] // a Content-kind drop row through a reference: setterScopeBad's records (D-918)
    [InlineData("rr2v", "")] // `=` through two exclusive Derefs (D-935)
    [InlineData("rr3v", "")] // `=` through three exclusive Derefs (D-935)
    [InlineData("rr2fv", "")] // `=` through two Derefs with a Field between (D-935)
    [InlineData("rr2i", "rr2i: y@follow = 5")] // through two Derefs: q's Loan read at the last Deref stays (D-935)
    [InlineData("rr2td", "rr2td: y@follow = 5")] // through two Derefs: w's Loan read through h.1's address stays (D-935)
    [InlineData("adX2v", "adX2v: y = 5; adX2v: var k: i32 = 3; adX2v: var z: i32 = 0")] // through two Derefs: v's Loan, held by x and h.1, stays (D-935)
    public void ADestructionRetainsAsItsDropsCall(string form, string expected)
    {
        var c = MirBuildTest.Compile(DropShapes + Form(DropForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]))}]");
    }

    // SPEC 4.9.6, 15.6.4, 16.3.3: a published destroying call composes the destruction its row lists at the call, after its own rows,
    // over the pre-call state: `clear` of an Array, `replace`'s old target, `insertOrReplace`'s unused key (keyParamBad: a parameter's
    // Dictionary, so 4.9.6's key dependencies also reach storage the body does not own); `truncate` and `removeAll` have their Bad rows in
    // ATruncateOrRemoveAllComposesItsElementsDestruction. Each Bad errs at `y = 5` through the composition (DECISIONS R4-M150; before it,
    // R4-M143's referent guard) or the key's dependencies; each null twin (the destroyed element's q is null, so its drop stores nothing
    // that carries y) is valid, so the composition adds no Loan the drop does not store.
    // D-933 (SPEC 15.7, 15.6.4): `replace` writes only its target and places `with`'s value there, never writing through it, so the
    // target's own Loans (`x@uniq` past `r`, `o@uniq` past `r` to a Field, the element borrow) reach no storage through `with`'s raw Field:
    // replaceDerefOk, replaceSlotOk (`with` addresses a local slot), replaceFieldOk and replaceElementOk are valid (fail-before: records at
    // the root's storage end and the later `@ref`); replaceValueBad's placed value carries w's Loan into x, and replaceDerefBad's old value's
    // drop stores q, so each errs at `y@follow = 5` alone (fail-before: also at `var x` and `x@ref`).
    [Theory]
    [InlineData("arrClearBad", "arrClearBad: y = 5")]
    [InlineData("clearNullOk", "")]
    [InlineData("truncateNullOk", "")]
    [InlineData("removeAllNullOk", "")]
    [InlineData("replaceBad", "replaceBad: y = 5")]
    [InlineData("replaceNullOk", "")]
    [InlineData("replaceDerefOk", "")]
    [InlineData("replaceSlotOk", "")]
    [InlineData("replaceFieldOk", "")]
    [InlineData("replaceElementOk", "")]
    [InlineData("replaceValueBad", "replaceValueBad: y@follow = 5")]
    [InlineData("replaceDerefBad", "replaceDerefBad: y@follow = 5")]
    [InlineData("keyParamBad", "keyParamBad: y = 5")]
    [InlineData("keyParamNullOk", "")]
    public void APublishedDestroyingCallComposesItsRowsDestruction(string form, string expected)
    {
        var c = MirBuildTest.Compile(DropShapes + Form(DropForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]))}]");
    }

    // SPEC 4.1, 15.6.6 (R8-X1 U3): a destruction that observes no Origin adds no requirement. Pair has no drop, and a `[0 of Guard]` it
    // holds destroys nothing, as its own Field (a5obs), in a Tuple (atup), an Option (aopt) or a Field's struct (awrap): no Guard's drop
    // observes a at pr's end, so x's Loan ends with pr's last use and `x = 2` is valid. A Guard's drop observes a there (one element:
    // a5obs1, atup1; a Guard Field beside the empty array: aguard), Pair's own drop observes all it reaches (apdrop), and a later use
    // keeps the Loan without any destruction (ause): each errs at `x = 2`. A non-regular Type behind an empty array (agrow, agrowq) is
    // never destroyed: no record at `x = 2`. Their call's OwnershipDepthLimit_Kd, on dev too, rests on the holding guard's walk of the
    // array's element Type (MirTypes.HoldsLoans, a holding fact, not destruction): a known limit on these valid programs, an open issue.
    [Theory]
    [InlineData("a5obs", "    public let gs: [0 of Guard during a]\n", "        self.gs = []\n", "    x = 2\n", "")]
    [InlineData("atup", "    public let t: (i32, [0 of Guard during a])\n", "        self.t = (1, [])\n", "    x = 2\n", "")]
    [InlineData("aopt", "    public let o: Option<[0 of Guard during a]>\n", "        self.o = .Some([])\n", "    x = 2\n", "")]
    [InlineData("awrap", "    public let w: Wrap during a\n", "        self.w = Wrap.init()\n", "    x = 2\n", "")]
    [InlineData("a5obs1", "    public let gs: [1 of Guard during a]\n", "        self.gs = [Guard.init(p: p)]\n", "    x = 2\n", "a5obs1: x = 2")]
    [InlineData("atup1", "    public let t: (i32, [1 of Guard during a])\n", "        self.t = (1, [Guard.init(p: p)])\n", "    x = 2\n", "atup1: x = 2")]
    [InlineData("aguard", "    public let gs: [0 of Guard during a]\n    public let g: Guard during a\n", "        self.gs = []\n        self.g = Guard.init(p: p)\n", "    x = 2\n", "aguard: x = 2")]
    [InlineData("apdrop", "    public let gs: [0 of Guard during a]\n", "        self.gs = []\n    drop => ()\n", "    x = 2\n", "apdrop: x = 2")]
    [InlineData("ause", "    public let gs: [0 of Guard during a]\n", "        self.gs = []\n", "    x = 2\n    inspect(pr@ref)\n", "ause: x = 2")]
    [InlineData("agrow", "    public let kids: [0 of rc/G<(i32, i32)>]\n", "        self.kids = []\n", "    x = 2\n", "agrow: Pair.init(p: x@ref) OwnershipDepthLimit_Kd")]
    [InlineData("agrowq", "    public let kids: [0 of rc/G<(i32, i32)>]\n", "        self.kids = []\n", "", "agrowq: Pair.init(p: x@ref) OwnershipDepthLimit_Kd")]
    public void AFixedArrayWithoutAnElementObservesNothing(string form, string fields, string init, string rest, string expected)
    {
        var c = MirBuildTest.Compile(
            "func inspect<T>(value: ref/T) => ()\nstruct G<T>\n    public var kids: [0 of rc/G<(T, T)>]\n    public init() => self.kids = []\n" +
            "struct Guard {a}\n    public let p: ref/i32 during a\n    public init(p: ref/i32 during a) => self.p = p\n    drop => Console.writeLine(\"g\")\n" +
            "struct Wrap {a}\n    public let gs: [0 of Guard during a]\n    public init() => self.gs = []\n" +
            $"struct Pair {{a}}\n    public let p: ref/i32 during a\n{fields}    public init(p: ref/i32 during a)\n        self.p = p\n{init}" +
            $"func {form}()\n    var x: i32 = 1\n    let pr = Pair.init(p: x@ref)\n{rest}");
        Assert.Equal(
            $"{form}: [{expected}]",
            $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.OwnershipDepthLimit_Kd]))}]");
    }

    // SPEC 15.6.4, 23.1.1, IMPL A.4 (DECISIONS R4-M153, R4-M150; G264 U1's review B1, the planning session's record decision): a declared
    // drop whose Binding retention passes the walk's depth bound (DQ40's Field, a Writer 40 levels down a Tuple, so the retention is
    // Limit) is the located resource limit where the destruction is composed, as its method twin `x.flush()` is on dev: at x's
    // reassignment and at its storage end, in the valid dq40Ok and beside dq40Bad's own `y@follow = 5` (w's Loan, which wr's slot keeps).
    // Each DQ40.init call's own contract is Deep, recorded at the call (G408B-17; the Middle's G408-B A5 ruling C4): its input d stores a
    // Writer past the bound, whose raw p can carry an address, so the finding left there may retain (a Q13 class 4 move from dev's two
    // records).
    // The initializer moves its non-Copy `d` with `d@move` (SPEC 3.5 row 3; R6-check H1-tests (t), da41db914's form).
    [Theory]
    [InlineData("dq40Ok")]
    [InlineData("dq40Bad")]
    public void ADeclaredDropPastTheDepthBoundIsTheDepthLimit(string form)
    {
        var type = new string('(', 40) + "Writer" + string.Concat(Enumerable.Repeat(", i32)", 40));
        var (made, remade) = ($"DQ40.init(d: {Nested("wr@move", 40)})", $"DQ40.init(d: {Nested("Writer.init(q: null)", 40)})");
        var (first, second) = ($"var x = {made}", $"x = {remade}");
        var q = form == "dq40Bad" ? "w@raw@raw/u8" : "null";
        var c = MirBuildTest.Compile(DropShapes + $"struct DQ40\n    public var d: {type}\n    public init(d: {type})\n        self.d = d@move\n" +
            $"    drop\n        return\nfunc {form}(y: uniq/i32)\n    let w = y@follow@ref\n    var slot: raw/u8 = null\n" +
            $"    var wr = Writer.init(q: {q})\n    wr.p = slot@raw\n    {first}\n    {second}\n    y@follow = 5\n    usep(slot)\n");
        var expected = $"{first} {DepthLimit}|{second} {DepthLimit}|{made} {DepthLimit}|{remade} {DepthLimit}" +
            (form == "dq40Bad" ? "|y@follow = 5 ComparisonLoanConflict_Kd" : string.Empty);
        Assert.Equal(Rows(expected), DepthRecords(c, Analyze(c), form).Select(static x => $"{x.Line} {x.Code}"));
    }

    // V16 (the design's §5.7; the owner's 「A) 原因を先に直す」 of 2026-10-31, with G342's rows): N locals whose drops store a parameter
    // referent's Loan through their raw slot, each destroyed by a reassignment (N = 8, 16, 32; SPEC 15.6.4's valid shape). Each Loan stays
    // live until `usep(slot)` (15.6.4's example), so the output is quadratic: the work grows at most 5 times as much from 16 to 32 as from 8
    // to 16, the class's precedent for values whose Loans are live together (MirBorrowsTest.Unowned's G342 rows; DECISIONS R4-M160).
    [Fact]
    public void DestructionsWorkStaysWithinTheirLiveLoans()
    {
        const string Shape = "func shape(y: uniq/i32)\n    var slot: raw/u8 = null\n" +
            "«N:    let w{0} = y@follow@ref\n    var x{0} = Writer.init(q: w{0}@raw@raw/u8)\n    x{0}.p = slot@raw\n    x{0} = Writer.init(q: null)\n»" +
            "    usep(slot)\npublic func main() => ()\n";
        long[] work = [.. new[] { 8, 16, 32 }.Select(n => Measured(DropShapes + Expand(Shape, n, 0)).Work)];
        output.WriteLine($"N 8, 16, 32: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
    }

    // V16: A5 on a program version it checked before allocates nothing at destructions: every site's composition, the published
    // destroying calls, a drop writing through a borrow Field and one through stated raw layers, and a Drop through an exclusive
    // reference, whose receiver is its Reborrow (D-918), also through two (D-935: the referent and the Remove nodes of rr2v, rr2td and
    // adX2v interned once).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmDestructionAllocatesNothing()
    {
        string[] forms = ["localBad", "tempOnlyBad", "scopeBad", "discardBad", "escapeBad", "setterScopeBad", "statedBad", "fieldBad", "orderBad",
            "baseBad", "tupleBad", "optionBad", "holderBad", "closureBad", "sealedLateBad", "erasedLateBad", "rcCellBad", "arrReassignBad",
            "litScopeBad", "dictReassignBad", "dictKeyReassignBad", "fixedReassignBad", "arrClearBad", "replaceBad", "keyParamBad", "derefOk",
            "derefBad", "rr2v", "rr2td", "adX2v"];
        var c = MirBuildTest.Compile(DropShapes + string.Concat(forms.Select(static x => Form(DropForms, x))) + "public func main() => ()\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.NotEmpty(Problems(c, program, LoanCodes));
    }
}
