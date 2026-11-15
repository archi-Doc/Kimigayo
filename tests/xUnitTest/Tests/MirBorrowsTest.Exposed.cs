// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's holders are the locals a Loan can reach (SPEC 15.6 Holders: a holder carries the Loans of the values stored in every
/// part of it; the A5 value redesign's unit U6): a local whose Type holds no Loan is a holder once a pointer the body formed may write
/// it, through an address of it, of an element or a Field (15.6 Addresses), or a raw pointer or a call result carrying an exclusive
/// Loan on it (15.6.3, 15.6.4); a store through that pointer lands there, a punned one over its Scalars included (5.2), and its Loans
/// live while the local or a pointer addressing it is used. A shared borrow writes nothing (5.2.1 condition 3), and a raw layer
/// whose pointee holds no Loan stores none (5.7 Calls). The rows are the unit's coverage table, each labelled from SPEC (I invalid, V
/// valid; P3: RESULT.md batch 1's settled label), the record each invalid one gives (SPEC 23.1.2); the valid rows marked fail-before
/// were rejected only by R4-M120's untracked rule, which sent the store to storage the body does not own.</summary>
public sealed partial class MirBorrowsTest
{
    private const string ExposedShapes = "func usep<T>(p: raw/T) => ()\n";

    private static readonly Dictionary<string, string> ExposedForms = new()
    {
        ["fScalarLetPtrOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fScalarLetPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let w: u64 = 3
                let p = w@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fScalarVarPtrOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fScalarVarPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let p = w@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fScalarUsedAfterBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fScalarUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let w: u64 = 3
                let p = w@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
                let z = w
            """,
        ["fScalarPtrUsedAfterBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fScalarPtrUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let w: u64 = 3
                let p = w@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
                usep(p)
            """,
        ["eStorageEndOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func eStorageEndOk()
                var w: u64 = 3
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                keep(w@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["eStorageEndBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func eStorageEndBad()
                var w: u64 = 3
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                keep(w@raw@raw/(raw/u8), spare)
                kk = 2
                let z = w
            """,
        ["fElemPtrNoSliceOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fElemPtrNoSliceOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let values = [2 of 3@u64]
                let p = values[0]@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fElemUsedAfterBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fElemUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let values = [2 of 3@u64]
                let p = values[0]@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
                let z = values[1]
            """,
        ["fWholePtrLetOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fWholePtrLetOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let values = [2 of 3@u64]
                let p = values@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fWholeUsedAfterBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fWholeUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let values = [2 of 3@u64]
                let p = values@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
                let z = values[1]
            """,
        ["fTupleScalarPtrOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fTupleScalarPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let t = (3@u64, 4@u64)
                let p = t@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fTupleUsedAfterBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fTupleUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let t = (3@u64, 4@u64)
                let p = t@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
                let z = t.1
            """,
        ["fStructScalarPtrOk"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init()
                    self.a = 1
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fStructScalarPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let s = Pt.init()
                let p = s@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fFieldPtrOk"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init()
                    self.a = 1
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fFieldPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s = Pt.init()
                let p = s.b@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fStructUsedAfterBad"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init()
                    self.a = 1
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fStructUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s = Pt.init()
                let p = s.b@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
                let z = s.a
            """,
        ["fUniqScalarRawOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fUniqScalarRawOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let r2 = w@uniq
                let q = r2@follow@raw@raw/(raw/u8)
                keep(q, spare)
                kk = 2
            """,
        ["fUniqScalarRawBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fUniqScalarRawBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let r2 = w@uniq
                let q = r2@follow@raw@raw/(raw/u8)
                keep(q, spare)
                kk = 2
                let z = w
            """,
        ["qElemUniqOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func qElemUniqOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var values = [2 of 3@u64]
                let e = values[1]@uniq
                let q = e@follow@raw@raw/(raw/u8)
                keep(q, spare)
                kk = 2
            """,
        ["qElemUniqBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func qElemUniqBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var values = [2 of 3@u64]
                let e = values[1]@uniq
                let q = e@follow@raw@raw/(raw/u8)
                keep(q, spare)
                kk = 2
                let z = values[0]
            """,
        ["eUniqFieldOk"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init()
                    self.a = 1
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func eUniqFieldOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s = Pt.init()
                let e = s.b@uniq
                let q = e@follow@raw@raw/(raw/u8)
                keep(q, spare)
                kk = 2
            """,
        ["eUniqFieldBad"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init()
                    self.a = 1
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func eUniqFieldBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s = Pt.init()
                let e = s.b@uniq
                let q = e@follow@raw@raw/(raw/u8)
                keep(q, spare)
                kk = 2
                let z = s.a
            """,
        ["eSharedOnlyOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func eSharedOnlyOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let s = w@ref
                let q = s@follow@raw@raw/(raw/u8)
                keep(q, spare)
                kk = 2
                let z = w
            """,
        ["kCalleeConvOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func view1(p: uniq/u64) -> raw/(raw/u8) during p => p@follow@raw@raw/(raw/u8)
            func kCalleeConvOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let q = view1(w@uniq)
                keep(q, spare)
                kk = 2
            """,
        ["kCalleeConvBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func view(p: uniq/u64) -> raw/(raw/(raw/u8)) during p => p@follow@raw@raw/(raw/(raw/u8))
            func kCalleeConvBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let q = view(w@uniq)
                keep2(q, spare)
                kk = 2
            """,
        ["fSliceKeepDirectOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func fSliceKeepDirectOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let values = [2 of 3@u64]
                let sl: Slice<u64> = values[..]
                let p = sl[0]@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
            """,
        ["fSliceUsedAfterBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func usev(v: ref/[2 of u64]) => ()
            func fSliceUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let values = [2 of 3@u64]
                let sl: Slice<u64> = values[..]
                let p = sl[0]@raw@raw/(raw/u8)
                keep(p, spare)
                kk = 2
                usev(values@ref)
            """,
        ["aSliceTupleNoWriteOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func aSliceTupleNoWriteOk(flag: bool)
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                let values = [2 of 3@u64]
                let sl: Slice<u64> = values[..]
                var o = (sl, [0 of other@raw])
                let tp = o@raw@raw/u16
                keep(o.1[0], spare)
                kk = 2
            """,
        ["qPunLoanOk"] = """
            func qPunLoanOk()
                var y: i32 = 1
                var x: i64 = 0
                let q = x@raw
                let q1: raw/(ref/i32) = q@raw/(ref/i32)
                unsafe => Raw.initialize(q1, y@ref)
                y = 5
            """,
        ["uHandleBytesOk"] = """
            struct Node
                public var v: i32
                public init(v: i32) => self.v = v
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func uHandleBytesOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: obj/Node = Node.init(5)@obj
                keep4(h@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["uHandleBytesBad"] = """
            struct Node
                public var v: i32
                public init(v: i32) => self.v = v
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func uHandleBytesBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var h: obj/Node = Node.init(5)@obj
                keep2(h@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["qIntChainNarrowOk"] = """
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func qIntChainNarrowOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let q = w@raw@raw/(raw/u8)
                keep4(q, spare)
                kk = 2
            """,
        ["sBufOk"] = """
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func sBufOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var buf = [8 of 0@u8]
                keep4(buf@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["sPairOk"] = """
            func keep4(slot: raw/(raw/u8), value: raw/u8) => ()
            func sPairOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: (u32, u32) = (1@u32, 2@u32)
                keep4(w@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["sTupElemOk"] = """
            func keepT2(slot: raw/(raw/u8, u64), value: raw/u8) => ()
            func sTupElemOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: (u64, u64) = (3@u64, 4@u64)
                keepT2(w@raw@raw/(raw/u8, u64), spare)
                kk = 2
            """,
        ["sArrElemOk"] = """
            func keepA2(slot: raw/[2 of raw/u8], value: raw/u8) => ()
            func sArrElemOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: (u64, u64) = (3@u64, 4@u64)
                keepA2(w@raw@raw/[2 of raw/u8], spare)
                kk = 2
            """,
        ["sBufBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func sBufBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var buf = [8 of 0@u8]
                keep2(buf@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["sPairBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func sPairBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: (u32, u32) = (1@u32, 2@u32)
                keep2(w@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
            """,
        ["sTupElemBad"] = """
            func keepT(slot: raw/(raw/(raw/u8), u64), value: raw/u8) => ()
            func sTupElemBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: (u64, u64) = (3@u64, 4@u64)
                keepT(w@raw@raw/(raw/(raw/u8), u64), spare)
                kk = 2
            """,
        ["sArrElemBad"] = """
            func keepA(slot: raw/[2 of raw/(raw/u8)], value: raw/u8) => ()
            func sArrElemBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: (u64, u64) = (3@u64, 4@u64)
                keepA(w@raw@raw/[2 of raw/(raw/u8)], spare)
                kk = 2
            """,
        ["sPtrPartOk"] = """
            func keep2u(slot: raw/(raw/(raw/u16)), value: raw/u8) => ()
            func sPtrPartOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var w: (raw/(raw/u8), u64) = (other@raw, 3@u64)
                keep2u(w.0@raw@raw/(raw/(raw/u16)), spare)
                kk = 2
            """,
        ["sPtrPartBad"] = """
            func keep2u(slot: raw/(raw/(raw/u16)), value: raw/u8) => ()
            func sPtrPartBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var w: (raw/(raw/u8), u64) = (other@raw, 3@u64)
                keep2u(w.0@raw@raw/(raw/(raw/u16)), spare)
                kk = 2
                usep(other)
            """,
        ["qTwoHopOk"] = """
            func stash4(slot: raw/(raw/(raw/(raw/u8))), value: raw/u8) => ()
            func qTwoHopOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var hop2: raw/u8 = v@raw@raw/u8
                var hop1: raw/u8 = hop2@raw@raw/u8
                stash4(hop1@raw@raw/(raw/(raw/(raw/u8))), spare)
                kk = 2
            """,
        ["fPunChainBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func fPunChainBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var w: u64 = 3
                let q = w@raw@raw/(raw/(raw/u8))
                unsafe => *q = other@raw
                keep2(q, spare)
                kk = 2
                usep(other)
            """,
        ["qPastHopU64Bad"] = """
            func stash3(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func qPastHopU64Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var hop: u64 = 0
                let hp = hop@raw@raw/(raw/u8)
                unsafe => *hp = v@raw@raw/u8
                stash3(hop@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
                let z = v
            """,
        ["qTupScalarReadOk"] = """
            func flushT(w: uniq/(raw/(raw/u8), raw/u8), value: raw/u8) => ()
            func qTupScalarReadOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var slot: raw/u8 = null
                var v: u8 = 0
                var x: (raw/(raw/u8), raw/u8) = (slot@raw, v@raw)
                flushT(x@uniq, spare)
                kk = 2
                let z = v
            """,
        ["ePastScalarOk"] = """
            func flushU(w: uniq/(raw/u8), value: raw/u8) => ()
            func ePastScalarOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u8 = 0
                var x: raw/u8 = v@raw
                flushU(x@uniq, spare)
                kk = 2
                let z = v
            """,
        ["ePastScalarU64Ok"] = """
            func flushU(w: uniq/(raw/u8), value: raw/u8) => ()
            func ePastScalarU64Ok()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var x: raw/u8 = v@raw@raw/u8
                flushU(x@uniq, spare)
                kk = 2
                let z = v
            """,
        ["ePastScalarBad"] = """
            func flushU(w: uniq/(raw/u8), value: raw/u8) => ()
            func ePastScalarBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u8 = 0
                var x: raw/u8 = v@raw
                flushU(x@uniq, spare)
                kk = 2
                usep(x)
            """,
        ["ePastTupScalarOk"] = """
            func flushT2(w: uniq/(raw/u8, raw/u8), value: raw/u8) => ()
            func ePastTupScalarOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: u8 = 0
                var v: u8 = 0
                var x: (raw/u8, raw/u8) = (a@raw, v@raw)
                flushT2(x@uniq, spare)
                kk = 2
                let z = v
            """,
        ["ePastTupScalarBad"] = """
            func flushT2(w: uniq/(raw/u8, raw/u8), value: raw/u8) => ()
            func ePastTupScalarBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: u8 = 0
                var v: u8 = 0
                var x: (raw/u8, raw/u8) = (a@raw, v@raw)
                flushT2(x@uniq, spare)
                kk = 2
                usep(x.0)
            """,
        ["ePutScalarOk"] = """
            func put8(p: raw/u8, v: raw/u8) => ()
            func ePutScalarOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                put8(w@raw@raw/u8, spare)
                kk = 2
                let z = w
            """,
        ["ePutScalarHOk"] = """
            func put8(p: raw/u8, v: raw/u8) => ()
            func ePutScalarHOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: raw/u8 = null
                var w: (raw/u8, u64) = (other, 3@u64)
                put8(w.1@raw@raw/u8, spare)
                kk = 2
                let z = w
            """,
        ["dW2Bad"] = """
            struct W2
                public var p: raw/(raw/u8)
                public var q: raw/u8
                public init(q: raw/u8)
                    self.p = null
                    self.q = q
                public func flush(self: uniq/Self, value: raw/u8) => unsafe => *self.p = value
            func dW2Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u8 = 0
                var x = W2.init(null)
                x.p = v@raw@raw/(raw/u8)
                x.flush(spare)
                kk = 2
                let z = v
            """,
        ["dCellBad"] = """
            struct Cell
                public var p: raw/(raw/u8)
                public init(p: raw/(raw/u8)) => self.p = p
                public func put(self: uniq/Self, value: raw/u8) => unsafe => *self.p = value
            func dCellBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var v: u64 = 0
                var c = Cell.init(v@raw@raw/(raw/u8))
                c.put(spare)
                kk = 2
                let z = v
            """,
    };

    // The coverage table's rows (a5v-u6-state.md), by exposure.
    [Theory]
    [InlineData("fScalarLetPtrOk", "")] // V, an address of a `let` local (fail-before: the untracked rule sent the store to E)
    [InlineData("fScalarVarPtrOk", "")] // V, the same of a `var` local (fail-before)
    [InlineData("fScalarUsedAfterBad", "fScalarUsedAfterBad: kk = 2")] // I, w carries r's Loan and is read after
    [InlineData("fScalarPtrUsedAfterBad", "fScalarPtrUsedAfterBad: kk = 2")] // I, the pointer addressing w is used after (15.6: a use of w)
    [InlineData("eStorageEndOk", "")] // V, w declared before kk: its storage end observes nothing (15.6.6; fail-before)
    [InlineData("eStorageEndBad", "eStorageEndBad: kk = 2")] // I, as eStorageEndOk, w read after
    [InlineData("fElemPtrNoSliceOk", "")] // V, an element's address names the root's holder (fail-before)
    [InlineData("fElemUsedAfterBad", "fElemUsedAfterBad: kk = 2")] // I, another element read after: the whole holder carries the Loan
    [InlineData("fWholePtrLetOk", "")] // V, a whole array's address (fail-before)
    [InlineData("fWholeUsedAfterBad", "fWholeUsedAfterBad: kk = 2")] // I
    [InlineData("fTupleScalarPtrOk", "")] // V, a Tuple of Scalars (fail-before)
    [InlineData("fTupleUsedAfterBad", "fTupleUsedAfterBad: kk = 2")] // I
    [InlineData("fStructScalarPtrOk", "")] // V, a struct of Scalar Fields (fail-before)
    [InlineData("fFieldPtrOk", "")] // V, a Field's address names the struct's holder (fail-before)
    [InlineData("fStructUsedAfterBad", "fStructUsedAfterBad: kk = 2")] // I, another Field read after
    [InlineData("fUniqScalarRawOk", "")] // V, a raw pointer formed through an exclusive borrow carries its Loan (15.6 Addresses; fail-before)
    [InlineData("fUniqScalarRawBad", "fUniqScalarRawBad: kk = 2")] // I
    [InlineData("qElemUniqOk", "")] // V, an exclusive borrow of an element, stored in a local (fail-before)
    [InlineData("qElemUniqBad", "qElemUniqBad: kk = 2")] // I
    [InlineData("eUniqFieldOk", "")] // V, an exclusive borrow of a Field, stored (fail-before)
    [InlineData("eUniqFieldBad", "eUniqFieldBad: kk = 2")] // I
    [InlineData("eSharedOnlyOk", "")] // V, a shared borrow only: no exposed holder, the call writes nothing past it (15.6.4, 5.2.1 condition 3)
    [InlineData("kCalleeConvOk", "")] // V, a call result tied to an exclusive argument (fail-before: the untracked rule's call-result arm)
    [InlineData("kCalleeConvBad", "kCalleeConvBad: kk = 2")] // I, a pointer read from w's u64 bytes reaches storage the body does not own (15.6 Holders)
    [InlineData("fSliceKeepDirectOk", "")] // V, a range selection's storage address (fail-before)
    [InlineData("fSliceUsedAfterBad", "fSliceUsedAfterBad: kk = 2")] // I
    [InlineData("aSliceTupleNoWriteOk", "")] // V, an always-failing position is a Warning (4.7.4; fail-before)
    [InlineData("qPunLoanOk", "")] // V, Raw.initialize over a punned Place of a Loan-free local, unused after (fail-before)
    [InlineData("uHandleBytesOk", "")] // V, a handle local's bytes read at a row storing only a u8 (fail-before)
    [InlineData("uHandleBytesBad", "uHandleBytesBad: kk = 2")] // I, a stepping row over a handle's bytes (PLAN G785)
    [InlineData("qIntChainNarrowOk", "")] // V, the row stores only a u8 (fail-before)
    [InlineData("sBufOk", "")] // V, a byte buffer read at raw/u8: only a u8 past it (fail-before)
    [InlineData("sPairOk", "")] // V, as sBufOk over (u32, u32) (fail-before)
    [InlineData("sTupElemOk", "")] // V, a punned Tuple's element raw/u8 (fail-before)
    [InlineData("sArrElemOk", "")] // V, a punned array's elements raw/u8 (fail-before)
    [InlineData("sBufBad", "sBufBad: kk = 2")] // I, a stepping row over owner Scalar bytes reaches storage the body does not own (15.6 Holders)
    [InlineData("sPairBad", "sPairBad: kk = 2")] // I
    [InlineData("sTupElemBad", "sTupElemBad: kk = 2")] // I
    [InlineData("sArrElemBad", "sArrElemBad: kk = 2")] // I
    [InlineData("sPtrPartOk", "sPtrPartOk: kk = 2")] // I by P3 (15.6.4's composition, twoLayers)
    [InlineData("sPtrPartBad", "sPtrPartBad: kk = 2")] // I
    [InlineData("qTwoHopOk", "qTwoHopOk: kk = 2")] // I by P3
    [InlineData("fPunChainBad", "fPunChainBad: kk = 2")] // I, a pointer punned over w's bytes read on
    [InlineData("qPastHopU64Bad", "qPastHopU64Bad: kk = 2")] // I
    [InlineData("qTupScalarReadOk", "qTupScalarReadOk: kk = 2")] // I, x.0 may hold the call's own pointer (15.6.4's composition; filed V before O-230)
    [InlineData("ePastScalarOk", "")] // V, past a raw/u8 layer only a u8 is stored (5.7 Calls)
    [InlineData("ePastScalarU64Ok", "")] // V, as ePastScalarOk over a u64
    [InlineData("ePastScalarBad", "ePastScalarBad: kk = 2")] // I, x used after
    [InlineData("ePastTupScalarOk", "")] // V, past a Tuple of raw/u8 layers only u8s are stored
    [InlineData("ePastTupScalarBad", "ePastTupScalarBad: kk = 2")] // I, x used after
    [InlineData("ePutScalarOk", "")] // V, a parameter's raw/u8 admits no Loan (5.7 Calls)
    [InlineData("ePutScalarHOk", "")] // V, as ePutScalarOk into a Loan-holding holder
    [InlineData("dW2Bad", "dW2Bad: kk = 2")] // I, a declaration's own layer addressing a Loan-free local (the declared step)
    [InlineData("dCellBad", "dCellBad: kk = 2")] // I, as dW2Bad over a u64
    public void AnExposedLocalIsAHolderWhateverItsType(string form, string expected)
    {
        var c = MirBuildTest.Compile(ExposedShapes + ExposedForms[form] + "\npublic func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes))}]");
    }
}
