// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's W reads its first step at the reading path's Type, the input's pointee or referent (SPEC 15.6 Holders, 5.7 Calls,
/// 15.6.4; the A5 value redesign's unit U3b, the owner's O-209): a Place that Type does not designate (SPEC 5.2, 4.1) is read punned
/// where the Type has a raw pointer raw Place within, the holder's state at the reading Type's guard beside its own, and storage the
/// body does not own where that guard steps over an `owner` Scalar's byte. The rows are the unit's coverage table, each labelled from
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
    };

    // The coverage table's rows (U3b): the 17 forms the first step's reading newly rejects (12 of PLAN G784's 13, pPlainViewBad, F2 unit
    // B's four) and pPlainFirstBad, the two P3 forms (invalid), the Ok twins and the valid views that must stay accepted, design review
    // 1's B1 three and G667a's four (rejected as on dev), the typed controls, and the rows past a holder that U3c moves.
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
    public void TheFirstStepReadsAtTheReadingPathsType(string form, string expected)
    {
        var c = MirBuildTest.Compile(FirstStepShapes + FirstStepForms[form] + "\npublic func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes))}]");
    }
}
