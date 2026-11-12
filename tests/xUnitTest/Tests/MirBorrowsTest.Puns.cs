// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's pun rule at the Place a raw pointer reaches (PLAN G667b; SPEC 15.6, 5.2.1 condition 1, 5.2, 5.3, 5.5, 5.7): a scalar
/// written over a Place that holds a raw pointer, at a Type the Place does not designate, leaves that pointer reaching only storage the
/// body does not own.</summary>
public sealed partial class MirBorrowsTest
{
    private const string PunForms = """
        func heap() -> raw/u8 during static
            unsafe => return Raw.allocate<u8>(8)@(raw/u8 during static)
        func stash(slot: raw/(raw/u8), value: raw/u8) => ()
        func punAt(p: raw/usize, v: usize)
            unsafe => *p = v
        func punPast(pp: raw/(raw/usize), v: usize)
            unsafe => *(*pp) = v
        func conv(slot: raw/(raw/u8), v: ref/(raw/usize))
            unsafe => *slot = v@follow@raw/u8
        func putq(target: uniq/(raw/u8) during a, q: raw/u8 during a) => ()
        func usep<T>(p: raw/T) => ()
        func setTag(p: raw/u32, v: u32)
            unsafe => *p = v
        #Layout("C")
        struct NodeC
            public var tag: u32
            public var next: raw/(raw/u8)
            public init()
                self.tag = 0
                self.next = null
        #Layout("C")
        struct NodeD
            public var next: raw/(raw/u8)
            public var tag: u32
            public init()
                self.next = null
                self.tag = 0
        enum ZE
            Has([0 of raw/(raw/u8)])
            Nil
        struct NodeK
            public var tag: u32
            public var next: raw/(raw/u8)
            public init()
                self.tag = 0
                self.next = null
        struct WrapT<T>
            public var value: T
            public init(value: T)
                self.value = value@move
        func mutateAt<U>(x: uniq/U) => ()
        func lookAt<U>(x: ref/U) => ()
        func storePunBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            let bytes = q@raw@raw/usize
            unsafe => *bytes = heap()@usize
            stash(q, spare)
            kk = 2
        func readPunBad()
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
            stash(q, spare)
            kk = 2
        func retainedPunBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            var box: raw/usize = q@raw@raw/usize
            var p: raw/u8 = null
            conv(p@raw, box@ref)
            unsafe => *p = 5
            stash(q, spare)
            kk = 2
        func callPunBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            punAt(q@raw@raw/usize, heap()@usize)
            stash(q, spare)
            kk = 2
        func pastPunBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            var p = q@raw@raw/usize
            punPast(p@raw, heap()@usize)
            stash(q, spare)
            kk = 2
        func arrayMidBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of (usize, raw/(raw/u8))] = [(0, other@raw), (0, other@raw)]
            unsafe
                let mid = (h@raw@raw/u8 + 8)@raw/(usize, raw/(raw/u8))
                *mid = (heap()@usize, other@raw)
            stash(h[0].1, spare)
            stash(h[1].1, spare)
            kk = 2
        func nullPunFalseError()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            let bytes = q@raw@raw/usize
            unsafe => *bytes = 0
            stash(q, spare)
            kk = 2
        func castBackStoreOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), usize) = (other@raw, 0)
            let a = h.1@raw@raw/u8@raw/usize
            unsafe => *a = 9
            stash(h.0, spare)
            kk = 2
        func castBackCallOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), usize) = (other@raw, 0)
            let a = h.1@raw@raw/u8@raw/usize
            punAt(a, 9)
            stash(h.0, spare)
            kk = 2
        func mixedOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (usize, raw/(raw/u8)) = (5, other@raw)
            let np = h.0@raw
            unsafe => *np = 7
            stash(h.1, spare)
            kk = 2
        func arrayElemOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of (usize, raw/(raw/u8))] = [(0, other@raw), (0, other@raw)]
            let a = h@raw@raw/(usize, raw/(raw/u8))
            unsafe => a[1] = (5, other@raw)
            stash(h[0].1, spare)
            kk = 2
        func arrayWalkOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of raw/(raw/u8)] = [other@raw, other@raw]
            let a = h@raw@raw/(raw/(raw/u8))
            var i: isize = 0
            while i < 2
                unsafe => a[i] = other@raw
                i += 1
            stash(h[1], spare)
            kk = 2
        func statedOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            let o = other@uniq
            var q = o@follow@raw@raw/u8
            let bytes = q@raw@raw/usize
            unsafe => *bytes = heap()@usize
            putq(spare@uniq, q)
            kk = 2
        func sliceReadOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            let bytes = q@raw@raw/u8
            unsafe
                let sl = Raw.slice(bytes, 8)
                let first = sl[0]
            stash(q, spare)
            kk = 2
        func wholeTupleRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (usize, raw/(raw/u8)) = (0, other@raw)
            let bp = h@raw@raw/usize
            unsafe => *bp = 5
            stash(h.1, spare)
            kk = 2
        func wholeTupleCallRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (usize, raw/(raw/u8)) = (0, other@raw)
            punAt(h@raw@raw/usize, 5)
            stash(h.1, spare)
            kk = 2
        func otherTypeRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (u64, raw/(raw/u8)) = (0, other@raw)
            let tp = h@raw@raw/u32
            unsafe => *tp = 7
            stash(h.1, spare)
            kk = 2
        func otherTypeCallRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (u64, raw/(raw/u8)) = (0, other@raw)
            setTag(h@raw@raw/u32, 7)
            stash(h.1, spare)
            kk = 2
        func zeroArrayOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            var z: [0 of u16] = [0 of 0]
            let tp = q@raw@raw/[0 of u16]
            unsafe => *tp = z
            stash(q, spare)
            kk = 2
        func lengthArrayRests<length N>(z: [N of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            let tp = q@raw@raw/[N of u16]
            unsafe => *tp = z
            stash(q, spare)
            kk = 2
        func lengthHolderElemOk<length N>(z: [N of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [N of (usize, raw/(raw/u8))] = [N of (0, other@raw)]
            let a = h@raw@raw/(usize, raw/(raw/u8))
            unsafe => (*a).0 = 5
            stash(h[0].1, spare)
            kk = 2
        func lengthHolderRests<length N>(z: [N of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [N of raw/(raw/u8)] = [N of other@raw]
            let tp = h@raw@raw/u16
            unsafe => *tp = 7
            stash(h[0], spare)
            kk = 2
        func zeroHolderBranchOk(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [0 of raw/(raw/u8)] = [0 of other@raw]
            let tp = h@raw@raw/u16
            if flag
                unsafe => *tp = 7
            stash(h[0], spare)
            kk = 2
        func zeroHolderUseOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [0 of raw/(raw/u8)] = [0 of other@raw]
            let tp = h@raw@raw/u16
            unsafe => *tp = 7
            stash(h[0], spare)
            kk = 2
        func zeroHolderCallOk(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [0 of raw/(raw/u8)] = [0 of other@raw]
            if flag
                unsafe => setTag(h@raw@raw/u32, 7)
            stash(h[0], spare)
            kk = 2
        func lengthZeroExprRests<length N>(z: [(N - N) of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            let tp = q@raw@raw/[(N - N) of u16]
            unsafe => *tp = z
            stash(q, spare)
            kk = 2
        func lengthMulZeroRests<length N>(z: [(N * 0) of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            let tp = q@raw@raw/[(N * 0) of u16]
            unsafe => *tp = z
            stash(q, spare)
            kk = 2
        func lengthZeroCondRests<length N>(gate: [(0 - N) of u8], z: [N of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            let tp = q@raw@raw/[N of u16]
            unsafe => *tp = z
            stash(q, spare)
            kk = 2
        func lengthZeroExprHolderRests<length N>(z: [(N - N) of u16], flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [(N - N) of raw/(raw/u8)] = [(N - N) of other@raw]
            let tp = h@raw@raw/u16
            if flag
                unsafe => *tp = 7
            stash(h[0], spare)
            kk = 2
        func zeroPayloadBranchOk(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var o: ZE = .Has([0 of other@raw])
            let tp = o@raw@raw/u16
            if flag
                unsafe => *tp = 7
            match o
                .Has(let a) => stash(a[0], spare)
                .Nil => ()
            kk = 2
        func zeroInTupleHolderOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (u64, [0 of raw/(raw/u8)]) = (0, [0 of other@raw])
            let tp = h@raw@raw/u32
            unsafe => *tp = 7
            stash(h.1[0], spare)
            kk = 2
        func zeroNestBranchOk(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of [0 of raw/(raw/u8)]] = [2 of [0 of other@raw]]
            let tp = h@raw@raw/u16
            if flag
                unsafe => *tp = 7
            stash(h[0][0], spare)
            kk = 2
        func lengthZeroNestOk<length N>(z: [N of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [N of [0 of raw/(raw/u8)]] = [N of [0 of other@raw]]
            let tp = h@raw@raw/u16
            unsafe => *tp = 7
            stash(h[0][0], spare)
            kk = 2
        func kLayoutFieldRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeK.init()
            n.next = other@raw
            let tp = n@raw@raw/u32
            unsafe => *tp = 7
            stash(n.next, spare)
            kk = 2
        func cLayoutSecondFieldBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeD.init()
            n.next = other@raw
            let tp = n@raw@raw/u32
            unsafe => *tp = 7
            stash(n.next, spare)
            kk = 2
        func cLayoutNoWrite()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeC.init()
            n.next = other@raw
            stash(n.next, spare)
            kk = 2
        func cLayoutFirstFieldTyped()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeC.init()
            n.next = other@raw
            let tp = n@raw@raw/u32
            unsafe => *tp = 7
            stash(n.next, spare)
            kk = 2
        func cLayoutFirstFieldCallTyped()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeC.init()
            n.next = other@raw
            setTag(n@raw@raw/u32, 7)
            stash(n.next, spare)
            kk = 2
        func wholeTupleLoanOk()
            var kk: u8 = 1
            let r = kk@uniq
            var other: raw/u8 = null
            var h: (usize, raw/(raw/u8)) = (0, other@raw)
            let bp = h@raw@raw/usize
            unsafe => *bp = 5
            usep(h.1)
            kk = 2
        func wholeTupleNoLoanOk()
            var other: raw/u8 = null
            var h: (usize, raw/(raw/u8)) = (0, other@raw)
            let bp = h@raw@raw/usize
            unsafe => *bp = 5
            usep(h.1)
        func wholeTupleCallLoanOk()
            var kk: u8 = 1
            let r = kk@uniq
            var other: raw/u8 = null
            var h: (usize, raw/(raw/u8)) = (0, other@raw)
            punAt(h@raw@raw/usize, 5)
            usep(h.1)
            kk = 2
        func arrayMidLoanOk()
            var kk: u8 = 1
            let r = kk@uniq
            var other: raw/u8 = null
            var h: [2 of (usize, raw/(raw/u8))] = [(0, other@raw), (0, other@raw)]
            unsafe
                let mid = (h@raw@raw/u8 + 8)@raw/(usize, raw/(raw/u8))
                *mid = (5, other@raw)
            usep(h[0].1)
            kk = 2
        func kLayoutFieldLoanOk()
            var kk: u8 = 1
            let r = kk@uniq
            var n = NodeK.init()
            let tp = n@raw@raw/u32
            unsafe => *tp = 7
            usep(n.next)
            kk = 2
        func cLayoutFieldStoreLoanOk()
            var kk: u8 = 1
            let r = kk@uniq
            var n = NodeC.init()
            let tp = n@raw@raw/u32
            unsafe => *tp = 7
            usep(n.next)
            kk = 2
        func cLayoutFieldCallLoanOk()
            var kk: u8 = 1
            let r = kk@uniq
            var n = NodeC.init()
            setTag(n@raw@raw/u32, 7)
            usep(n.next)
            kk = 2
        func absTakeBackBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/T
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absCallBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            usep(h@raw@raw/T)
            stash(h[0], spare)
            kk = 2
        func absOnTupleRests<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let tp = h@raw@raw/T
            unsafe => *tp = (*tp)@move
            stash(h.0, spare)
            kk = 2
        func absOnTupleCopyBad<T>(v: T)
            T is Copy
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let tp = h@raw@raw/T
            unsafe => *tp = *tp
            stash(h.0, spare)
            kk = 2
        func absOnTupleCallBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            usep(h@raw@raw/T)
            stash(h.0, spare)
            kk = 2
        func absOffTupleRests<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let bp = h@raw@raw/u64
            unsafe
                let tp = (bp + 1)@raw/T
                *tp = (*tp)@move
            stash(h.0, spare)
            kk = 2
        func absOffTupleCallRests<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let bp = h@raw@raw/u64
            unsafe => usep((bp + 1)@raw/T)
            stash(h.0, spare)
            kk = 2
        func absOffTupleNoWriteOk<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let bp = h@raw@raw/u64
            unsafe
                let tp = (bp + 1)@raw/T
            stash(h.0, spare)
            kk = 2
        func absOffGridBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of raw/(raw/u8)] = [2 of other@raw]
            let bp = h@raw@raw/u8
            unsafe
                let tp = (bp + 3)@raw/T
                *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absLengthRests<T, length N>(v: T, z: [(N - N) of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [N of raw/(raw/u8)] = [N of other@raw]
            let tp = h@raw@raw/T
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absLengthNoWriteOk<T, length N>(v: T, z: [(N - N) of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [N of raw/(raw/u8)] = [N of other@raw]
            let tp = h@raw@raw/T
            stash(h[0], spare)
            kk = 2
        func absZeroExprRests<T, length N>(v: T, z: [(N - N) of u16])
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [(N - N) of raw/(raw/u8)] = [(N - N) of other@raw]
            let tp = h@raw@raw/T
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absTupleStoreBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of raw/(raw/u8)] = [2 of other@raw]
            let tp = h@raw@raw/(u64, T)
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absArrayStoreBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/[1 of T]
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absArrayCallBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            usep(h@raw@raw/[1 of T])
            stash(h[0], spare)
            kk = 2
        func absFieldStoreBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/WrapT<T>
            unsafe => (*tp).value = ((*tp).value)@move
            stash(h[0], spare)
            kk = 2
        func absFieldCallBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            usep(h@raw@raw/WrapT<T>)
            stash(h[0], spare)
            kk = 2
        func absWholeStructOk<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/WrapT<T>
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absUniqBorrowBad<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/T
            unsafe => mutateAt((*tp)@uniq)
            stash(h[0], spare)
            kk = 2
        func absReadRefOk<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/T
            unsafe => lookAt((*tp)@ref)
            stash(h[0], spare)
            kk = 2
        func absReadCopyOk<T>(v: T)
            T is Copy
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/T
            var x = v
            unsafe => x = *tp
            stash(h[0], spare)
            kk = 2
        func absZeroTakeBackOk<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [0 of raw/(raw/u8)] = [0 of other@raw]
            let tp = h@raw@raw/T
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func absZeroCallOk<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [0 of raw/(raw/u8)] = [0 of other@raw]
            usep(h@raw@raw/T)
            stash(h[0], spare)
            kk = 2
        func absScalarHolderOk<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var slot: raw/(raw/u8) = other@raw
            var h: [1 of u64] = [1 of 0]
            let tp = h@raw@raw/T
            unsafe => *tp = (*tp)@move
            stash(slot, spare)
            kk = 2
        func absBehindLayerOk<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [1 of raw/(raw/u8)] = [1 of other@raw]
            let tp = h@raw@raw/(raw/T)
            unsafe => *tp = *tp
            stash(h[0], spare)
            kk = 2
        func fnStoreOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of raw/(raw/u8)] = [2 of other@raw]
            let tp = h@raw@raw/((u64) -> u64)
            unsafe => *tp = (*tp)@move
            stash(h[0], spare)
            kk = 2
        func fnCallOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: [2 of raw/(raw/u8)] = [2 of other@raw]
            usep(h@raw@raw/((u64) -> u64))
            stash(h[0], spare)
            kk = 2
        """;

