// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's W reads its first step at the reading path's Type, the input's pointee or referent (SPEC 15.6 Holders, 5.7 Calls,
/// 15.6.4; the A5 value redesign's unit U3b, the owner's O-209): a Place that Type does not designate (SPEC 5.2, 4.1) is read punned
/// through the Type's raw pointer raw Places, each a layer of its own (the owner's O-223 (B'): no other part of it is read), the
/// holder's state at those layers' guard beside its own, and storage the body does not own, with those layers' record, where that
/// guard steps over an `owner` Scalar's byte or a byte a punned write may not change (PLAN G785). The rows are the unit's coverage table, each labelled from
/// SPEC (I invalid, V valid; P3: RESULT.md batch 1's settled label), the record each invalid one gives (SPEC 23.1.2).</summary>
public sealed partial class MirBorrowsTest
{
    private const string FirstStepShapes = "func usep<T>(p: raw/T) => ()\n";

    private static readonly Dictionary<string, string> FirstStepForms = new()
    {
        ["fMixedPunChainBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fMixedPunChainBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var z: u8 = 5
                var w = (z@ref, 3@u64)
                let q = w.1@raw@raw/(raw/(raw/u8))
                unsafe => *q = other@raw
                keep2(q, spare)
                kk = 2
                usep(other)
            """,
        ["qIntChainMixedBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func qIntChainMixedBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: (raw/u8, u64) = (null, 3@u64)
                let q = w.1@raw@raw/(raw/(raw/u8))
                keep2(q, spare)
                kk = 2
            """,
        ["qPartPunBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func qPartPunBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var w: (raw/(raw/u8), u64) = (other@raw, 3@u64)
                let q = w.1@raw@raw/(raw/(raw/u8))
                keep2(q, spare)
                kk = 2
            """,
        ["sSpanBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func sSpanBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var w: (u64, raw/(raw/u8)) = (0@u64, other@raw)
                let b = w@raw@raw/u8
                var q: raw/(raw/(raw/u8)) = null
                unsafe => q = (b + 4)@raw/(raw/(raw/u8))
                keep2(q, spare)
                kk = 2
            """,
        ["sHBufBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func sHBufBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ([8 of u8], raw/u8) = ([8 of 0@u8], other@raw)
                keep2(w.0@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["sHPairBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func sHPairBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ((u32, u32), raw/u8) = ((1@u32, 2@u32), other@raw)
                keep2(w.0@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["sHTupElemBad"] = """
            func keepT(slot: raw/(raw/(raw/u8), u64), value: raw/u8) => ()
            func sHTupElemBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                keepT(w.0@raw@raw/(raw/(raw/u8), u64), spare)
                kk = 2
            """,
        ["sHArrElemBad"] = """
            func keepA(slot: raw/[2 of raw/(raw/u8)], value: raw/u8) => ()
            func sHArrElemBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                keepA(w.0@raw@raw/[2 of raw/(raw/u8)], spare)
                kk = 2
            """,
        ["lChainH2Bad"] = """
            func stash4(slot: raw/(raw/(raw/(raw/u8))), value: raw/u8) => ()
            func lChainH2Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h4: u8 = 0
                var h3: raw/u8 = h4@raw
                var h2: raw/u16 = h3@raw@raw/u16
                var h1: raw/u32 = h2@raw@raw/u32
                stash4(h1@raw@raw/(raw/(raw/(raw/u8))), spare)
                kk = 2
                usep(h2)
            """,
        ["lTupLeafBad"] = """
            func keepTL(slot: raw/(raw/(raw/u8, u16)), value: raw/u8) => ()
            func lTupLeafBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h3: u8 = 0
                var h2: (raw/u8, u16) = (h3@raw, 0@u16)
                var h1: raw/u32 = h2@raw@raw/u32
                keepTL(h1@raw@raw/(raw/(raw/u8, u16)), spare)
                kk = 2
                let z = h2
            """,
        ["lArrLeafBad"] = """
            func keepAL(slot: raw/(raw/[2 of raw/u8]), value: raw/u8) => ()
            func lArrLeafBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h3: u8 = 0
                var h2: [2 of raw/u8] = [h3@raw, null]
                var h1: raw/u32 = h2@raw@raw/u32
                keepAL(h1@raw@raw/(raw/[2 of raw/u8]), spare)
                kk = 2
                let z = h2
            """,
        ["xParamBad"] = """
            func stash3(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func xParamBad(p: raw/u8)
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: (raw/u8, raw/u8) = (p, null)
                stash3(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["pPlainViewBad"] = """
            struct Plain
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            struct Wrap<T>
                public var value: T
            func viaRawPlain(slot: raw/Plain, value: raw/u8) => ()
            func pPlainViewBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var h: raw/u64 = v@raw@raw/u64
                viaRawPlain(h@raw@raw/Plain, spare)
                kk = 2
                usep(v)
            """,
        ["pPlainFirstBad"] = """
            #Layout("C")
            struct PlainC
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            func viaFirst(slot: raw/PlainC, value: raw/u8) => ()
            func pPlainFirstBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var h: raw/u64 = v@raw@raw/u64
                viaFirst(h@raw@raw/PlainC, spare)
                kk = 2
                usep(v)
            """,
        ["fPastLoanFreeBad"] = """
            func stash3(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fPastLoanFreeBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var hop: raw/u8 = v@raw@raw/u8
                stash3(hop@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
                let z = v
            """,
        ["qPastHopRawBad"] = """
            func stash3(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func qPastHopRawBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var hop: raw/u8 = null
                let hp = hop@raw@raw/(raw/u8)
                unsafe => *hp = v@raw@raw/u8
                stash3(hop@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
                let z = v
            """,
        ["kLoanHoldConvBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func kLoanHoldConvBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: u8 = 0
                var b: u8 = 0
                var s: (raw/u8, raw/u8) = (a@raw, b@raw)
                let q = s@raw@raw/(raw/(raw/u8))
                keep2(q, spare)
                kk = 2
                let z = a
            """,
        ["xParamUntrackedBad"] = """
            func stash3(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func xParamUntrackedBad(p: raw/u8)
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u8 = 0
                var h: (raw/u8, raw/u8) = (p, v@raw)
                stash3(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["fPastLoanFreeOk"] = """
            func stash3(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fPastLoanFreeOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var hop: raw/u8 = v@raw@raw/u8
                stash3(hop@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["kLoanHoldConvOk"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func kLoanHoldConvOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: u8 = 0
                var b: u8 = 0
                var s: (raw/u8, raw/u8) = (a@raw, b@raw)
                let q = s@raw@raw/(raw/(raw/u8))
                keep2(q, spare)
                kk = 2
            """,
        ["xParamOk"] = """
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func xParamOk(p: raw/u8)
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: (raw/u8, raw/u8) = (p, null)
                keep4(h@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["sSpanOk"] = """
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func sSpanOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var w: (u64, raw/(raw/u8)) = (0@u64, other@raw)
                let b = w@raw@raw/u8
                var q: raw/(raw/u8) = null
                unsafe => q = (b + 4)@raw/(raw/u8)
                keep4(q, spare)
                kk = 2
            """,
        ["sHBufOk"] = """
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func sHBufOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ([8 of u8], raw/u8) = ([8 of 0@u8], other@raw)
                keep4(w.0@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["sHPairOk"] = """
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func sHPairOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ((u32, u32), raw/u8) = ((1@u32, 2@u32), other@raw)
                keep4(w.0@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["sHTupElemOk"] = """
            func keepT2(slot: raw/(raw/u8, u64), value: raw/u8) => ()
            func sHTupElemOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                keepT2(w.0@raw@raw/(raw/u8, u64), spare)
                kk = 2
            """,
        ["sHArrElemOk"] = """
            func keepA2(slot: raw/[2 of raw/u8], value: raw/u8) => ()
            func sHArrElemOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 0
                var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                keepA2(w.0@raw@raw/[2 of raw/u8], spare)
                kk = 2
            """,
        ["pStaticViewOk"] = """
            struct PlainS
                public var p: raw/u8 during static
                public init() => self.p = null
            func viaRawPlainS(slot: raw/PlainS, value: raw/u8) => ()
            func pStaticViewOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var h: raw/u64 = v@raw@raw/u64
                viaRawPlainS(h@raw@raw/PlainS, spare)
                kk = 2
                usep(v)
            """,
        ["pWrapViewOk"] = """
            struct Plain
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            struct Wrap<T>
                public var value: T
            func viaRawWrap(slot: raw/Wrap<raw/u8>, value: raw/u8) => ()
            func pWrapViewOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var h: raw/u32 = v@raw@raw/u32
                viaRawWrap(h@raw@raw/Wrap<raw/u8>, spare)
                kk = 2
                let z = v
            """,
        ["zEnumViewOk"] = """
            func viaOpt(slot: raw/(Option<raw/(raw/u8)>), value: raw/u8) => ()
            func zEnumViewOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: raw/u64 = x@raw@raw/u64
                viaOpt(h@raw@raw/(Option<raw/(raw/u8)>), spare)
                kk = 2
                usep(x)
            """,
        ["zEnumFieldOk"] = """
            #Layout("C")
            struct OptS
                public var o: Option<raw/(raw/u8)>
                public var n: u64
                public init()
                    self.o = .None
                    self.n = 0
            func viaOptS(slot: raw/OptS, value: raw/u8) => ()
            func zEnumFieldOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: raw/u64 = x@raw@raw/u64
                viaOptS(h@raw@raw/OptS, spare)
                kk = 2
                usep(x)
            """,
        ["zFnViewOk"] = """
            func viaFn(slot: raw/(() -> ()), value: raw/u8) => ()
            func zFnViewOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: raw/u64 = x@raw@raw/u64
                viaFn(h@raw@raw/(() -> ()), spare)
                kk = 2
                usep(x)
            """,
        ["zLeafViewOk"] = """
            func sneak64(slot: raw/u64, value: raw/u8) => ()
            func zLeafViewOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var x: raw/(raw/u8) = v@raw
                sneak64(x@raw@raw/u64, spare)
                kk = 2
                usep(v)
            """,
        ["zBorrowAloneOk"] = """
            func sneak1(slot: uniq/(uniq/u64 during o), value: raw/u8) => ()
            func zBorrowAloneOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var x: raw/(raw/u8) = v@raw
                let p = x@raw@raw/u64
                unsafe
                    var h = (*p)@uniq
                    sneak1(h@uniq, spare)
                    kk = 2
                    usep(v)
            """,
        ["zBorrowMixBad"] = """
            func sneakH(slot: uniq/((uniq/u64 during o, uniq/(raw/(raw/u8)) during o)), value: raw/u8) => ()
            func zBorrowMixBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var x: raw/(raw/u8) = v@raw
                var x3: raw/(raw/u8) = null
                let p = x@raw@raw/u64
                unsafe
                    var h = ((*p)@uniq, x3@uniq)
                    sneakH(h@uniq, spare)
                    kk = 2
                    usep(v)
            """,
        ["zPunReadBad"] = """
            func sneak(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func zPunReadBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var x: raw/(raw/u8) = v@raw
                var s: raw/u64 = x@raw@raw/u64
                let ps = s@raw@raw/(raw/(raw/(raw/u8)))
                unsafe
                    let t = *ps
                    sneak(t, spare)
                    kk = 2
                    usep(v)
            """,
        ["zRawMixBad"] = """
            func sneakR(slot: raw/((raw/u64, raw/(raw/(raw/u8)))), value: raw/u8) => ()
            func zRawMixBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var x: raw/(raw/u8) = v@raw
                var x3: raw/(raw/u8) = null
                var h = (x@raw@raw/u64, x3@raw)
                sneakR(h@raw, spare)
                kk = 2
                usep(v)
            """,
        ["gPunReadPtrBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func gPunReadPtrBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var q: raw/(raw/u8) = other@raw
                var h: raw/usize = q@raw@raw/usize
                var p: raw/u8 = null
                unsafe
                    p = *(h@raw@raw/(raw/u8))
                    *p = 5
                keep(q, spare)
                kk = 2
            """,
        ["gCallStoreConvBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func conv(slot: raw/(raw/u8), v: ref/(raw/usize))
                unsafe => *slot = v@follow@raw/u8
            func gCallStoreConvBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var q: raw/(raw/u8) = other@raw
                var box: raw/usize = q@raw@raw/usize
                var p: raw/u8 = null
                conv(p@raw, box@ref)
                unsafe => *p = 5
                keep(q, spare)
                kk = 2
            """,
        ["sTupleSideOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func sTupleSideOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var q: raw/(raw/u8) = other@raw
                var o2: u8 = 0
                var h: (raw/usize, raw/u8) = (q@raw@raw/usize, null)
                h.1 = o2@raw
                unsafe => *(h.1) = 5
                keep(q, spare)
                kk = 2
            """,
        ["sConvFieldBad"] = """
            func s() -> raw/u8 during static
                unsafe => return Raw.allocate<u8>(8)@(raw/u8 during static)
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            struct PQ
                public var p: raw/(raw/(raw/u8))
                public init() => self.p = null
            func conv2(b: PQ) -> raw/usize
                return b.p@raw/usize
            func sConvFieldBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var q: raw/(raw/u8) = other@raw
                var pq = PQ.init()
                pq.p = q@raw
                let bytes = conv2(pq@move)
                unsafe => *bytes = s()@usize
                keep(q, spare)
                kk = 2
            """,
        ["zEnumTypedBad"] = """
            func viaOpt(slot: raw/(Option<raw/(raw/u8)>), value: raw/u8) => ()
            func zEnumTypedBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: Option<raw/(raw/u8)> = .Some(x@raw)
                viaOpt(h@raw, spare)
                kk = 2
                usep(x)
            """,
        ["pWrapViewBad"] = """
            struct Plain
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            struct Wrap<T>
                public var value: T
            func viaRawWrap(slot: raw/Wrap<raw/u8>, value: raw/u8) => ()
            func pWrapViewBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var h: raw/u32 = v@raw@raw/u32
                viaRawWrap(h@raw@raw/Wrap<raw/u8>, spare)
                kk = 2
                usep(h)
            """,
        ["pPlainViewDeclBad"] = """
            struct Plain
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            struct Wrap<T>
                public var value: T
            func viaRawPlain(slot: raw/Plain, value: raw/u8) => ()
            func pPlainViewDeclBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var h: raw/(raw/u8) = v@raw
                viaRawPlain(h@raw@raw/Plain, spare)
                kk = 2
                usep(v)
            """,
        ["pPlainDeclBad"] = """
            struct Plain
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            struct Wrap<T>
                public var value: T
            func viaPlain(w: uniq/Plain, value: raw/u8) => ()
            func pPlainDeclBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var w = Plain.init(p: v@raw@raw/u8)
                viaPlain(w@uniq, spare)
                kk = 2
                usep(v)
            """,
        ["pPlainDeclOk"] = """
            struct Plain
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            struct Wrap<T>
                public var value: T
            func viaPlain(w: uniq/Plain, value: raw/u8) => ()
            func pPlainDeclOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var w = Plain.init(p: v@raw@raw/u8)
                viaPlain(w@uniq, spare)
                kk = 2
            """,
        ["lChainH3Bad"] = """
            func stash4(slot: raw/(raw/(raw/(raw/u8))), value: raw/u8) => ()
            func lChainH3Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h4: u8 = 0
                var h3: raw/u8 = h4@raw
                var h2: raw/u16 = h3@raw@raw/u16
                var h1: raw/u32 = h2@raw@raw/u32
                stash4(h1@raw@raw/(raw/(raw/(raw/u8))), spare)
                kk = 2
                usep(h3)
            """,
        ["qTwoHopBad"] = """
            func stash4(slot: raw/(raw/(raw/(raw/u8))), value: raw/u8) => ()
            func qTwoHopBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var hop2: raw/u8 = v@raw@raw/u8
                var hop1: raw/u8 = hop2@raw@raw/u8
                stash4(hop1@raw@raw/(raw/(raw/(raw/u8))), spare)
                kk = 2
                let z = v
            """,
        ["pPlainPastBad"] = """
            #Layout("C")
            struct PlainC
                public var p: raw/u8
                public init(p: raw/u8) => self.p = p
            func viaPast(slot: raw/(raw/PlainC), value: raw/u8) => ()
            func pPlainPastBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: raw/u8 = null
                var h: raw/u64 = v@raw@raw/u64
                var hp: raw/PlainC = h@raw@raw/PlainC
                viaPast(hp@raw, spare)
                kk = 2
                usep(v)
            """,
        ["zEnumTupPtrOk"] = """
            func viaTO(slot: raw/((raw/u8, Option<raw/(raw/u8)>)), value: raw/u8) => ()
            func zEnumTupPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: (raw/u64, raw/u64, raw/u64) = (x@raw@raw/u64, null, null)
                viaTO(h@raw@raw/((raw/u8, Option<raw/(raw/u8)>)), spare)
                kk = 2
                usep(x)
            """,
        ["zFnTupPtrOk"] = """
            func viaTF(slot: raw/((raw/u8, () -> ())), value: raw/u8) => ()
            func zFnTupPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: (raw/u64, raw/u64, raw/u64) = (x@raw@raw/u64, null, null)
                viaTF(h@raw@raw/((raw/u8, () -> ())), spare)
                kk = 2
                usep(x)
            """,
        ["zEnumTupMixOk"] = """
            func viaTO(slot: raw/((raw/u8, Option<raw/(raw/u8)>)), value: raw/u8) => ()
            func zEnumTupMixOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var w: (raw/u64, u64, u64) = (x@raw@raw/u64, 0@u64, 0@u64)
                viaTO(w@raw@raw/((raw/u8, Option<raw/(raw/u8)>)), spare)
                kk = 2
            """,
        ["zFnTupMixOk"] = """
            func viaTF(slot: raw/((raw/u8, () -> ())), value: raw/u8) => ()
            func zFnTupMixOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var w: (raw/u64, u64, u64) = (x@raw@raw/u64, 0@u64, 0@u64)
                viaTF(w@raw@raw/((raw/u8, () -> ())), spare)
                kk = 2
            """,
        ["zTupPtrOnlyOk"] = """
            func viaTU(slot: raw/((raw/u8, u64)), value: raw/u8) => ()
            func zTupPtrOnlyOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var w: (raw/u64, u64, u64) = (x@raw@raw/u64, 0@u64, 0@u64)
                viaTU(w@raw@raw/((raw/u8, u64)), spare)
                kk = 2
                usep(x)
            """,
        ["zEnumTupPtrBad"] = """
            func viaTO(slot: raw/((raw/u8, Option<raw/(raw/u8)>)), value: raw/u8) => ()
            func zEnumTupPtrBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: (raw/u64, raw/u64, raw/u64) = (x@raw@raw/u64, null, null)
                viaTO(h@raw@raw/((raw/u8, Option<raw/(raw/u8)>)), spare)
                kk = 2
                let z = h
            """,
        ["zEnumTupMixBad"] = """
            func viaTO(slot: raw/((raw/u8, Option<raw/(raw/u8)>)), value: raw/u8) => ()
            func zEnumTupMixBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var w: (raw/u64, u64, u64) = (x@raw@raw/u64, 0@u64, 0@u64)
                viaTO(w@raw@raw/((raw/u8, Option<raw/(raw/u8)>)), spare)
                kk = 2
                let z = w
            """,
        ["zEnumTupScalarBad"] = """
            func viaTO(slot: raw/((raw/u8, Option<raw/(raw/u8)>)), value: raw/u8) => ()
            func zEnumTupScalarBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: [4 of u64] = [0@u64, 0@u64, 0@u64, 0@u64]
                viaTO(x@raw@raw/((raw/u8, Option<raw/(raw/u8)>)), spare)
                kk = 2
                let z = x
            """,
        ["zRecMixBad"] = """
            func keepP(slot: raw/(raw/((raw/u8, u64))), a: ref/u8, value: raw/u8) => ()
            func zRecMixBad(a: ref/u8)
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var w: (raw/u64, u64, u64) = (x@raw@raw/u64, 0@u64, 0@u64)
                keepP(w@raw@raw/(raw/((raw/u8, u64))), a, spare)
                kk = 2
            """,
        ["kStatedPtrOk"] = """
            func keepS(slot: raw/((raw/(raw/u8) during a, u64)), a: uniq/(raw/u8), value: raw/u8) => ()
            func kStatedPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var cell: raw/u8 = null
                var v: raw/u8 = null
                var h: (raw/u16, u64) = (v@raw@raw/u16, 0@u64)
                let r2 = cell@uniq
                let p = h@raw@raw/((raw/(raw/u8) during r2, u64))
                keepS(p, r2, spare)
                kk = 2
            """,
        ["kStatedVOk"] = """
            func keepS(slot: raw/((raw/(raw/u8) during a, u64)), a: uniq/(raw/u8), value: raw/u8) => ()
            func kStatedVOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var cell: raw/u8 = null
                var v: raw/u8 = null
                var h: (raw/u16, u64) = (v@raw@raw/u16, 0@u64)
                let r2 = cell@uniq
                let p = h@raw@raw/((raw/(raw/u8) during r2, u64))
                keepS(p, r2, spare)
                kk = 2
                usep(v)
            """,
        ["kStatedPtrBad"] = """
            func keepS(slot: raw/((raw/(raw/u8) during a, u64)), a: uniq/(raw/u8), value: raw/u8) => ()
            func kStatedPtrBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var cell: raw/u8 = null
                var v: raw/u8 = null
                var h: (raw/u16, u64) = (v@raw@raw/u16, 0@u64)
                let r2 = cell@uniq
                let p = h@raw@raw/((raw/(raw/u8) during r2, u64))
                keepS(p, r2, spare)
                kk = 2
                usep(cell)
            """,
        ["fObjPtrOk"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keepT(slot: raw/((raw/u8, u64)), value: raw/u8) => ()
            func fObjPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: u8 = 0
                var item = Cel.init()@obj
                var h = (x@raw, item@objref)
                keepT(h@raw@raw/((raw/u8, u64)), spare)
                kk = 2
            """,
        ["fObjPtrBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keepT(slot: raw/((raw/u8, u64)), value: raw/u8) => ()
            func fObjPtrBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: u8 = 0
                var item = Cel.init()@obj
                var h = (x@raw, item@objref)
                keepT(h@raw@raw/((raw/u8, u64)), spare)
                kk = 2
                usep(h.0)
            """,
        ["fRcPtrOk"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keepT(slot: raw/((raw/u8, u64)), value: raw/u8) => ()
            func fRcPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: u8 = 0
                var h = (x@raw, Cel.init()@rc)
                keepT(h@raw@raw/((raw/u8, u64)), spare)
                kk = 2
            """,
        ["fRcPtrBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keepT(slot: raw/((raw/u8, u64)), value: raw/u8) => ()
            func fRcPtrBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: u8 = 0
                var h = (x@raw, Cel.init()@rc)
                keepT(h@raw@raw/((raw/u8, u64)), spare)
                kk = 2
                usep(h.0)
            """,
        ["fObjPtrStoreBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fObjPtrStoreBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: u8 = 0
                var item = Cel.init()@obj
                var h = (x@raw, item@objref)
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["fObjUniqOnlyOk"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func fObjUniqOnlyOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var item = Cel.init()@obj
                var h = item@objuniq
                keep1(h@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["fObjUniqOnlyBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fObjUniqOnlyBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var item = Cel.init()@obj
                var h = item@objuniq
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["fObjRefOnlyBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fObjRefOnlyBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var item = Cel.init()@obj
                var h = item@objref
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["fRefOnlyBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fRefOnlyBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["fRcOnlyBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fRcOnlyBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h = Cel.init()@rc
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["aUniqBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func aUniqBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@uniq
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["aObjBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func aObjBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h = Cel.init()@obj
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["aArcBad"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func aArcBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h = Cel.init()@arc
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["bRefOk"] = """
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func bRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                keep1(h@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["bUniqOk"] = """
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func bUniqOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@uniq
                keep1(h@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["bObjRefOk"] = """
            struct Cel
                public var v: i32 = 0
                public init() => ()
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func bObjRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var item = Cel.init()@obj
                var h = item@objref
                keep1(h@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["cRefNoLoanOk"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func cRefNoLoanOk()
                var kk: u8 = 1
                var x: u8 = 0
                var y: i32 = 5
                var h = y@ref
                keep2(h@raw@raw/(raw/(raw/u8)), x@raw)
                kk = 2
            """,
        ["dRefYBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func usei(v: i32) => ()
            func dRefYBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
                usei(y)
            """,
        ["dUniqYOk"] = """
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func usei(v: i32) => ()
            func dUniqYOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@uniq
                keep1(h@raw@raw/(raw/u8), spare)
                kk = 2
                usei(y)
            """,
        ["eTupRowRefBad"] = """
            func keepT2(slot: raw/((raw/(raw/u8), u64)), value: raw/u8) => ()
            func eTupRowRefBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                keepT2(h@raw@raw/((raw/(raw/u8), u64)), spare)
                kk = 2
            """,
        ["hZeroRefOk"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hZeroRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: [0 of ref/i32] = []
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hOneRefBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hOneRefBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h: [1 of ref/i32] = [y@ref]
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hZeroPairOk"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hZeroPairOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: ([0 of u64], [0 of ref/i32]) = ([], [])
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hOnePairBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hOnePairBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: ([1 of u64], [0 of ref/i32]) = ([1 of 0@u64], [])
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hUnitOk"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hUnitOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h = ()
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hUnitPairOk"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hUnitPairOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: ((), [0 of ref/i32]) = ((), [])
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hUnitPairOneBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hUnitPairOneBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h: ((), [1 of ref/i32]) = ((), [y@ref])
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hFnItemOk"] = """
            func nop() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hFnItemOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h = nop
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hFnItemPairOk"] = """
            func nop() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hFnItemPairOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let e: [0 of ref/i32] = []
                var h = (nop, e)
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["hFnItemRefBad"] = """
            func nop() => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func hFnItemRefBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = (nop, y@ref)
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["iEnumRefBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func iEnumRefBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h: Option<ref/i32> = .Some(y@ref)
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["iStructRefBad"] = """
            struct Box {a}
                public var p: ref/i32 during a
                public init(p: ref/i32 during a) => self.p = p
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func iStructRefBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = Box.init(y@ref)
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["jHidRefOk"] = """
            func wa<U>(p: raw/U, value: raw/u8) => ()
            func jHidRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                wa(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["jHidPartScalarOk"] = """
            func wa<U>(p: raw/U, value: raw/u8) => ()
            func jHidPartScalarOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = (7@u64, y@ref)
                wa(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["jHidPartRefOk"] = """
            func wa<U>(p: raw/U, value: raw/u8) => ()
            func jHidPartRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = (y@ref, y@ref)
                wa(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["jHidWholeRefOk"] = """
            func wz<U>(p: U, value: raw/u8) => ()
            func jHidWholeRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                wz(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["jHidWholeScalarOk"] = """
            func wz<U>(p: U, value: raw/u8) => ()
            func jHidWholeScalarOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = (7@u64, y@ref)
                wz(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["jGenCalleeBad"] = """
            func wb<U>(p: raw/(raw/(raw/u8)), q: U, value: raw/u8) => ()
            func jGenCalleeBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                wb(h@raw@raw/(raw/(raw/u8)), 0, spare)
                kk = 2
            """,
        ["jShowPartScalarBad"] = """
            func wd<U>(p: raw/(raw/U), value: U) => ()
            func jShowPartScalarBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = (7@u64, y@ref)
                wd(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["twoTupleSlotBad"] = """
            func keepTup(slot: (raw/(raw/(raw/u8)), i32), value: raw/u8) => ()

            func twoTupleSlot(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepTup((hop@raw, 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["twoTupleSlotOk"] = """
            func keepTup1(slot: (raw/(raw/u8), i32), value: raw/u8) => ()

            func twoTupleSlotOne(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                keepTup1((cell@raw, 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["twoTupleTwoBad"] = """
            func keepTup2(slot: (raw/(raw/u8), raw/(raw/(raw/u8))), value: raw/u8) => ()

            func twoTupleTwo(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var cell2: raw/u8 = null
                var hop2: raw/(raw/u8) = cell2@raw
                keepTup2((cell@raw, hop2@raw), w@raw@raw/u8)
                y@follow = 5
            """,
        ["twoTuplePunBad"] = """
            func keepTup(slot: (raw/(raw/(raw/u8)), i32), value: raw/u8) => ()

            func twoTuplePun(y: uniq/i32)
                let w = y@follow@ref
                let z: i32 = 0
                let t = z@ref
                keepTup((t@raw@raw/(raw/(raw/u8)), 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1OptSlotBad"] = """
            func keepOpt(slot: Option<raw/(raw/(raw/u8))>, value: raw/u8) => ()

            func optSlot(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepOpt(Option.Some(hop@raw), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1OptSlotOk"] = """
            func keepOpt1(slot: Option<raw/(raw/u8)>, value: raw/u8) => ()

            func optSlotOne(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                keepOpt1(Option.Some(cell@raw), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1TupUniqSlotBad"] = """
            func keepTupU(slot: (uniq/(raw/(raw/u8)) during s, i32), value: raw/u8) => ()

            func tupUniqSlot(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepTupU((hop@uniq, 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1TupUniqSlotOk"] = """
            func keepTupU1(slot: (uniq/(raw/u8) during s, i32), value: raw/u8) => ()

            func tupUniqSlotOne(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                keepTupU1((cell@uniq, 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1TupSharedOk"] = """
            func keepTupR(slot: (ref/(raw/(raw/u8)) during s, i32), value: raw/u8) => ()

            func tupShared(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepTupR((hop@ref, 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1DispTupBad"] = """
            func keepTup(slot: (raw/(raw/(raw/u8)), i32), value: raw/u8) => ()

            func h1DispTup(y: uniq/i32)
                let w = y@follow@ref
                var cells: [2 of raw/(raw/u8)] = [null, null]
                var d: raw/(raw/(raw/u8)) = null
                unsafe => d = cells@raw@raw/(raw/(raw/u8)) + 1
                keepTup((d, 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1DispTupOk"] = """
            func keepTup1(slot: (raw/(raw/u8), i32), value: raw/u8) => ()

            func h1DispTupOne(y: uniq/i32)
                let w = y@follow@ref
                var cells: [2 of raw/u8] = [null, null]
                var d: raw/(raw/u8) = null
                unsafe => d = cells@raw@raw/(raw/u8) + 1
                keepTup1((d, 1), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1MixTupleBad"] = """
            func keepMix(slot: (raw/(raw/(raw/u8)), raw/i32 during a), anchor: ref/i32 during a, value: raw/u8) => ()

            func mixTuple(y: uniq/i32)
                let w = y@follow@ref
                var k: i32 = 0
                let ka = k@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepMix((hop@raw, ka@follow@raw), ka, w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1MixTupleOk"] = """
            func keepMix1(slot: (raw/(raw/u8), raw/i32 during a), anchor: ref/i32 during a, value: raw/u8) => ()

            func mixTupleOne(y: uniq/i32)
                let w = y@follow@ref
                var k: i32 = 0
                let ka = k@ref
                var cell: raw/u8 = null
                keepMix1((cell@raw, ka@follow@raw), ka, w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1MixStaticBad"] = """
            func keepMixS(slot: (raw/(raw/(raw/u8)), raw/u8 during static), value: raw/u8) => ()

            func mixStatic(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepMixS((hop@raw, null), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1Mix3Bad"] = """
            func keepMix3(slot: (raw/(raw/(raw/u8)), raw/u8 during static, uniq/(raw/(raw/u8)) during s), value: raw/u8) => ()

            func mix3(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                var cell2: raw/u8 = null
                var hop2: raw/(raw/u8) = cell2@raw
                keepMix3((hop@raw, null, hop2@uniq), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1Mix3Ok"] = """
            func keepMix3One(slot: (raw/(raw/u8), raw/u8 during static, uniq/(raw/u8) during s), value: raw/u8) => ()

            func mix3One(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var cell2: raw/u8 = null
                keepMix3One((cell@raw, null, cell2@uniq), w@raw@raw/u8)
                y@follow = 5
            """,
        ["h1Mix3OnlyBorrowBad"] = """
            func keepMix3B(slot: (raw/(raw/u8), raw/u8 during static, uniq/(raw/(raw/u8)) during s), value: raw/u8) => ()

            func mix3B(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var cell2: raw/u8 = null
                var hop2: raw/(raw/u8) = cell2@raw
                keepMix3B((cell@raw, null, hop2@uniq), w@raw@raw/u8)
                y@follow = 5
            """,
    };

    // The coverage table's rows (U3b): the 17 forms the first step's reading newly rejects (12 of PLAN G784's 13, pPlainViewBad, F2 unit
    // B's four) and pPlainFirstBad, the two P3 forms (invalid), the Ok twins and the valid views that must stay accepted, design review
    // 1's B1 three and G667a's four (rejected as on dev), the typed controls, and the rows past a holder that U3c moves; and the
    // implementation review's F1 rows: a reading Type with a part that is no raw Place beside a raw pointer whose pointee admits no
    // Loan (four valid forms, their control and invalid twins), a row whose pointee admits the argument, a row stating an Origin;
    // and its F2 rows: a safe reference or a handle beside a raw pointer, and a Place that is one alone; and PLAN G785's rows (the
    // state file a5v-r3ref-state.md): a stepping row over a byte a punned write may not change (a reference's, a handle's, an enum's
    // or a struct Field's) reaches storage the body does not own, an array of no element, Unit and a Function Item have no byte of
    // either kind (the owner's O-356 (a)), and a layer
    // hidden in the called function's Type parameter's binding adds no reach (SPEC 15.6.4). PLAN G841's clauses (1) and (2): the first
    // step reads every layer of the input's key, one an argument holds by value (a Tuple element, an Option payload) and one beside a
    // layer stating an Origin included, so SPEC 15.6.4's composition (twoLayers, punnedRef) holds there too.
    [Theory]
    [InlineData("fMixedPunChainBad", "fMixedPunChainBad: kk = 2; fMixedPunChainBad: var z: u8 = 5")] // I, PLAN G784
    [InlineData("qIntChainMixedBad", "qIntChainMixedBad: kk = 2")] // I, PLAN G784
    [InlineData("qPartPunBad", "qPartPunBad: kk = 2")] // I, PLAN G784
    [InlineData("sSpanBad", "sSpanBad: kk = 2")] // I, PLAN G784
    [InlineData("sHBufBad", "sHBufBad: kk = 2")] // I, PLAN G784
    [InlineData("sHPairBad", "sHPairBad: kk = 2")] // I, PLAN G784
    [InlineData("sHTupElemBad", "sHTupElemBad: kk = 2")] // I, PLAN G784
    [InlineData("sHArrElemBad", "sHArrElemBad: kk = 2")] // I, PLAN G784
    [InlineData("lChainH2Bad", "lChainH2Bad: kk = 2")] // I, PLAN G784
    [InlineData("lTupLeafBad", "lTupLeafBad: kk = 2")] // I, PLAN G784
    [InlineData("lArrLeafBad", "lArrLeafBad: kk = 2")] // I, PLAN G784
    [InlineData("xParamBad", "xParamBad: kk = 2")] // I, PLAN G784
    [InlineData("pPlainViewBad", "pPlainViewBad: kk = 2")] // I, a declaration's own layer (5.7 Calls, throughField)
    [InlineData("pPlainFirstBad", "pPlainFirstBad: kk = 2")] // I, a declaration's own layer (5.7 Calls, throughField)
    [InlineData("fPastLoanFreeBad", "fPastLoanFreeBad: kk = 2")] // I, F2 unit B
    [InlineData("qPastHopRawBad", "qPastHopRawBad: kk = 2")] // I, F2 unit B
    [InlineData("kLoanHoldConvBad", "kLoanHoldConvBad: kk = 2")] // I, F2 unit B
    [InlineData("xParamUntrackedBad", "xParamUntrackedBad: kk = 2")] // I, F2 unit B
    [InlineData("fPastLoanFreeOk", "fPastLoanFreeOk: kk = 2")] // I, I by P3 (15.6.4, twoLayers)
    [InlineData("kLoanHoldConvOk", "kLoanHoldConvOk: kk = 2")] // I, I by P3 (15.6.4, twoLayers)
    [InlineData("xParamOk", "")] // V, must stay accepted
    [InlineData("sSpanOk", "")] // V, must stay accepted
    [InlineData("sHBufOk", "")] // V, must stay accepted
    [InlineData("sHPairOk", "")] // V, must stay accepted
    [InlineData("sHTupElemOk", "")] // V, must stay accepted
    [InlineData("sHArrElemOk", "")] // V, must stay accepted
    [InlineData("pStaticViewOk", "")] // V, must stay accepted
    [InlineData("pWrapViewOk", "")] // V, must stay accepted
    [InlineData("zEnumViewOk", "")] // V, must stay accepted
    [InlineData("zEnumFieldOk", "")] // V, must stay accepted
    [InlineData("zFnViewOk", "")] // V, must stay accepted
    [InlineData("zLeafViewOk", "")] // V, must stay accepted
    [InlineData("zBorrowAloneOk", "")] // V, must stay accepted
    [InlineData("zBorrowMixBad", "zBorrowMixBad: kk = 2; zBorrowMixBad: var x3: raw/(raw/u8) = null")] // I, design review 1's B1; U3c adds x3's storage end (E may hold x3's Loan: an independent failure, Q13)
    [InlineData("zPunReadBad", "zPunReadBad: kk = 2")] // I, design review 1's B1
    [InlineData("zRawMixBad", "zRawMixBad: kk = 2")] // I, design review 1's B1
    [InlineData("gPunReadPtrBad", "gPunReadPtrBad: kk = 2")] // I, G667a's (R)
    [InlineData("gCallStoreConvBad", "gCallStoreConvBad: kk = 2")] // I, G667a's (R)
    [InlineData("sTupleSideOk", "sTupleSideOk: kk = 2")] // I, G667a's (R)
    [InlineData("sConvFieldBad", "sConvFieldBad: kk = 2")] // I, G667a's (R)
    [InlineData("zEnumTypedBad", "zEnumTypedBad: kk = 2")] // I, typed: as dev
    [InlineData("pWrapViewBad", "pWrapViewBad: kk = 2")] // I, typed: as dev
    [InlineData("pPlainViewDeclBad", "pPlainViewDeclBad: kk = 2")] // I, typed: as dev
    [InlineData("pPlainDeclBad", "pPlainDeclBad: kk = 2")] // I, typed: as dev
    [InlineData("pPlainDeclOk", "pPlainDeclOk: kk = 2")] // I, typed: as dev
    [InlineData("lChainH3Bad", "lChainH3Bad: kk = 2")] // I, past a holder: rejected from U3c (MirBorrowsTest.PastHolder)
    [InlineData("qTwoHopBad", "qTwoHopBad: kk = 2")] // I, past a holder: rejected from U3c (MirBorrowsTest.PastHolder)
    [InlineData("pPlainPastBad", "pPlainPastBad: kk = 2")] // I, past a holder: rejected from U3c (hp may hold the call's own pointer; 15.6.4's composition)
    [InlineData("zEnumTupPtrOk", "")] // V, review F1: an Option part is no raw Place; the row raw/u8 stores only a u8
    [InlineData("zFnTupPtrOk", "")] // V, review F1: a Function part designates nothing
    [InlineData("zEnumTupMixOk", "")] // V, review F1: no storage the body does not own through the row raw/u8
    [InlineData("zFnTupMixOk", "")] // V, review F1
    [InlineData("zTupPtrOnlyOk", "")] // V, review F1's control: no part but raw Places
    [InlineData("zEnumTupPtrBad", "zEnumTupPtrBad: kk = 2")] // I, review F1: the first step's own write, h used after
    [InlineData("zEnumTupMixBad", "zEnumTupMixBad: kk = 2")] // I, review F1: the first step's own write, w used after
    [InlineData("zEnumTupScalarBad", "zEnumTupScalarBad: kk = 2")] // I, review F1: the first step's own write, x used after
    [InlineData("zRecMixBad", "zRecMixBad: kk = 2")] // I, a row whose pointee admits the argument: storage the body does not own keeps it
    [InlineData("kStatedPtrOk", "kStatedPtrOk: kk = 2; kStatedPtrOk: var cell: raw/u8 = null")] // I, a row stating an Origin over Scalar bytes (15.6 Holders)
    [InlineData("kStatedVOk", "kStatedVOk: kk = 2; kStatedVOk: var cell: raw/u8 = null")] // I, as kStatedPtrOk, v used after
    [InlineData("kStatedPtrBad", "kStatedPtrBad: kk = 2; kStatedPtrBad: usep(cell); kStatedPtrBad: var cell: raw/u8 = null")] // I, cell used after
    [InlineData("fObjPtrOk", "")] // V, review F2: a safe reference beside a raw pointer; the row raw/u8 stores only a u8
    [InlineData("fObjPtrBad", "fObjPtrBad: kk = 2")] // I, review F2: the first step's own write, h used after
    [InlineData("fRcPtrOk", "")] // V, review F2: a handle beside a raw pointer; its destruction observes nothing (15.6.6)
    [InlineData("fRcPtrBad", "fRcPtrBad: kk = 2")] // I, review F2: the first step's own write, h used after
    [InlineData("fObjPtrStoreBad", "fObjPtrStoreBad: kk = 2; fObjPtrStoreBad: var item = Cel.init()@obj")] // I, a stepping row over h's pointer bytes: the call's own pointer (15.6.4's composition); item's Loan is kept there too
    [InlineData("fObjUniqOnlyOk", "")] // V, review F2: a row raw/u8 over a reference's bytes stores only a u8
    [InlineData("fObjUniqOnlyBad", "fObjUniqOnlyBad: kk = 2; fObjUniqOnlyBad: var item = Cel.init()@obj")] // I, 15.6 Holders (example punnedRef): a row stepping over a reference's bytes reaches storage the body does not own (PLAN G785); E keeps h's Loan on item (15.6.4, reading (a))
    [InlineData("fObjRefOnlyBad", "fObjRefOnlyBad: kk = 2; fObjRefOnlyBad: var item = Cel.init()@obj")] // I, as fObjUniqOnlyBad
    [InlineData("fRefOnlyBad", "fRefOnlyBad: kk = 2; fRefOnlyBad: var y: i32 = 5")] // I, 15.6's example punnedRef itself (PLAN G785)
    [InlineData("fRcOnlyBad", "fRcOnlyBad: kk = 2")] // I, a handle's bytes: rejected as on dev
    [InlineData("aUniqBad", "aUniqBad: kk = 2; aUniqBad: var y: i32 = 5")] // I, PLAN G785: a `uniq` reference's bytes
    [InlineData("aObjBad", "aObjBad: kk = 2")] // I, an `obj` handle's bytes
    [InlineData("aArcBad", "aArcBad: kk = 2")] // I, an `arc` handle's bytes
    [InlineData("bRefOk", "")] // V, the row raw/u8 stores only a u8 (5.7 Calls)
    [InlineData("bUniqOk", "")] // V, as bRefOk
    [InlineData("bObjRefOk", "")] // V, as bRefOk
    [InlineData("cRefNoLoanOk", "cRefNoLoanOk: var y: i32 = 5")] // I, E keeps what the chain of reads through slot carries, h's Loan on y (15.6.4, reading (a)); value is an address only
    [InlineData("dRefYBad", "dRefYBad: kk = 2; dRefYBad: var y: i32 = 5")] // I, y read after: a shared Loan does not conflict with the read
    [InlineData("dUniqYOk", "")] // V, nothing stored through the row raw/u8; h is dead at the read of y
    [InlineData("eTupRowRefBad", "eTupRowRefBad: kk = 2; eTupRowRefBad: var y: i32 = 5")] // I, a Tuple's stepping row over a reference's bytes
    [InlineData("hZeroRefOk", "")] // V, an array of no element has no byte (5.2: a punned Place is positive-sized)
    [InlineData("hOneRefBad", "hOneRefBad: kk = 2; hOneRefBad: var y: i32 = 5")] // I, an array of one reference
    [InlineData("hZeroPairOk", "")] // V, no byte: an array of no element holds no owner Scalar's byte either (5.2)
    [InlineData("hOnePairBad", "hOnePairBad: kk = 2")] // I, an owner Scalar's bytes (the first clause)
    [InlineData("hUnitOk", "")] // V, Unit has no byte (IMPL 21.1: size 0; the planning session's reading 2; the owner's O-356 (a))
    [InlineData("hUnitPairOk", "")] // V, ((), [0 of ref/i32]) has no byte
    [InlineData("hUnitPairOneBad", "hUnitPairOneBad: kk = 2; hUnitPairOneBad: var y: i32 = 5")] // I, a reference's bytes beside Unit
    [InlineData("hFnItemOk", "")] // V, a Function Item value has no byte (IMPL 21.1: size 0)
    [InlineData("hFnItemPairOk", "")] // V, (Function Item, [0 of ref/i32]) has no byte
    [InlineData("hFnItemRefBad", "hFnItemRefBad: kk = 2; hFnItemRefBad: var y: i32 = 5")] // I, a reference's bytes beside a Function Item
    [InlineData("iEnumRefBad", "iEnumRefBad: kk = 2; iEnumRefBad: var y: i32 = 5")] // I, an enum: its own bytes and its payload's reference bytes
    [InlineData("iStructRefBad", "iStructRefBad: kk = 2; iStructRefBad: var y: i32 = 5")] // I, a struct Field of a reference Type
    [InlineData("jHidRefOk", "")] // V, a layer hidden in the callee's Type parameter's binding adds no reach (15.6.4)
    [InlineData("jHidPartScalarOk", "")] // V, as jHidRefOk over a holder with owner Scalar bytes
    [InlineData("jHidPartRefOk", "")] // V, as jHidRefOk over two references
    [InlineData("jHidWholeRefOk", "")] // V, the whole parameter Type hidden
    [InlineData("jHidWholeScalarOk", "")] // V, as jHidWholeRefOk over owner Scalar bytes
    [InlineData("jGenCalleeBad", "jGenCalleeBad: kk = 2; jGenCalleeBad: var y: i32 = 5")] // I, a generic callee whose stepping row is shown
    [InlineData("jShowPartScalarBad", "jShowPartScalarBad: kk = 2")] // I, the stepping layers shown above the hidden one (its dependent `var y` record went with the A5 landing: DECISIONS Q13)
    [InlineData("twoTupleSlotBad", "twoTupleSlot: y@follow = 5")] // I, PLAN G841 (1): SPEC 15.6.4's twoLayers with the slot pointer held by value as a Tuple element (retained as a raw pointer argument is)
    [InlineData("twoTupleSlotOk", "")] // V, its oneLayer twin (the next layer u8)
    [InlineData("twoTupleTwoBad", "twoTupleTwo: y@follow = 5")] // I, two slot pointers in one Tuple, the second written through
    [InlineData("twoTuplePunBad", "twoTuplePun: y@follow = 5; twoTuplePun: let z: i32 = 0")] // I, punnedRef with the slot pointer as a Tuple element (its records as punnedRef's)
    [InlineData("h1OptSlotBad", "optSlot: y@follow = 5")] // I, through an Option payload (5.7 Calls: a Type argument's layer)
    [InlineData("h1OptSlotOk", "")] // V
    [InlineData("h1TupUniqSlotBad", "tupUniqSlot: y@follow = 5")] // I, an exclusive borrow held by value in a Tuple
    [InlineData("h1TupUniqSlotOk", "")] // V
    [InlineData("h1TupSharedOk", "")] // V, a shared borrow: no write past it
    [InlineData("h1DispTupBad", "h1DispTup: y@follow = 5")] // I, the slot element displaced within cells (5.3; N4's row: the limit meets its label)
    [InlineData("h1DispTupOk", "")] // V
    [InlineData("h1MixTupleBad", "mixTuple: y@follow = 5; mixTuple: var k: i32 = 0")] // I, PLAN G841 (2): the layer without an Origin beside one stating an Origin is read as at a key of one kind (5.7 Calls decides per layer); k's storage end too
    [InlineData("h1MixTupleOk", "")] // V
    [InlineData("h1MixStaticBad", "mixStatic: y@follow = 5")] // I, the second element states `static`
    [InlineData("h1Mix3Bad", "mix3: y@follow = 5")] // I, three kinds of part: an unstated raw layer, a `static` one, an exclusive borrow
    [InlineData("h1Mix3Ok", "")] // V
    [InlineData("h1Mix3OnlyBorrowBad", "mix3B: y@follow = 5")] // I, the composition through the borrow element alone
    public void TheFirstStepReadsAtTheReadingPathsType(string form, string expected)
    {
        var c = MirBuildTest.Compile(FirstStepShapes + FirstStepForms[form] + "\npublic func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes))}]");
    }
}
