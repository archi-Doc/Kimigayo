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
/// were rejected only by R4-M120's untracked rule, which sent the store to storage the body does not own. A local a store gives a value
/// read from a holder is one too, whatever its Type (15.6 Holders: "A value read, Copied or Moved from a part carries every Loan the
/// holder carries"; rule H, MirBorrows.Holders' Flowing).</summary>
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
        ["kCalleeConvUsedAfterBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func view1(p: uniq/u64) -> raw/(raw/u8) during p => p@follow@raw@raw/(raw/u8)
            func kCalleeConvUsedAfterBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let q = view1(w@uniq)
                keep(q, spare)
                kk = 2
                usep(q)
            """,
        ["kCalleeConvReadBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func view1(p: uniq/u64) -> raw/(raw/u8) during p => p@follow@raw@raw/(raw/u8)
            func kCalleeConvReadBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                let q = view1(w@uniq)
                keep(q, spare)
                kk = 2
                let z = w
            """,
        ["kCalleeRefOk"] = """
            func pick(p: uniq/u64) -> uniq/u64 during p => p
            func kCalleeRefOk()
                var w: u64 = 3
                let q = pick(w@uniq)
                let v = q@follow
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

    private static readonly Dictionary<string, string> FlowForms = new()
    {
        ["cpyOutBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyOutBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = w
                kk = 2
                let y = z
            """,
        ["cpyOutOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyOutOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = w
                kk = 2
            """,
        ["cpyPunBad2"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func get(slot: raw/(raw/u8)) -> raw/u8
                unsafe => return *slot
            func put8(p: raw/u8, v: raw/u8) => ()
            func cpyPunBad2()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = w
                var w2: u64 = z
                let q = get(w2@raw@raw/(raw/u8))
                kk = 2
                put8(q, null)
            """,
        ["cpyPunOk2"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func get(slot: raw/(raw/u8)) -> raw/u8
                unsafe => return *slot
            func put8(p: raw/u8, v: raw/u8) => ()
            func cpyPunOk2()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = w
                var w2: u64 = z
                let q = get(w2@raw@raw/(raw/u8))
                put8(q, null)
                kk = 2
            """,
        ["cpyPunDirectBad2"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func get(slot: raw/(raw/u8)) -> raw/u8
                unsafe => return *slot
            func put8(p: raw/u8, v: raw/u8) => ()
            func cpyPunDirectBad2()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var w2: u64 = w
                let q = get(w2@raw@raw/(raw/u8))
                kk = 2
                put8(q, null)
            """,
        ["cpyHolderBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyHolderBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z: (u64, raw/u8) = (w, null)
                kk = 2
                let y = z.0
            """,
        ["cpyTupOutBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyTupOutBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var t: (u64, raw/u8) = (3@u64, null)
                keep(t.0@raw@raw/(raw/u8), spare)
                let z = t.0
                kk = 2
                let y = z
            """,
        ["cpyTupOutOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyTupOutOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var t: (u64, raw/u8) = (3@u64, null)
                keep(t.0@raw@raw/(raw/u8), spare)
                let z = t.0
                kk = 2
            """,
        ["cpyTupPunBad2"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func get(slot: raw/(raw/u8)) -> raw/u8
                unsafe => return *slot
            func put8(p: raw/u8, v: raw/u8) => ()
            func cpyTupPunBad2()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var t: (u64, raw/u8) = (3@u64, null)
                keep(t.0@raw@raw/(raw/u8), spare)
                let z = t.0
                var w2: u64 = z
                let q = get(w2@raw@raw/(raw/u8))
                kk = 2
                put8(q, null)
            """,
        ["cpyTupAggBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyTupAggBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let s = (w, 1@u64)
                kk = 2
                let y = s.0
            """,
        ["cpyTupAggOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyTupAggOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let s = (w, 1@u64)
                kk = 2
            """,
        ["loopCpyBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func loopCpyBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: u64 = 0
                var b: u64 = 0
                var i: i32 = 0
                while i < 2
                    a = b
                    b = w
                    i = i + 1
                kk = 2
                let y = a
            """,
        ["loopCpyOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func loopCpyOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: u64 = 0
                var b: u64 = 0
                var i: i32 = 0
                while i < 2
                    a = b
                    b = w
                    i = i + 1
                kk = 2
            """,
        ["capCpyBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func capCpyBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let f = func [w] () -> u64 => w
                kk = 2
                let y = f()
            """,
        ["capCpyOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func capCpyOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let f = func [w] () -> u64 => w
                kk = 2
            """,
        ["argUseBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func use64(v: u64) => ()
            func argUseBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = w
                kk = 2
                use64(z)
            """,
        ["arithOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func arithOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = w + 0
                kk = 2
                let y = z
            """,
        ["cpyResultBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func id64(a: u64) -> u64 => a
            func cpyResultBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = id64(w)
                kk = 2
                let y = z
            """,
        ["cpyResultOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func id64(a: u64) -> u64 => a
            func cpyResultOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = id64(w)
                kk = 2
            """,
        ["cpyFieldBad"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init(a: u64)
                    self.a = a
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyFieldBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let s = Pt.init(a: w)
                kk = 2
                let y = s.a
            """,
        ["cpyFieldOk"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init(a: u64)
                    self.a = a
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyFieldOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let s = Pt.init(a: w)
                kk = 2
            """,
        ["narrowResultOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func lo(a: u64) -> u8 => 1
            func narrowResultOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = lo(w)
                kk = 2
                let y = z
            """,
        ["intFirstBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func put64(slot: raw/u64, v: u64) => ()
            func intFirstBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                put64(d@raw, w0)
                kk = 2
                let y = d
            """,
        ["intFirstPunBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func put64(slot: raw/u64, v: u64) => ()
            func get(slot: raw/(raw/u8)) -> raw/u8
                unsafe => return *slot
            func put8(p: raw/u8, v: raw/u8) => ()
            func intFirstPunBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                put64(d@raw, w0)
                let q = get(d@raw@raw/(raw/u8))
                kk = 2
                put8(q, null)
            """,
        ["intFirstPunOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func put64(slot: raw/u64, v: u64) => ()
            func get(slot: raw/(raw/u8)) -> raw/u8
                unsafe => return *slot
            func put8(p: raw/u8, v: raw/u8) => ()
            func intFirstPunOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                put64(d@raw, w0)
                let q = get(d@raw@raw/(raw/u8))
                put8(q, null)
                kk = 2
            """,
        ["storeCopyBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func set(x: uniq/u64, v: u64) => x@follow = v
            func storeCopyBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var a: u64 = 0
                set(a@uniq, w0)
                kk = 2
                let y = a
            """,
        ["storeCopyOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func set(x: uniq/u64, v: u64) => x@follow = v
            func storeCopyOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var a: u64 = 0
                set(a@uniq, w0)
                kk = 2
            """,
        ["storeSafeOk"] = """
            func set(x: uniq/i32, v: i32) => x@follow = v
            func storeSafeOk()
                var kk: u8 = 1
                let r = kk@uniq
                var a: i32 = 0
                set(a@uniq, 5)
                kk = 2
                let y = a
            """,
        ["intPastBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func put64p(w: uniq/(raw/u64), v: u64) => ()
            func intPastBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                var x: raw/u64 = d@raw
                put64p(x@uniq, w0)
                kk = 2
                let y = d
            """,
        ["intPastPunBad2"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func put64p(w: uniq/(raw/u64), v: u64) => ()
            func get(slot: raw/(raw/u8)) -> raw/u8
                unsafe => return *slot
            func put8(p: raw/u8, v: raw/u8) => ()
            func intPastPunBad2()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                var x: raw/u64 = d@raw
                put64p(x@uniq, w0)
                let q = get(d@raw@raw/(raw/u8))
                kk = 2
                put8(q, null)
            """,
        ["intPastOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func put64p(w: uniq/(raw/u64), v: u64) => ()
            func intPastOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                var x: raw/u64 = d@raw
                put64p(x@uniq, w0)
                kk = 2
            """,
        ["ownerQBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func id64(a: u64) -> u64 => a
            func put64(slot: raw/u64, v: u64) => ()
            func ownerQBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = id64(w)
                var d: u64 = 0
                put64(d@raw, w)
                kk = 2
                let y = z + d
            """,
        ["ownerQOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func id64(a: u64) -> u64 => a
            func put64(slot: raw/u64, v: u64) => ()
            func ownerQOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                let z = id64(w)
                var d: u64 = 0
                put64(d@raw, w)
                let y = z + d
                kk = 2
            """,
        ["intPastScalarOk"] = """
            func put64p(w: uniq/(raw/u64), v: u64) => ()
            func intPastScalarOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                var d: u64 = 0
                var x: raw/u64 = d@raw
                put64p(x@uniq, w0)
                kk = 2
                let y = d
            """,
        ["cpyNoHolderOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyNoHolderOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var n: u64 = 4
                let z = n
                kk = 2
                let y = z
            """,
        ["resultPlainOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func id64(a: u64) -> u64 => a
            func resultPlainOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var n: u64 = 4
                let z = id64(n)
                kk = 2
                let y = z
            """,
        ["sharedSrcOk"] = """
            func peek(p: ref/u64) => ()
            func sharedSrcOk()
                var kk: u8 = 1
                let r = kk@uniq
                var w: u64 = 3
                peek(w@ref)
                let z = w
                kk = 2
                let y = z
            """,
        ["cpyReplOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func cpyReplOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var z: u64 = w
                z = 5
                kk = 2
                let y = z
            """,
        ["replOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func replOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                w = 5
                kk = 2
                let y = w
            """,
        ["partWriteBad"] = """
            struct Pt
                public var a: u64
                public var b: u64
                public init()
                    self.a = 1
                    self.b = 2
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func partWriteBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s = Pt.init()
                keep(s.a@raw@raw/(raw/u8), spare)
                s.a = 5
                kk = 2
                let y = s.b
            """,
        ["rvPastU64HolderBad"] = """
            func put64p(w: uniq/(raw/u64), v: u64) => ()
            func rvPastU64HolderBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var t: (u64, raw/u8) = (3@u64, spare)
                var d: u64 = 0
                var x: raw/u64 = d@raw
                put64p(x@uniq, t.0)
                kk = 2
            """,
        ["rvPastU8OwnBad"] = """
            func setb(w: uniq/(raw/u8), v: u8) => ()
            func rvPastU8OwnBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let t: (u8, raw/u8) = (1@u8, spare)
                var b: u8 = 0
                var x: raw/u8 = b@raw
                setb(x@uniq, t.0)
                kk = 2
            """,
        ["rvPastU8Ok"] = """
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func rvPastU8Ok(y: uniq/i32)
                let w = y@follow@ref
                var b: u8 = 0
                var cell: raw/u8 = b@raw
                keep1(cell@raw, w@raw@raw/u8)
                y@follow = 5
                let k = b
            """,
        ["rvPastU8UniqOk"] = """
            func setp(w: uniq/(raw/u8), v: raw/u8) => ()
            func rvPastU8UniqOk(y: uniq/i32)
                let t = y@follow@ref
                var b: u8 = 0
                var x: raw/u8 = b@raw
                setp(x@uniq, t@raw@raw/u8)
                y@follow = 5
                let k = b
            """,
        ["rvPastU8UniqBad"] = """
            func setp(w: uniq/(raw/u8), v: raw/u8) => ()
            func rvPastU8UniqBad(y: uniq/i32)
                let t = y@follow@ref
                var b: u8 = 0
                var x: raw/u8 = b@raw
                setp(x@uniq, t@raw@raw/u8)
                y@follow = 5
                let k = x
            """,
        ["rvCompRawBad"] = """
            func put8p(w: uniq/(raw/(raw/u8)), v: raw/u8) => ()
            func rvCompRawBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var d: raw/u8 = null
                var x: raw/(raw/u8) = d@raw
                put8p(x@uniq, spare)
                kk = 2
            """,
        ["rvCompTupBad"] = """
            func putT(w: uniq/(raw/(u64, raw/u8)), v: (u64, raw/u8)) => ()
            func rvCompTupBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var d: (u64, raw/u8) = (0@u64, null)
                var x: raw/(u64, raw/u8) = d@raw
                putT(x@uniq, (1@u64, spare))
                kk = 2
            """,
        ["rvCompRawOk"] = """
            func put8p(w: uniq/(raw/(raw/u8)), v: raw/u8) => ()
            func rvCompRawOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var d: raw/u8 = null
                var x: raw/(raw/u8) = d@raw
                put8p(x@uniq, null)
                kk = 2
            """,
        ["rvTupPartSafeBad"] = """
            func rvTupPartSafeBad() -> i32
                var a: i32 = 1
                let t = (a@ref, 3)
                let n = t.1
                a = 2
                return n
            """,
        ["rvTupPartSafeOk"] = """
            func rvTupPartSafeOk() -> i32
                var a: i32 = 1
                let t = (a@ref, 3)
                let n = t.1
                let m = n + 1
                a = 2
                return m
            """,
        ["rvFieldSafeBad"] = """
            struct RS {s}
                public let r: ref/i32 during s
                public let n: i32
                public init(r: ref/i32 during s, n: i32)
                    self.r = r
                    self.n = n
            func rvFieldSafeBad() -> i32
                var a: i32 = 1
                let s = RS.init(r: a@ref, n: 3)
                let n = s.n
                a = 2
                return n
            """,
        ["rvFieldSafeOk"] = """
            struct RS {s}
                public let r: ref/i32 during s
                public let n: i32
                public init(r: ref/i32 during s, n: i32)
                    self.r = r
                    self.n = n
            func rvFieldSafeOk() -> i32
                var a: i32 = 1
                let s = RS.init(r: a@ref, n: 3)
                let n = s.n
                let m = n + 1
                a = 2
                return m
            """,
        ["rvReborrowRetBad"] = """
            struct KI
                public var v: i32 = 0
                public init() => ()
                public func keep(self: uniq/Self) -> uniq/KI during self => self
            func rvReborrowRetBad() -> i32
                var x = KI.init()
                var slot = x@uniq
                var rr = slot@uniq
                let res = rr@follow@follow.keep()
                return res.v
            """,
        ["rvReborrowUseBad"] = """
            struct KI
                public var v: i32 = 0
                public init() => ()
                public func keep(self: uniq/Self) -> uniq/KI during self => self
            func rvReborrowUseBad()
                var x = KI.init()
                var slot = x@uniq
                var rr = slot@uniq
                let res = rr@follow@follow.keep()
                let n = res.v
                x.v = 5
                let y = n
            """,
        ["rvReborrowUseOk"] = """
            struct KI
                public var v: i32 = 0
                public init() => ()
                public func keep(self: uniq/Self) -> uniq/KI during self => self
            func rvReborrowUseOk()
                var x = KI.init()
                var slot = x@uniq
                var rr = slot@uniq
                let res = rr@follow@follow.keep()
                let n = res.v
                let y = n
                x.v = 5
            """,
        ["rvDerefPlainOk"] = """
            struct KI
                public var v: i32 = 0
                public init() => ()
            func rvDerefPlainOk()
                var x = KI.init()
                let r = x@uniq
                let n = r@follow.v
                x.v = 5
                let y = n
            """,
        ["rvCopyUseBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func rvCopyUseBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                d = w0
                kk = 2
                let y = d
            """,
        ["rvPutROk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func putR(slot: raw/u64, v: u64, f: ref/i32) => ()
            func rvPutROk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                var i: i32 = 0
                putR(d@raw, w0, i@ref)
                kk = 2
            """,
        ["rvPutRNoKeepOk"] = """
            func putR(slot: raw/u64, v: u64, f: ref/i32) => ()
            func rvPutRNoKeepOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                var d: u64 = 0
                var i: i32 = 0
                putR(d@raw, w0, i@ref)
                kk = 2
                let y = d
            """,
        ["rvPutTROk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func putTR<T>(slot: raw/T, v: T, f: ref/i32) => ()
            func rvPutTROk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var d: u64 = 0
                var i: i32 = 0
                putTR(d@raw, w0, i@ref)
                kk = 2
            """,
        ["rvPickSpillOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func pickR<T>(a: T, b: ref/i32) -> T => a@move
            func rvPickSpillOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                var e: u64 = 0
                let pe = e@raw
                var i: i32 = 0
                e = pickR(w0, i@ref)
                kk = 2
            """,
        ["tryErrLocal"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func tryErrLocal() -> Result<(), Full>
                var a: i32 = 0
                let w = try reserve(a@uniq)
                w@follow = 1
                return .Ok(())
            """,
        ["tryErrMatchLocal"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func tryErrMatchLocal() -> Result<(), Full>
                var a: i32 = 0
                match reserve(a@uniq)@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["tryErrLetLocal"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func tryErrLetLocal() -> Result<(), Full>
                var a: i32 = 0
                let r = reserve(a@uniq)
                match r@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["tryErrExposedE"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func touch(e: uniq/Full) => ()
            func tryErrExposedE() -> Result<(), Full>
                var a: i32 = 0
                match reserve(a@uniq)@move
                    .Ok(let w) => w@follow = 1
                    .Err(var e)
                        touch(e@uniq)
                        return .Err(e@move)
                return .Ok(())
            """,
        ["tryErrParamOk"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func tryErrParamOk(a: uniq/i32) -> Result<(), Full>
                let w = try reserve(a)
                w@follow = 1
                return .Ok(())
            """,
        ["tryErrFreshOk"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func tryErrFreshOk() -> Result<(), Full>
                var a: i32 = 0
                match reserve(a@uniq)@move
                    .Ok(let w) => w@follow = 1
                    .Err(_) => return .Err(Full.init())
                return .Ok(())
            """,
        ["caseHoldsUseBad"] = """
            func lend(x: uniq/i32) -> Result<(), uniq/i32> => .Err(x)
            func caseHoldsUseBad() -> i32
                var a: i32 = 0
                match lend(a@uniq)@move
                    .Ok(_) => return 0
                    .Err(let e)
                        a = 2
                        e@follow = 3
                        return 1
            """,
        ["caseHoldsUseOk"] = """
            func lend(x: uniq/i32) -> Result<(), uniq/i32> => .Err(x)
            func caseHoldsUseOk() -> i32
                var a: i32 = 0
                match lend(a@uniq)@move
                    .Ok(_) => return 0
                    .Err(let e)
                        e@follow = 3
                        a = 2
                        return 1
            """,
        ["casePartHoldsBad"] = """
            func lend2(x: uniq/i32) -> Result<(), (uniq/i32, i32)> => .Err((x, 3))
            func casePartHoldsBad() -> i32
                var a: i32 = 0
                match lend2(a@uniq)@move
                    .Ok(_) => return 0
                    .Err(let e)
                        let n = e.1
                        a = 2
                        return n
            """,
        ["caseBuiltLocal"] = """
            struct Full
                public init() => ()
            func caseBuiltLocal(c: bool) -> Result<(), Full>
                var a: i32 = 0
                var r: Result<uniq/i32, Full> = .Err(Full.init())
                if c => r = .Ok(a@uniq)
                match r@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["callFieldSafeBad"] = """
            struct RS {s}
                public let r: ref/i32 during s
                public let n: i32
                public init(r: ref/i32 during s, n: i32)
                    self.r = r
                    self.n = n
            func mk(r: ref/i32) -> RS => RS.init(r: r, n: 3)
            func callFieldSafeBad() -> i32
                var a: i32 = 1
                let s = mk(a@ref)
                let n = s.n
                a = 2
                return n
            """,
        ["tryErrRefRead"] = """
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func tryErrRefRead() -> i32
                var a: i32 = 0
                let r = count(a@uniq)
                match r
                    .Ok(_) => return 0
                    .Err(let e) => return e@follow
            """,
        ["caseOwnValueBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32, v: u64) -> Result<uniq/i32, u64> => .Err(v)
            func caseOwnValueBad() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                match mk(a@uniq, w)@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["rvCaseNestedTryOk"] = """
            struct Full
                public init() => ()
            func reserve2(x: uniq/i32) -> Result<Result<uniq/i32, Full>, Full> => .Ok(.Ok(x))
            func rvCaseNestedTryOk() -> Result<(), Full>
                var a: i32 = 0
                let inner = try reserve2(a@uniq)
                let w = try inner@move
                w@follow = 1
                return .Ok(())
            """,
        ["rvCaseOptionOk"] = """
            struct Full
                public init() => ()
            func next1(x: uniq/i32) -> Option<Result<uniq/i32, Full>> => .Some(.Ok(x))
            func rvCaseOptionOk() -> Result<(), Full>
                var a: i32 = 0
                match next1(a@uniq)@move
                    .Some(let item)
                        let w = try item@move
                        w@follow = 1
                    .None => return .Ok(())
                return .Ok(())
            """,
        ["rvCaseNestedOk"] = """
            struct Full
                public init() => ()
            func reserve2(x: uniq/i32) -> Result<Result<uniq/i32, Full>, Full> => .Ok(.Ok(x))
            func rvCaseNestedOk() -> Result<(), Full>
                var a: i32 = 0
                match reserve2(a@uniq)@move
                    .Ok(let inner)
                        match inner@move
                            .Ok(let w) => w@follow = 1
                            .Err(let e) => return .Err(e@move)
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCaseNestedHoldsBad"] = """
            struct Full
                public init() => ()
            func reserve2(x: uniq/i32) -> Result<Result<Full, uniq/i32>, Full> => .Ok(.Err(x))
            func rvCaseNestedHoldsBad() -> i32
                var a: i32 = 0
                match reserve2(a@uniq)@move
                    .Ok(let inner)
                        match inner@move
                            .Ok(_) => return 0
                            .Err(let e)
                                a = 2
                                e@follow = 3
                                return 1
                    .Err(_) => return 0
                return 0
            """,
        ["rvCaseNestedBuiltBad"] = """
            struct Full
                public init() => ()
            func reserve2(x: uniq/i32) -> Result<Result<uniq/i32, Full>, Full> => .Ok(.Ok(x))
            func rvCaseNestedBuiltBad(c: bool) -> Result<(), Full>
                var a: i32 = 0
                match reserve2(a@uniq)@move
                    .Ok(var inner)
                        if c => inner = .Err(Full.init())
                        match inner@move
                            .Ok(let w) => w@follow = 1
                            .Err(let e) => return .Err(e@move)
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCaseFieldResBad"] = """
            struct Full
                public init() => ()
            struct Wrap {s}
                public var res: Result<uniq/i32 during s, Full>
                public init(res: Result<uniq/i32 during s, Full>) => self.res = res@move
            func wrap(x: uniq/i32) -> Wrap => Wrap.init(res: .Ok(x))
            func rvCaseFieldResBad() -> Result<(), Full>
                var a: i32 = 0
                var s = wrap(a@uniq)
                match s.res@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCasePayloadFieldBad"] = """
            struct Full
                public init() => ()
            struct Wrap {s}
                public var res: Result<uniq/i32 during s, Full>
                public init(res: Result<uniq/i32 during s, Full>) => self.res = res@move
            func wrap2(x: uniq/i32) -> Result<Wrap, Full> => .Ok(Wrap.init(res: .Ok(x)))
            func rvCasePayloadFieldBad() -> Result<(), Full>
                var a: i32 = 0
                match wrap2(a@uniq)@move
                    .Ok(var s)
                        match s.res@move
                            .Ok(let w) => w@follow = 1
                            .Err(let e) => return .Err(e@move)
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCaseUniqOk"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func touchR(r: uniq/Result<uniq/i32 during s, Full>) => ()
            func rvCaseUniqOk() -> Result<(), Full>
                var a: i32 = 0
                var r = reserve(a@uniq)
                touchR(r@uniq)
                match r@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCaseRawOk"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func rvCaseRawOk() -> Result<(), Full>
                var a: i32 = 0
                var r = reserve(a@uniq)
                let p = r@raw
                match r@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCaseRefCodeOk"] = """
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func rvCaseRefCodeOk() -> i32
                var a: i32 = 0
                let r = count(a@uniq)
                match r
                    .Ok(_) => return 0
                    .Err(let code) => return code@follow
            """,
        ["rvCaseRefHoldsBad"] = """
            func lend2(x: uniq/i32) -> Result<(), (uniq/i32, i32)> => .Err((x, 3))
            func rvCaseRefHoldsBad() -> i32
                var a: i32 = 0
                let r = lend2(a@uniq)
                match r
                    .Ok(_) => return 0
                    .Err(let e)
                        let n = e@follow.1
                        a = 2
                        return n
            """,
        ["rvCaseRefBuiltBad"] = """
            func rvCaseRefBuiltBad(c: bool) -> i32
                var a: i32 = 0
                var r: Result<uniq/i32, i32> = .Err(3)
                if c => r = .Ok(a@uniq)
                match r
                    .Ok(_) => return 0
                    .Err(let code) => return code@follow
            """,
        ["rvCaseAssignBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func rvCaseAssignBad(a: uniq/i32, c: bool) -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var res = mk(a)
                if c => res = .Err(w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["rvCaseRefWriteBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func rvCaseRefWriteBad(a: uniq/i32) -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var res = mk(a)
                let rr = res@uniq
                rr@follow = .Err(w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["rvCaseLoopOk"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func rvCaseLoopOk(c: bool) -> Result<(), Full>
                var a: i32 = 0
                var b: i32 = 0
                var r = reserve(a@uniq)
                if c => r = reserve(b@uniq)
                match r@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCaseSharedOk"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func look(r: ref/Result<uniq/i32 during s, Full>) => ()
            func rvCaseSharedOk() -> Result<(), Full>
                var a: i32 = 0
                var r = reserve(a@uniq)
                look(r@ref)
                match r@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvMatchBuiltBad"] = """
            func rvMatchBuiltBad(c: bool) -> i32
                var a: i32 = 1
                var r: Result<ref/i32, i32> = .Err(3)
                if c => r = .Ok(a@ref)
                match r@move
                    .Ok(_) => return 0
                    .Err(let n)
                        a = 2
                        return n
            """,
        ["rvGenCaseBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mkT<T>(x: uniq/i32, v: T) -> Result<uniq/i32, T> => .Err(v@move)
            func rvGenCaseBad(a: uniq/i32) -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w0: u64 = 3
                keep(w0@raw@raw/(raw/u8), spare)
                match mkT(a, w0)@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["rvCaseKeptOk"] = """
            struct Full
                public init() => ()
            func reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)
            func touchR(r: uniq/Result<uniq/i32 during s, Full>) => ()
            func rvCaseKeptOk() -> Result<(), Full>
                var a: i32 = 0
                var r = reserve(a@uniq)
                let rr = r@uniq
                touchR(rr)
                match r@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCasePairOk"] = """
            struct Full
                public init() => ()
            enum Two {s}
                Both(Result<uniq/i32 during s, Full>, i32)
                Neither
            func both(x: uniq/i32) -> Two => .Both(.Ok(x), 3)
            func rvCasePairOk() -> Result<(), Full>
                var a: i32 = 0
                match both(a@uniq)@move
                    .Both(let item, let n)
                        let w = try item@move
                        w@follow = n
                    .Neither => return .Ok(())
                return .Ok(())
            """,
        ["rvCasePairPartBad"] = """
            struct Full
                public init() => ()
            enum Two {s}
                Both(Result<uniq/i32 during s, Full>, i32)
                Neither
            func both(x: uniq/i32) -> Two => .Both(.Ok(x), 3)
            func rvCasePairPartBad() -> i32
                var a: i32 = 0
                match both(a@uniq)@move
                    .Both(let item, let n)
                        a = 2
                        return n
                    .Neither => return 0
            """,
        ["rvCaseRefNestedOk"] = """
            func count2(x: uniq/i32) -> Result<Result<uniq/i32, i32>, i32> => .Ok(.Ok(x))
            func rvCaseRefNestedOk() -> i32
                var a: i32 = 0
                let r = count2(a@uniq)
                match r
                    .Ok(let inner)
                        match inner@follow
                            .Ok(_) => return 0
                            .Err(let code) => return code@follow
                    .Err(let code) => return code@follow
            """,
        ["rvCaseRefUniqSubjectBad"] = """
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func rvCaseRefUniqSubjectBad() -> i32
                var a: i32 = 0
                var r = count(a@uniq)
                match r@uniq
                    .Ok(_) => return 0
                    .Err(let code) => return code@follow
            """,
        ["rvCaseElementBad"] = """
            struct Full
                public init() => ()
            func pair(x: uniq/i32) -> (Result<uniq/i32, Full>, i32) => (.Ok(x), 3)
            func rvCaseElementBad() -> Result<(), Full>
                var a: i32 = 0
                var t = pair(a@uniq)
                match t.0@move
                    .Ok(let w) => w@follow = 1
                    .Err(let e) => return .Err(e@move)
                return .Ok(())
            """,
        ["rvCaseFieldOfFreeOk"] = """
            struct Code
                public var n: i32
                public init(n: i32) => self.n = n
            func coded(x: uniq/i32) -> Result<uniq/i32, Code> => .Ok(x)
            func rvCaseFieldOfFreeOk() -> i32
                var a: i32 = 0
                match coded(a@uniq)@move
                    .Ok(_) => return 0
                    .Err(let e) => return e.n
            """,
        ["rvCaseRefTwiceOk"] = """
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func rvCaseRefTwiceOk(c: bool) -> i32
                var a: i32 = 0
                var b: i32 = 5
                let r = count(a@uniq)
                var p: ref/i32 = b@ref
                match r
                    .Ok(_) => return 0
                    .Err(let code)
                        if c => p = code
                        return p@follow
            """,
        ["rvCaseRefTwiceBuiltBad"] = """
            func rvCaseRefTwiceBuiltBad(c: bool) -> i32
                var a: i32 = 0
                var b: i32 = 5
                var r: Result<uniq/i32, i32> = .Err(3)
                if c => r = .Ok(a@uniq)
                var p: ref/i32 = b@ref
                match r
                    .Ok(_) => return 0
                    .Err(let code)
                        if c => p = code
                        return p@follow
            """,
        ["rvCaseRefTwoOk"] = """
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func rvCaseRefTwoOk(c: bool) -> i32
                var a: i32 = 0
                var d: i32 = 0
                let r = count(a@uniq)
                let s = count(d@uniq)
                match s
                    .Ok(_) => return 0
                    .Err(let first)
                        var p: ref/i32 = first
                        match r
                            .Ok(_) => return 1
                            .Err(let code)
                                if c => p = code
                                return p@follow
            """,
        ["rvCaseRefCycleOk"] = """
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func rvCaseRefCycleOk(c: bool) -> i32
                var a: i32 = 0
                var b: i32 = 5
                let r = count(a@uniq)
                var p: ref/i32 = b@ref
                var q: ref/i32 = p
                match r
                    .Ok(_) => return 0
                    .Err(let code)
                        if c => p = code
                        if c => q = p
                        if c => p = q
                        return q@follow
            """,
        ["rvCaseRefTwiceHolderBad"] = """
            struct RS {s}
                public let r: ref/i32 during s
                public let n: i32
                public init(r: ref/i32 during s, n: i32)
                    self.r = r
                    self.n = n
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func rvCaseRefTwiceHolderBad(c: bool) -> i32
                var a: i32 = 1
                var d: i32 = 0
                let s = RS.init(r: a@ref, n: 3)
                let n = s.n
                let r = count(d@uniq)
                var p: ref/i32 = n@ref
                match r
                    .Ok(_) => return 0
                    .Err(let code)
                        if c => p = code
                        let v = p@follow
                        a = 2
                        let w = v
                        return 0
            """,
        ["rvCaseRefCopyOk"] = """
            func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)
            func rvCaseRefCopyOk() -> i32
                var a: i32 = 0
                let r = count(a@uniq)
                match r
                    .Ok(_) => return 0
                    .Err(let code)
                        let q = code
                        return q@follow
            """,
        ["caseWriteR2Bad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2Bad() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["caseWriteR2Ok"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2Ok() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        let k = e
                        kk = 2
                        return 0
            """,
        ["caseWriteR2CleanOk"] = """
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2CleanOk() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var w: u64 = 3
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["caseWriteR2ConstOk"] = """
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2ConstOk() -> u64
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, 3)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e) => return e
            """,
        ["caseWriteR2FieldBad"] = """
            struct RS {s}
                public let r: ref/u8 during s
                public let n: u64
                public init(r: ref/u8 during s, n: u64)
                    self.r = r
                    self.n = n
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2FieldBad() -> u64
                var kk: u8 = 1
                let s = RS.init(r: kk@ref, n: 3)
                let w = s.n
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["caseWriteR2KeptBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2KeptBad() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                let rr = res@uniq
                put(rr, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["caseWriteOtherOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteOtherOk() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var b: i32 = 0
                var res = mk(a@uniq)
                var oth = mk(b@uniq)
                let ro = res@uniq
                put(oth@uniq, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["caseWriteOkCaseOk"] = """
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put2(dst: uniq/Result<uniq/i32 during s, u64>, y: uniq/i32 during s) => ()
            func caseWriteOkCaseOk() -> u64
                var a: i32 = 0
                var b: i32 = 0
                var res = mk(a@uniq)
                put2(res@uniq, b@uniq)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e) => return e
            """,
        ["caseWriteR2LenderOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2LenderOk() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        a = 2
                        return e
            """,
        ["caseWriteR2LenderUseOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2LenderUseOk() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        a = 2
                        let k = e
                        return 0
            """,
        ["caseWriteR2KeptUseOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2KeptUseOk() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                let rr = res@uniq
                put(rr, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        a = 2
                        let k = e
                        return 0
            """,
        ["caseWriteR2LaterBad"] = """
            struct RS {s}
                public let r: ref/u8 during s
                public let n: u64
                public init(r: ref/u8 during s, n: u64)
                    self.r = r
                    self.n = n
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2LaterBad() -> u64
                var kk: u8 = 1
                let s = RS.init(r: kk@ref, n: 3)
                var w = s.n
                var a: i32 = 0
                var res = mk(a@uniq)
                put(res@uniq, w)
                w = 7
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["caseWriteR2ExposedBad"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2ExposedBad() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                var rr = res@uniq
                let pr = rr@raw
                put(rr, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        kk = 2
                        return e
            """,
        ["caseWriteR2ExposedUseOk"] = """
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func mk(x: uniq/i32) -> Result<uniq/i32, u64> => .Ok(x)
            func put(dst: uniq/Result<uniq/i32 during s, u64>, v: u64) => ()
            func caseWriteR2ExposedUseOk() -> u64
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var w: u64 = 3
                keep(w@raw@raw/(raw/u8), spare)
                var a: i32 = 0
                var res = mk(a@uniq)
                var rr = res@uniq
                let pr = rr@raw
                put(rr, w)
                match res@move
                    .Ok(_) => return 0
                    .Err(let e)
                        a = 2
                        let k = e
                        return 0
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
    [InlineData("kCalleeConvUsedAfterBad", "kCalleeConvUsedAfterBad: kk = 2")] // I, the result, carrying w's Loan, is used after: a use of w (15.6)
    [InlineData("kCalleeConvReadBad", "kCalleeConvReadBad: kk = 2")] // I, w, which carries spare, is read after (15.6 Holders)
    [InlineData("kCalleeRefOk", "")] // V, a result whose Type holds no raw pointer keeps its exclusive Loan only (15.6.4)
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

    // H's coverage table (a5v-u6flow-design.md, section 1 and R1.2), each row labelled from SPEC 15.6 Holders and Loans (O-230 Q1 (a),
    // O-364 (a): as written, a Scalar part's Copy included), 15.6.4 and 5.7 Calls; rows marked "with G935's rule R" are invalid and keep
    // today's acceptance until Binding retains an Origin-free input's own value (PLAN G935). The tryErr, case and rvCase rows are the
    // Case bound's (the owner's O-383 (b)): a value Moved or Copied out of a Case payload of a call's result, a payload of a payload
    // included, directly or through a shared reference to the result, carries only the Loans the result can hold through that Case;
    // a Field of the result or of a payload, and a value the body built, keep O-364 (a)'s rule.
    [Theory]
    [InlineData("cpyOutBad", "cpyOutBad: kk = 2")] // I, z, a Copy of w (which carries r's Loan), is read after (15.6 Holders' Copy sentence; U6's F1)
    [InlineData("cpyOutOk", "")] // V
    [InlineData("cpyPunBad2", "cpyPunBad2: kk = 2")] // I, z copied on into the exposed w2 and a pointer read over w2's bytes (U6's F1)
    [InlineData("cpyPunOk2", "")] // V
    [InlineData("cpyPunDirectBad2", "cpyPunDirectBad2: kk = 2")] // I, the control: the Copy straight into the exposed local (U6's holders)
    [InlineData("cpyHolderBad", "cpyHolderBad: kk = 2")] // I, the control: into a Loan-holding Tuple (a holder by its Type)
    [InlineData("cpyTupOutBad", "cpyTupOutBad: kk = 2")] // I, a Scalar part of an exposed Tuple holder copied out (accepted before on every build)
    [InlineData("cpyTupOutOk", "")] // V
    [InlineData("cpyTupPunBad2", "cpyTupPunBad2: kk = 2")] // I, the same re-punned (accepted before on every build)
    [InlineData("cpyTupAggBad", "cpyTupAggBad: kk = 2")] // I, into an aggregate built by value (15.6 Loans bullet 1)
    [InlineData("cpyTupAggOk", "")] // V
    [InlineData("loopCpyBad", "loopCpyBad: kk = 2")] // I, a = b in a loop: the second iteration's a holds the first's Copy (liveness's fixed point)
    [InlineData("loopCpyOk", "")] // V
    [InlineData("capCpyBad", "capCpyBad: kk = 2")] // I, a Closure capturing the Copy (15.6 Loans bullet 1)
    [InlineData("capCpyOk", "")] // V
    [InlineData("argUseBad", "argUseBad: kk = 2")] // I, the Copy passed to a call after kk = 2 (a use)
    [InlineData("arithOk", "")] // V, an arithmetic result carries nothing (A3 5's val)
    [InlineData("cpyResultBad", "")] // I; kk = 2 with G935's rule R (an Origin-free input's own value at a result that can hold it, 15.6.4)
    [InlineData("cpyResultOk", "")] // V
    [InlineData("cpyFieldBad", "")] // I; kk = 2 with G935's rule R (a constructor's Field holds the input by value)
    [InlineData("cpyFieldOk", "")] // V
    [InlineData("narrowResultOk", "")] // V, a u8 result cannot hold a u64 or a part of one (15.6.4)
    [InlineData("intFirstBad", "")] // I; kk = 2 with G935's rule R (put64 stores v at d: 5.7 Calls, 5.2.1 Retained dependencies)
    [InlineData("intFirstPunBad", "")] // I; kk = 2 with G935's rule R
    [InlineData("intFirstPunOk", "")] // V
    [InlineData("storeCopyBad", "")] // I; kk = 2 with G935's rule R (set stores v through x into a)
    [InlineData("storeCopyOk", "")] // V
    [InlineData("storeSafeOk", "")] // V, a safe store of a constant
    [InlineData("intPastBad", "")] // I; kk = 2 with G935's rule R (the storage past a parameter's raw layer without an Origin)
    [InlineData("intPastPunBad2", "")] // I; kk = 2 with G935's rule R
    [InlineData("intPastOk", "")] // I by 15.6.4's composition (twoLayers; O-230 Q2 (a)); kk = 2 with G935's rule R and OwnPointer's pointee test deleted (rule D, PLAN G841)
    [InlineData("ownerQBad", "")] // I; kk = 2 with G935's rule R
    [InlineData("ownerQOk", "")] // V
    [InlineData("intPastScalarOk", "")] // V, the control: w0 never punned, so it carries nothing
    [InlineData("cpyNoHolderOk", "")] // V, a Copy of a local that carries nothing
    [InlineData("resultPlainOk", "")] // V
    [InlineData("sharedSrcOk", "")] // V, a shared-only source (5.2.1 condition 3)
    [InlineData("cpyReplOk", "")] // V, z = 5 replaces the Copy (15.6 Holders: completely replaced)
    [InlineData("replOk", "")] // V
    [InlineData("partWriteBad", "partWriteBad: kk = 2")] // I, a part write ends none (15.6 Holders)
    [InlineData("rvPastU64HolderBad", "")] // I (O-364 (a): v = t.0 carries t's Loans; 15.6.4's composition); kk = 2 with G935's rule R
    [InlineData("rvPastU8OwnBad", "")] // I (O-364 (a): a u8 own value at a u8 past layer); kk = 2 with G935's rule R
    [InlineData("rvPastU8Ok", "")] // V, past a parameter's raw/u8 layer the u8 admits no raw/u8 own value (15.6.4's oneLayer)
    [InlineData("rvPastU8UniqOk", "")] // V
    [InlineData("rvPastU8UniqBad", "rvPastU8UniqBad: y@follow = 5")] // I, x itself receives v, which addresses t
    [InlineData("rvCompRawBad", "rvCompRawBad: kk = 2")] // I, the composition with an Origin-carrying value (O-230 Q2 (a))
    [InlineData("rvCompTupBad", "rvCompTupBad: kk = 2")] // I
    [InlineData("rvCompRawOk", "")] // V
    [InlineData("rvTupPartSafeBad", "rvTupPartSafeBad: a = 2")] // I (15.6 Holders' Copy sentence as written, O-364 (a)): a Scalar part of a Loan-holding Tuple copied, the owner written, the Copy used
    [InlineData("rvTupPartSafeOk", "")] // V
    [InlineData("rvFieldSafeBad", "rvFieldSafeBad: a = 2")] // I (O-364 (a)): the same with a struct Field
    [InlineData("rvFieldSafeOk", "")] // V
    [InlineData("rvReborrowRetBad", "rvReborrowRetBad: var x = KI.init()")] // I (O-364 (a)): a Scalar read through a reference reborrowed through another holder, returned past x's end
    [InlineData("rvReborrowUseBad", "rvReborrowUseBad: x.v = 5")] // I (O-364 (a)): the same used after x.v = 5
    [InlineData("rvReborrowUseOk", "")] // V
    [InlineData("rvDerefPlainOk", "")] // V, the control: r reaches x only, which carries nothing
    [InlineData("rvCopyUseBad", "rvCopyUseBad: kk = 2")] // I, the Copy by assignment into an initialized local: d carries w0's Loans and is read after (15.6 Holders' Copy sentence)
    [InlineData("rvPutROk", "")] // V, a Scalar argument the builder spills into a temporary before a borrow argument; d is not used after
    [InlineData("rvPutRNoKeepOk", "")] // V, the same with d read after and no keep: w0 carries nothing, so neither does its Copy in the temporary
    [InlineData("rvPutTROk", "")] // V, the spill before a borrow argument in a generic call (T = u64); d is not used after
    [InlineData("rvPickSpillOk", "")] // V, the spill in a call whose result is stored into an exposed local; e is not used after
    [InlineData("tryErrLocal", "")] // V (the owner's O-383 (b)): the Err payload `try` Moves out of reserve's result carries none, Full holding no Loan
    [InlineData("tryErrMatchLocal", "")] // V (O-383 (b)): the same by an explicit match, the result Moved whole into the Subject
    [InlineData("tryErrLetLocal", "")] // V (O-383 (b)): the result Moved whole into a local first is still the call's result
    [InlineData("tryErrExposedE", "")] // V (O-383 (b)): the payload carries none into an exposed binding either
    [InlineData("tryErrParamOk", "")] // V, the control: the lender is a parameter's referent
    [InlineData("tryErrFreshOk", "")] // V, the control: a fresh Err is returned
    [InlineData("caseHoldsUseBad", "caseHoldsUseBad: a = 2")] // I, a Case whose payload Type holds the reference: e carries the result's Loan on a
    [InlineData("caseHoldsUseOk", "")] // V
    [InlineData("casePartHoldsBad", "casePartHoldsBad: a = 2")] // I (O-364 (a) within the Case): a Scalar beside the reference in the payload, copied out of the binding
    [InlineData("caseBuiltLocal", "caseBuiltLocal: var a: i32 = 0")] // I (O-364 (a); O-383 (b) bounds a call's result only): a value the body built, its Err payload returned past a's end
    [InlineData("callFieldSafeBad", "callFieldSafeBad: a = 2")] // I (O-364 (a)): a struct Field of a call's result, which O-383 (b) does not bound
    [InlineData("tryErrRefRead", "")] // V (O-383 (b); I before the Middle's reading of 2026-11-17): a Copy out of the payload through a shared reference to the result is bounded like a Move
    [InlineData("caseOwnValueBad", "")] // I; kk = 2 with G935's rule R read per Case: the Err Case, u64, then holds v's own value, which the payload Type does not show
    [InlineData("rvCaseNestedTryOk", "")] // V (O-383 (b), a Case payload of a Case payload): through Ok then Err the result holds a Full; two `try`s
    [InlineData("rvCaseOptionOk", "")] // V (O-383 (b)): through Some then Err, a fallible iterator's `Option<Result<..>>`
    [InlineData("rvCaseNestedOk", "")] // V (O-383 (b)): the same by two nested matches
    [InlineData("rvCaseNestedHoldsBad", "rvCaseNestedHoldsBad: a = 2")] // I, the other side: through Ok then Err the result holds the reference on a
    [InlineData("rvCaseNestedBuiltBad", "rvCaseNestedBuiltBad: var a: i32 = 0")] // I (O-364 (a)): the payload's binding also receives a value the body built, so it is no Case payload of a call's result only
    [InlineData("rvCaseFieldResBad", "rvCaseFieldResBad: var a: i32 = 0")] // I (O-364 (a)'s Field rule before the Case): a Case payload of a Field of a call's result carries what the holder carries
    [InlineData("rvCasePayloadFieldBad", "rvCasePayloadFieldBad: var a: i32 = 0")] // I (O-364 (a)): the same Field rule inside a payload, a Case payload of a Field of the Ok payload
    [InlineData("rvCaseUniqOk", "")] // V (O-383 (b)): an exclusive borrow of the call's result passed to a call does not unbound it (15.6.4: touchR retains nothing)
    [InlineData("rvCaseRawOk", "")] // V (O-383 (b)): the result's address formed; raw writes stay under 15.6 Addresses
    [InlineData("rvCaseRefCodeOk", "")] // V (O-383 (b)): a Copy out of the payload through the shared match Subject is bounded like a Move
    [InlineData("rvCaseRefHoldsBad", "rvCaseRefHoldsBad: a = 2")] // I (O-364 (a) within the Case): through the shared Subject, a Scalar beside the reference in the payload
    [InlineData("rvCaseRefBuiltBad", "rvCaseRefBuiltBad: var a: i32 = 0")] // I (O-364 (a)): the shared read of a value the body built is not bounded
    [InlineData("rvCaseAssignBad", "rvCaseAssignBad: kk = 2")] // I (O-364 (a)): a value the body built assigned into the call's result holder; e carries what the holder carries
    [InlineData("rvCaseRefWriteBad", "")] // I (O-364 (a)); kk = 2 once A5 reads a store through a reference into a call's result holder as the body's own value (a known limit; accepted on every build before H)
    [InlineData("rvCaseLoopOk", "")] // V (O-383 (b)): two calls' results in one local
    [InlineData("rvCaseSharedOk", "")] // V (O-383 (b)): a shared borrow of the result writes nothing (5.2.1 condition 3)
    [InlineData("rvMatchBuiltBad", "rvMatchBuiltBad: a = 2")] // I (O-364 (a)): a body-built Result's Copy-type Err payload copied out while its Ok Case may hold a shared Loan on a
    [InlineData("rvGenCaseBad", "")] // I; kk = 2 with G935's abstract-storage Value rows read per Case: the Err Case, T = u64, then holds v's own value (as caseOwnValueBad)
    [InlineData("rvCaseKeptOk", "")] // V (O-383 (b)): an exclusive reference to the call's result kept in a local and never stored through does not unbound it
    [InlineData("rvCasePairOk", "")] // V (O-383 (b)): an element of a Case payload Moved into a local is bounded as the result is
    [InlineData("rvCasePairPartBad", "rvCasePairPartBad: a = 2")] // I (O-364 (a) within the Case): the Scalar beside the Result in the payload, whose Type holds the reference
    [InlineData("rvCaseRefNestedOk", "")] // V (O-383 (b)): a Copy out of a nested Case payload through shared references
    [InlineData("rvCaseRefUniqSubjectBad", "rvCaseRefUniqSubjectBad: var a: i32 = 0")] // I (O-364 (a)): the payload read through an exclusive Subject, which the bound does not follow
    [InlineData("rvCaseElementBad", "rvCaseElementBad: var a: i32 = 0")] // I (O-364 (a)'s rule for an element before the Case): a Case payload of an element of a Tuple result
    [InlineData("rvCaseFieldOfFreeOk", "")] // V (O-383 (b)): a Field of a Case payload whose Type holds no Loan lies in it
    [InlineData("rvCaseRefTwiceOk", "")] // V (O-383 (b)): a Copy through a shared reference stored twice, which refers to a local that is no holder or to the Err payload of a call's result
    [InlineData("rvCaseRefTwiceBuiltBad", "rvCaseRefTwiceBuiltBad: var a: i32 = 0")] // I (O-364 (a)): the same where the result is a value the body built, so one Place the reference refers to carries what its holder carries
    [InlineData("rvCaseRefTwoOk", "")] // V (O-383 (b)): both Places the reference refers to are Err payloads of calls' results
    [InlineData("rvCaseRefCycleOk", "")] // V (O-383 (b)): the same Places through reference locals that Copy each other
    [InlineData("rvCaseRefTwiceHolderBad", "rvCaseRefTwiceHolderBad: a = 2")] // I (O-364 (a)): one Place the reference refers to is a local that is a holder by value flow
    [InlineData("rvCaseRefCopyOk", "")] // V (O-383 (b)): a Copy of the payload's reference refers to what it refers to
    [InlineData("caseWriteR2Bad", "")] // I; kk = 2 once a rule retains v in put's write (G935's rule R): put's write of a Loan-carrying u64 is then a write source of res's Loan-free payloads (MirBorrows.Holders' Writes, WriteSlots)
    [InlineData("caseWriteR2Ok", "")] // V: e is not used after kk = 2
    [InlineData("caseWriteR2CleanOk", "")] // V: w is plain, so the u64 put may write carries nothing and res stays a call's result
    [InlineData("caseWriteR2ConstOk", "")] // V: a constant written
    [InlineData("caseWriteR2FieldBad", "")] // I (O-364 (a): w, a Copy of a Field of s, carries kk's Loan); kk = 2 with G935's rule R, as caseWriteR2Bad
    [InlineData("caseWriteR2KeptBad", "")] // I; kk = 2 with G935's rule R: put writes through rr, which refers to res
    [InlineData("caseWriteOtherOk", "")] // V: put writes into oth, not res; res's exclusive reference ro is never written through
    [InlineData("caseWriteOkCaseOk", "")] // V (O-383 (b)): put2 may store y only in the Ok Case, and a value of a Type that holds a Loan reaches no Loan-free payload
    [InlineData("caseWriteR2LenderOk", "")] // D (the Middle: V; mine: I at var kk: u8 = 1, SPEC 15.6: e carries r's Loan on kk and is returned past kk's end); with G935's rule R: var kk: u8 = 1 and a = 2 while the write mixed res, var kk: u8 = 1 alone with the write slot
    [InlineData("caseWriteR2LenderUseOk", "")] // V (O-383 (b), the Middle's phase-6 decision 2): put's write reaches only the Err payload, so e carries r's Loan and not a's; a = 2 with G935's rule R while the write mixed res
    [InlineData("caseWriteR2KeptUseOk", "")] // V: the same through a kept exclusive reference
    [InlineData("caseWriteR2LaterBad", "")] // I (O-364 (a)); kk = 2 with G935's rule R: the write slot keeps the value written, though w is overwritten after the call
    [InlineData("caseWriteR2ExposedBad", "")] // I; kk = 2 with G935's rule R: put writes through an exposed reference local, so every exposed local is written (Written's lost argument)
    [InlineData("caseWriteR2ExposedUseOk", "")] // V: the same with e used after a = 2; the lost argument's targets keep their bound
    public void AValueReadFromAHolderCarriesItsLoansWhateverItsType(string form, string expected)
    {
        var c = MirBuildTest.Compile(ExposedShapes + FlowForms[form] + "\npublic func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes))}]");
    }

    // The Case bound's fact, which locals hold only calls' results (MirBorrows.Holders' Mixing), is linear in the body: a call's result
    // Moved whole through N locals, then its Err payload Moved out and returned past the lender's end (N = 16, 32, 64; valid by the
    // owner's O-383 (b) at every N, which Measured asserts).
    [Fact]
    public void ACallsResultMovedWholeStaysOneInLinearWork()
    {
        const string Head = "struct Full\n    public init() => ()\nfunc reserve(x: uniq/i32) -> Result<uniq/i32, Full> => .Ok(x)\n" +
            "func shape() -> Result<(), Full>\n    var a: i32 = 0\n    let r0 = reserve(a@uniq)\n«N:    let r{1} = r{0}@move\n»";
        const string Tail = "@move\n        .Ok(let w) => w@follow = 1\n        .Err(let e) => return .Err(e@move)\n    return .Ok(())\n" +
            "public func main() => ()\n";
        long[] work = [.. new[] { 16, 32, 64 }.Select(n => Measured(Expand(Head, n, 0) + $"    match r{n}" + Tail).Work)];
        output.WriteLine($"N 16, 32, 64: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 3), $"work {string.Join(", ", work)}");
    }

    // The Places a shared reference local refers to (MirBorrows.Values' Sources over MirBorrows.Loans' Referred) are followed in work
    // linear in its stores and reads: a reference stored N more times with the Err payload's reference, then N Copies through it into
    // a local returned past the lender's end (N = 16, 32, 64; valid by the owner's O-383 (b) at every N, which Measured asserts).
    [Fact]
    public void AReferenceStoredManyTimesIsFollowedInLinearWork()
    {
        const string Head = "func count(x: uniq/i32) -> Result<uniq/i32, i32> => .Ok(x)\nfunc shape(c: bool) -> i32\n    var a: i32 = 0\n" +
            "    var b: i32 = 5\n    let r = count(a@uniq)\n    var p: ref/i32 = b@ref\n    match r\n        .Ok(_) => return 0\n" +
            "        .Err(let code)\n            var s: i32 = 0\n«N:            if c => p = code\n»«N:            s = p@follow\n»" +
            "            return s\npublic func main() => ()\n";
        long[] work = [.. new[] { 16, 32, 64 }.Select(n => Measured(Expand(Head, n, 0)).Work)];
        output.WriteLine($"N 16, 32, 64: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 3), $"work {string.Join(", ", work)}");
    }
}