    // PLAN G667b, SPEC 15.6 (a write may reach every Place its pointer's value carries a non-shared direct Loan or address of), 5.2.1
    // condition 1 (bounded to that Place), 5.2, 5.5: a scalar written over q, a raw pointer, at a Type q does not designate, leaves q
    // reaching only storage the body does not own, where stash may store spare, used after kk = 2 (the Bad rows, each accepted before):
    // a store through a cast (storePunBad), through a pointer read from punned bytes (readPunBad: 5.2, the read keeps the provenance of
    // the pointer written there) or retained by a call (retainedPunBad: 5.2.1 Retained dependencies); a call's first step (callPunBad,
    // 5.7 Calls: punAt may store a usize through p) and past it (pastPunBad: both layers of raw/(raw/usize) are the parameter's). The Ok
    // rows: a write at the Place's own Type through casts back (castBack*, 5.4), on a scalar component (mixedOk), on the element grid
    // (arrayElemOk, arrayWalkOk: 5.3, 4.1), over a layer stating another Origin (statedOk, 5.2, 5.7) and through an input the contract
    // stores nothing through (sliceReadOk, 5.6). Integer zero writes null (5.5), so q is still never read as a pointer reaching storage
    // the body does not own; A5 cannot tell a zero, the owner's exception O-105 (b) (nullPunFalseError).
    // A write A5 cannot place (IMPL 21.1.2: a Kimigayo-layout component's offset is the implementation's) is a pun or none, by layout:
    // a component's Type through the whole holder's address (wholeTuple*, kLayoutField*), another Scalar's there (otherType*: it may
    // sit on the scalar component's bytes alone) or a Tuple over [8, 24) of an array of Tuples (arrayMid*). Where the conflict at
    // kk = 2 rests on it (stash stores spare through the pointer it may have punned: invalid if a pun,
    // 5.5, 15.6), the check is a located Unsupported_Kd (the Rests rows, arrayMidBad); where nothing does (usep only receives the
    // pointer: valid under either layout), nothing is reported, with a Loan in the body or none (the LoanOk rows, wholeTupleNoLoanOk).
    // A C-layout struct's first Field is at offset 0 (IMPL 21.1.2, SPEC 6.7.1), so its Type through the struct's address is a typed
    // write of it: it changes no result. A5 keeps one state for a struct an init made, which holds what that call may have left in it,
    // so stash's store through n.next is a conflict without any write (cLayoutNoWrite), and the same conflict, not a limit, with the
    // typed one (cLayoutFirstField*Typed); cLayoutField*LoanOk are accepted. A later Field's Type there is a pun: NodeD's u32 starts on
    // next's first byte (the chain's end is a raw pointer: invalid, the conflict and no limit, cLayoutSecondFieldBad); a Kimigayo-layout
    // Field's is not decided (kLayoutFieldRests).
    // A written array of no element over a pointer is no pun (zeroArrayOk, 5.2: positive-sized). A length that is no constant is not
    // established positive (4.4.2: the body is checked for every binding that meets the public conditions, which may all make it 0:
    // `N - N`, `N * 0`, a bare `N` beside a `[(0 - N) of u8]` parameter), so no pun is decided over one, written or the holder's: a
    // located Unsupported_Kd where the conflict rests on it, never a Language error (lengthArrayRests, lengthZeroExprRests,
    // lengthMulZeroRests, lengthZeroCondRests; lengthHolderRests, lengthZeroExprHolderRests). A holder's typed rows need no length:
    // its element's Type through its address is typed (lengthHolderElemOk, 4.1). A holder of no element, at any depth, or a Tuple or
    // an enum whose only pointers are in one (zeroInTupleHolderOk, zeroPayloadBranchOk: bytes held, not Loans carried, 4.3.3),
    // holds no pointer, and a write at its address, seen or a call's, is no pun (zeroHolder*Ok, zeroNestBranchOk, lengthZeroNestOk;
    // 4.1, 5.2; the write is undefined when it runs, which is no compile-time error, 1.3).
    // An abstract written part (PLAN G734's F1; SPEC 8.15.1: every binding, a Scalar of any size among them) is a punnable write that
    // surely holds a Scalar, judged at its Position: over a pointer, a seen store, a call's write, a Field or an exclusive borrow of it
    // is the conflict (absTakeBackBad, absCallBad, absTupleStoreBad, absArrayStoreBad, absArrayCallBad, absFieldStoreBad,
    // absFieldCallBad, absUniqBorrowBad; off the grid of an all-pointer holder, absOffGridBad); at the address of a Place that holds a
    // pointer but not only pointers, a write all of whose bytes a binding decides covers it (absOnTupleCopyBad, absOnTupleCallBad); off
    // that address the layout decides, a located Unsupported_Kd (absOffTupleRests, absOffTupleCallRests), as at a holder's length that
    // is no constant (limit (c): absLengthRests, absZeroExprRests, the last valid). A non-Copy T stored at the address is an opaque
    // form (R4-M70), so its pointer's Position is not seen formed and it rests too (absOnTupleRests). A struct written whole, a Function
    // value, a read and a pointer to T are no pun; a holder with no pointer byte holds none (the abs*Ok rows, fnStoreOk, fnCallOk).
    [Theory]
    [InlineData("storePunBad", "storePunBad: kk = 2")]
    [InlineData("readPunBad", "readPunBad: kk = 2")]
    [InlineData("retainedPunBad", "retainedPunBad: kk = 2")]
    [InlineData("callPunBad", "callPunBad: kk = 2")]
    [InlineData("pastPunBad", "pastPunBad: kk = 2")]
    [InlineData("arrayMidBad", "arrayMidBad: kk = 2 Unsupported_Kd")]
    [InlineData("wholeTupleRests", "wholeTupleRests: kk = 2 Unsupported_Kd")]
    [InlineData("wholeTupleCallRests", "wholeTupleCallRests: kk = 2 Unsupported_Kd")]
    [InlineData("kLayoutFieldRests", "kLayoutFieldRests: kk = 2 Unsupported_Kd")]
    [InlineData("otherTypeRests", "otherTypeRests: kk = 2 Unsupported_Kd")]
    [InlineData("otherTypeCallRests", "otherTypeCallRests: kk = 2 Unsupported_Kd")]
    [InlineData("cLayoutSecondFieldBad", "cLayoutSecondFieldBad: kk = 2")]
    [InlineData("zeroArrayOk", "")]
    [InlineData("lengthArrayRests", "lengthArrayRests: kk = 2 Unsupported_Kd")]
    [InlineData("lengthHolderElemOk", "")]
    [InlineData("lengthHolderRests", "lengthHolderRests: kk = 2 Unsupported_Kd")]
    [InlineData("zeroHolderBranchOk", "")]
    [InlineData("zeroHolderUseOk", "")]
    [InlineData("zeroHolderCallOk", "")]
    [InlineData("lengthZeroExprRests", "lengthZeroExprRests: kk = 2 Unsupported_Kd")]
    [InlineData("lengthMulZeroRests", "lengthMulZeroRests: kk = 2 Unsupported_Kd")]
    [InlineData("lengthZeroCondRests", "lengthZeroCondRests: kk = 2 Unsupported_Kd")]
    [InlineData("lengthZeroExprHolderRests", "lengthZeroExprHolderRests: kk = 2 Unsupported_Kd")]
    [InlineData("zeroPayloadBranchOk", "")]
    [InlineData("zeroInTupleHolderOk", "")]
    [InlineData("zeroNestBranchOk", "")]
    [InlineData("lengthZeroNestOk", "")]
    [InlineData("cLayoutNoWrite", "cLayoutNoWrite: kk = 2")]
    [InlineData("cLayoutFirstFieldTyped", "cLayoutFirstFieldTyped: kk = 2")]
    [InlineData("cLayoutFirstFieldCallTyped", "cLayoutFirstFieldCallTyped: kk = 2")]
    [InlineData("wholeTupleLoanOk", "")]
    [InlineData("wholeTupleNoLoanOk", "")]
    [InlineData("wholeTupleCallLoanOk", "")]
    [InlineData("arrayMidLoanOk", "")]
    [InlineData("kLayoutFieldLoanOk", "")]
    [InlineData("cLayoutFieldStoreLoanOk", "")]
    [InlineData("cLayoutFieldCallLoanOk", "")]
    [InlineData("nullPunFalseError", "nullPunFalseError: kk = 2")]
    [InlineData("castBackStoreOk", "")]
    [InlineData("castBackCallOk", "")]
    [InlineData("mixedOk", "")]
    [InlineData("arrayElemOk", "")]
    [InlineData("arrayWalkOk", "")]
    [InlineData("statedOk", "")]
    [InlineData("sliceReadOk", "")]
    [InlineData("absTakeBackBad", "absTakeBackBad: kk = 2")]
    [InlineData("absCallBad", "absCallBad: kk = 2")]
    [InlineData("absOnTupleRests", "absOnTupleRests: kk = 2 Unsupported_Kd")]
    [InlineData("absOnTupleCopyBad", "absOnTupleCopyBad: kk = 2")]
    [InlineData("absOnTupleCallBad", "absOnTupleCallBad: kk = 2")]
    [InlineData("absOffTupleRests", "absOffTupleRests: kk = 2 Unsupported_Kd")]
    [InlineData("absOffTupleCallRests", "absOffTupleCallRests: kk = 2 Unsupported_Kd")]
    [InlineData("absOffTupleNoWriteOk", "")]
    [InlineData("absOffGridBad", "absOffGridBad: kk = 2")]
    [InlineData("absLengthRests", "absLengthRests: kk = 2 Unsupported_Kd")]
    [InlineData("absLengthNoWriteOk", "")]
    [InlineData("absZeroExprRests", "absZeroExprRests: kk = 2 Unsupported_Kd")]
    [InlineData("absTupleStoreBad", "absTupleStoreBad: kk = 2")]
    [InlineData("absArrayStoreBad", "absArrayStoreBad: kk = 2")]
    [InlineData("absArrayCallBad", "absArrayCallBad: kk = 2")]
    [InlineData("absFieldStoreBad", "absFieldStoreBad: kk = 2")]
    [InlineData("absFieldCallBad", "absFieldCallBad: kk = 2")]
    [InlineData("absWholeStructOk", "")]
    [InlineData("absUniqBorrowBad", "absUniqBorrowBad: kk = 2")]
    [InlineData("absReadRefOk", "")]
    [InlineData("absReadCopyOk", "")]
    [InlineData("absZeroTakeBackOk", "")]
    [InlineData("absZeroCallOk", "")]
    [InlineData("absScalarHolderOk", "")]
    [InlineData("absBehindLayerOk", "")]
    [InlineData("fnStoreOk", "")]
    [InlineData("fnCallOk", "")]
    public void AScalarWrittenOverARawPointerLeavesItReachingUnownedStorage(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + PunForms[..PunForms.IndexOf("func storePunBad(", StringComparison.Ordinal)] + Form(PunForms, form));
        var problems = Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]).Order(StringComparer.Ordinal);
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", problems)}]");
    }

    // PLAN G667c's named limit (a) on the stated-store forms (StatedForms; the Middle's Q13 rulings): a Scalar written at the whole
    // address of a struct or a Tuple that holds a raw pointer is undecided, IMPL 21.1.2 promising no offset there, so these invalid
    // forms, the conflict before G667c, are rejected by the located Unsupported_Kd at the conflict's two subjects, until Layout's byte
    // positions decide them.
    [Theory]
    [InlineData("fieldPunBad")]
    [InlineData("tuplePunnedBad")]
    [InlineData("tupleCopiedPunBad")]
    public void AScalarOverAStatedLayersHolderRestsOnItsLayout(string form)
    {
        var c = MirBuildTest.Compile(Prelude + StatedForms[..StatedForms.IndexOf("func endOk(", StringComparison.Ordinal)] + Form(StatedForms, form));
        Assert.Equal(
            [$"{form}: kk = 2 Unsupported_Kd", $"{form}: var spare: raw/u8 = r@follow@raw Unsupported_Kd"],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]).Order(StringComparer.Ordinal));
    }

    // V16 (DECISIONS R4-M140, C-189's readers): after a rebind, the pun rule's key memo, its scans and its walks past a call's first step
    // run again over a store pun, a walk past a holder, an undecided write, a stated layer and a no-store input, and allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ThePunRuleAllocatesNothingWarm()
    {
        var head = PunForms[..PunForms.IndexOf("func storePunBad(", StringComparison.Ordinal)];
        var c = MirBuildTest.Compile(Prelude + head + Form(PunForms, "storePunBad") + Form(PunForms, "pastPunBad") + Form(PunForms, "arrayMidBad") +
            Form(PunForms, "statedOk") + Form(PunForms, "sliceReadOk"));
        var stratum = new Stratum(c.Types);
        stratum.Run(c.BuildMir());
        MirProgram? program = null;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            stratum.Run(program = c.BuildMir());
        }));
        Assert.Contains("pastPunBad: kk = 2", Problems(c, program!, LoanCodes));
    }
}
