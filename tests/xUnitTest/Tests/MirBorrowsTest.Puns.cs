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
        func keepPair(t: uniq/(u64, raw/(raw/u8)), value: raw/u8) => ()
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
        enum PunE
            Ptr(raw/(raw/u8))
            Num(u64)
        struct OBoxP
            public var v: u64
            public init(v: u64) => self.v = v
        #Layout("C")
        struct NodeO
            public var link: Option<obj/OBoxP>
            public var next: raw/(raw/u8)
            public init()
                self.link = .Some(OBoxP.init(v: 0)@obj)
                self.next = null
        struct NodeKO
            public var link: Option<obj/OBoxP>
            public var next: raw/(raw/u8)
            public init()
                self.link = .Some(OBoxP.init(v: 0)@obj)
                self.next = null
        #Layout("C")
        struct NodeOO
            public var link: Option<Option<obj/OBoxP>>
            public var next: raw/(raw/u8)
            public init()
                self.link = .Some(.Some(OBoxP.init(v: 0)@obj))
                self.next = null
        enum OE
            Has(obj/OBoxP)
            Nil
        #Layout("C")
        struct NodeE
            public var link: OE
            public var next: raw/(raw/u8)
            public init()
                self.link = .Has(OBoxP.init(v: 0)@obj)
                self.next = null
        #Layout("C")
        struct NodeH
            public var h: obj/OBoxP
            public var next: raw/(raw/u8)
            public init()
                self.h = OBoxP.init(v: 0)@obj
                self.next = null
        func setV(h: raw/(obj/OBoxP))
            unsafe => (*h).v = 7
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
        func volatileReadOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            unsafe => _ = Raw.readVolatile(q@raw@raw/usize)
            stash(q, spare)
            kk = 2
        func volatileWriteBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var q: raw/(raw/u8) = other@raw
            unsafe => Raw.writeVolatile(q@raw@raw/usize, heap()@usize)
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
        func closureMixedRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = other@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            let tp = c@raw@raw/u16
            unsafe => *tp = 7
            c(spare)
            kk = 2
        func closureMixedNoWriteOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = other@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            c(spare)
            kk = 2
        func closureMixedLiveBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = other@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            let tp = c@raw@raw/u16
            unsafe => *tp = 7
            c(spare)
            kk = 2
            usep(other)
        func closurePtrBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            let q: raw/(raw/u8) = other@raw
            var c = func [q] (v: raw/u8)
                unsafe => *q = v
            let tp = c@raw@raw/u16
            unsafe => *tp = 7
            c(spare)
            kk = 2
        func borrowedTupleRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (u64, raw/(raw/u8)) = (3, other@raw)
            let tp = h@raw@raw/u16
            unsafe => *tp = 7
            keepPair(h@uniq, spare)
            kk = 2
        func selfStoreRests()
            var other: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = other@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            let tp = c@raw@raw/u16
            unsafe => *tp = 7
            var nothing: raw/u8 = null
            c(nothing)
            let r = other@uniq
            other = r@follow@raw@raw/u8
        func selfStoreEscBad(slot: raw/(raw/(raw/u8)))
            var other: raw/u8 = null
            unsafe => *slot = other@raw
            let r = other@uniq
            other = r@follow@raw@raw/u8
        func selfStoreNoEscOk()
            var other: raw/u8 = null
            let r = other@uniq
            other = r@follow@raw@raw/u8
        func selfStoreNoWriteOk()
            var other: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = other@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            var nothing: raw/u8 = null
            c(nothing)
            let r = other@uniq
            other = r@follow@raw@raw/u8
        func viasRestedFirstBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var cc: raw/u8 = null
            var aa: raw/u8 = null
            var bb: raw/u8 = null
            var p: raw/(raw/u8) = aa@raw
            if flag == 1
                p = bb@raw
            if flag == 2
                p = cc@raw
            unsafe => *p = spare
            var m: (raw/(raw/u8), raw/(raw/u8)) = (aa@raw, bb@raw)
            var h: (u64, raw/(raw/(raw/u8), raw/(raw/u8))) = (0, m@raw)
            let tp = h@raw@raw/u16
            unsafe => *tp = 7
            let hp = h.1
            var x: (raw/(raw/u8), raw/(raw/u8)) = (null, null)
            unsafe => x = *hp
            kk = 2
            usep(cc)
            usep(x.0)
        func viasDecidedFirstBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var aa: raw/u8 = null
            var bb: raw/u8 = null
            var m: (raw/(raw/u8), raw/(raw/u8)) = (aa@raw, bb@raw)
            var h: (u64, raw/(raw/(raw/u8), raw/(raw/u8))) = (0, m@raw)
            let tp = h@raw@raw/u16
            unsafe => *tp = 7
            let hp = h.1
            var x: (raw/(raw/u8), raw/(raw/u8)) = (null, null)
            unsafe => x = *hp
            var cc: raw/u8 = null
            var p: raw/(raw/u8) = aa@raw
            if flag == 1
                p = bb@raw
            if flag == 2
                p = cc@raw
            unsafe => *p = spare
            kk = 2
            usep(cc)
            usep(x.0)
        func ownRestedViasBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var cc: raw/u8 = null
            var w1: raw/u8 = null
            var w2: raw/u8 = null
            var g1: (u64, raw/(raw/u8)) = (0, w1@raw)
            var g2: (u64, raw/(raw/u8)) = (0, w2@raw)
            let t1 = g1@raw@raw/u16
            let t2 = g2@raw@raw/u16
            unsafe
                *t1 = 7
                *t2 = 7
            let gp1 = g1.1
            let gp2 = g2.1
            var aa: raw/u8 = null
            var bb: raw/u8 = null
            unsafe
                aa = *gp1
                bb = *gp2
            var p: raw/(raw/u8) = aa@raw
            if flag == 1
                p = bb@raw
            if flag == 2
                p = cc@raw
            unsafe => *p = spare
            kk = 2
            usep(cc)
            usep(aa)
            usep(bb)
        func ownRestedCcLastBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var w1: raw/u8 = null
            var w2: raw/u8 = null
            var g1: (u64, raw/(raw/u8)) = (0, w1@raw)
            var g2: (u64, raw/(raw/u8)) = (0, w2@raw)
            let t1 = g1@raw@raw/u16
            let t2 = g2@raw@raw/u16
            unsafe
                *t1 = 7
                *t2 = 7
            let gp1 = g1.1
            let gp2 = g2.1
            var aa: raw/u8 = null
            var bb: raw/u8 = null
            unsafe
                aa = *gp1
                bb = *gp2
            var cc: raw/u8 = null
            var p: raw/(raw/u8) = aa@raw
            if flag == 1
                p = bb@raw
            if flag == 2
                p = cc@raw
            unsafe => *p = spare
            kk = 2
            usep(cc)
            usep(aa)
            usep(bb)
        func cutHolderFirstBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var hh: raw/u8 = spare
            var aa: raw/u8 = null
            var bb: raw/u8 = null
            var w: raw/u8 = null
            var g: (u64, raw/(raw/u8)) = (0, w@raw)
            let tg = g@raw@raw/u16
            unsafe => *tg = 7
            let gp = g.1
            var vU: raw/u8 = null
            unsafe => vU = *gp
            var p: raw/(raw/u8) = aa@raw
            if flag == 1
                p = bb@raw
            if flag == 2
                p = hh@raw
            unsafe => *p = vU
            kk = 2
            usep(hh)
            usep(aa)
            usep(bb)
        func cutHolderLastBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var aa: raw/u8 = null
            var bb: raw/u8 = null
            var hh: raw/u8 = spare
            var w: raw/u8 = null
            var g: (u64, raw/(raw/u8)) = (0, w@raw)
            let tg = g@raw@raw/u16
            unsafe => *tg = 7
            let gp = g.1
            var vU: raw/u8 = null
            unsafe => vU = *gp
            var p: raw/(raw/u8) = aa@raw
            if flag == 1
                p = bb@raw
            if flag == 2
                p = hh@raw
            unsafe => *p = vU
            kk = 2
            usep(hh)
            usep(aa)
            usep(bb)
        func cutNoWriteBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var hh: raw/u8 = spare
            var aa: raw/u8 = null
            var bb: raw/u8 = null
            var w: raw/u8 = null
            var g: (u64, raw/(raw/u8)) = (0, w@raw)
            let gp = g.1
            var vU: raw/u8 = null
            unsafe => vU = *gp
            var p: raw/(raw/u8) = aa@raw
            if flag == 1
                p = bb@raw
            if flag == 2
                p = hh@raw
            unsafe => *p = vU
            kk = 2
            usep(hh)
            usep(aa)
            usep(bb)
        func cutTwoViasBad(flag: u8)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var hh: raw/u8 = spare
            var aa: raw/u8 = null
            var w: raw/u8 = null
            var g: (u64, raw/(raw/u8)) = (0, w@raw)
            let tg = g@raw@raw/u16
            unsafe => *tg = 7
            let gp = g.1
            var vU: raw/u8 = null
            unsafe => vU = *gp
            var p: raw/(raw/u8) = aa@raw
            if flag == 2
                p = hh@raw
            unsafe => *p = vU
            kk = 2
            usep(hh)
            usep(aa)
        func selfStoreLiveBad()
            var other: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = other@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            let tp = c@raw@raw/u16
            unsafe => *tp = 7
            var nothing: raw/u8 = null
            c(nothing)
            let r = other@uniq
            other = r@follow@raw@raw/u8
            usep(other)
        func tookPerTierBad(flag: bool)
            var a: raw/u8 = null
            var b: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = a@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            let tp = c@raw@raw/u16
            unsafe => *tp = 7
            var nothing: raw/u8 = null
            c(nothing)
            var p: raw/(raw/u8) = b@raw
            if flag
                p = a@raw
            let rp = p@uniq
            unsafe => *p = rp@follow@raw@raw/u8
            usep(b)
        func tookPerTierSwapBad(flag: bool)
            var b: raw/u8 = null
            var a: raw/u8 = null
            let n: u64 = 3
            let q: raw/(raw/u8) = a@raw
            var c = func [n, q] (v: raw/u8)
                unsafe => *q = v
            let tp = c@raw@raw/u16
            unsafe => *tp = 7
            var nothing: raw/u8 = null
            c(nothing)
            var p: raw/(raw/u8) = b@raw
            if flag
                p = a@raw
            let rp = p@uniq
            unsafe => *p = rp@follow@raw@raw/u8
            usep(b)
        func escBesideRestedBad(slot: raw/(raw/(raw/u8)))
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = spare
            unsafe => *slot = other@raw
            var w: raw/u8 = null
            var h: (u64, raw/(raw/u8)) = (0, w@raw)
            let tp = h@raw@raw/u32
            unsafe => *tp = 7
            var nothing: raw/u8 = null
            stash(h.1, nothing)
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
        func optCastObjTyped()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeO.init()
            n.next = other@raw
            let hp = n@raw@raw/(obj/OBoxP)
            unsafe => (*hp).v = 7
            stash(n.next, spare)
            kk = 2
        func optMatchObjTyped()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeO.init()
            n.next = other@raw
            let p = n@raw
            unsafe
                match ((*p).link)@uniq
                    .Some(let b) => b.v = 7
                    .None => ()
            stash(n.next, spare)
            kk = 2
        func kOptCastRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeKO.init()
            n.next = other@raw
            let hp = n@raw@raw/(obj/OBoxP)
            unsafe => (*hp).v = 7
            stash(n.next, spare)
            kk = 2
        func kOptMatchRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeKO.init()
            n.next = other@raw
            let p = n@raw
            unsafe
                match ((*p).link)@uniq
                    .Some(let b) => b.v = 7
                    .None => ()
            stash(n.next, spare)
            kk = 2
        func optOptCastRests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeOO.init()
            n.next = other@raw
            let hp = n@raw@raw/(obj/OBoxP)
            unsafe => (*hp).v = 7
            stash(n.next, spare)
            kk = 2
        func sharedOptTyped(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeO.init()
            n.next = other@raw
            let p = n@raw
            if flag
                unsafe
                    match (*p).link
                        .Some(let b) => (*(b@follow@raw)).v = 7
                        .None => ()
            stash(n.next, spare)
            kk = 2
        func sharedOptNoWrite(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeO.init()
            n.next = other@raw
            let p = n@raw
            if flag
                unsafe
                    match (*p).link
                        .Some(let b) => inspect(b)
                        .None => ()
            stash(n.next, spare)
            kk = 2
        func sharedOptCallTyped(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeO.init()
            n.next = other@raw
            let p = n@raw
            if flag
                unsafe
                    match (*p).link
                        .Some(let b) => setV(b@follow@raw)
                        .None => ()
            stash(n.next, spare)
            kk = 2
        func sharedKOptRests(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeKO.init()
            n.next = other@raw
            let p = n@raw
            if flag
                unsafe
                    match (*p).link
                        .Some(let b) => (*(b@follow@raw)).v = 7
                        .None => ()
            stash(n.next, spare)
            kk = 2
        func sharedEnumRests(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeE.init()
            n.next = other@raw
            let p = n@raw
            if flag
                unsafe
                    match (*p).link
                        .Has(let b) => (*(b@follow@raw)).v = 7
                        .Nil => ()
            stash(n.next, spare)
            kk = 2
        func sharedOptOptRests(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeOO.init()
            n.next = other@raw
            let p = n@raw
            if flag
                unsafe
                    match (*p).link
                        .Some(.Some(let b)) => (*(b@follow@raw)).v = 7
                        _ => ()
            stash(n.next, spare)
            kk = 2
        func hOptMatch(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeH.init()
            n.next = other@raw
            let p = n@raw@raw/(Option<obj/OBoxP>)
            if flag
                unsafe
                    match (*p)@uniq
                        .Some(let b) => b.v = 7
                        .None => ()
            stash(n.next, spare)
            kk = 2
        func hEnumMatch(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var n = NodeH.init()
            n.next = other@raw
            let p = n@raw@raw/OE
            if flag
                unsafe
                    match (*p)@uniq
                        .Has(let b) => b.v = 7
                        .Nil => ()
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
        func absOnTupleCopyRests<T>(v: T)
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
        func absOnTupleCallRests<T>(v: T)
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
        func absWideArrayRests<T>(v: T)
            T is Copy
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let tp = h@raw@raw/[32 of T]
            unsafe => *tp = *tp
            stash(h.0, spare)
            kk = 2
        func absWideArrayU8Rests()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let tp = h@raw@raw/[32 of u8]
            unsafe => *tp = *tp
            stash(h.0, spare)
            kk = 2
        func absArray13Rests<T>(v: T)
            T is Copy
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (u64, u64, raw/(raw/u8)) = (0, 0, other@raw)
            let tp = h@raw@raw/[13 of T]
            unsafe => *tp = *tp
            stash(h.2, spare)
            kk = 2
        func absEnumNumRests<T>(v: T, flag: bool)
            T is Copy
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var e: PunE = .Ptr(other@raw)
            if flag
                e = .Num(5)
                let tp = e@raw@raw/T
                unsafe => *tp = *tp
            match e
                .Ptr(let q) => stash(q@follow, spare)
                .Num(let n) => ()
            kk = 2
        func absEnumNumU16Rests(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var e: PunE = .Ptr(other@raw)
            if flag
                e = .Num(5)
                let tq = e@raw@raw/u16
                unsafe => *tq = *tq
            match e
                .Ptr(let q) => stash(q@follow, spare)
                .Num(let n) => ()
            kk = 2
        func absEnumNoWriteOk(flag: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var e: PunE = .Ptr(other@raw)
            if flag
                e = .Num(5)
            match e
                .Ptr(let q) => stash(q@follow, spare)
                .Num(let n) => ()
            kk = 2
        func absEnumPtrRests<T>(v: T)
            T is Copy
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var e: PunE = .Ptr(other@raw)
            let tp = e@raw@raw/T
            unsafe => *tp = *tp
            match e
                .Ptr(let q) => stash(q@follow, spare)
                .Num(let n) => ()
            kk = 2
        func absDropThenCallRests<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let tp = h@raw@raw/T
            unsafe => _ = (*tp)@move
            usep(tp)
            stash(h.0, spare)
            kk = 2
        func absOtherSourceRests<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var buf: [2 of u64] = [2 of 7]
            let bq = buf@raw@raw/T
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let tp = h@raw@raw/T
            unsafe => *tp = (*bq)@move
            stash(h.0, spare)
            kk = 2
        func absCallThenStoreRests<T>(v: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: raw/u8 = null
            var h: (raw/(raw/u8), u64) = (other@raw, 0)
            let tp = h@raw@raw/T
            usep(tp)
            unsafe => *tp = (*tp)@move
            stash(h.0, spare)
            kk = 2
        """;

    // The body read forms' declarations (ARawPointerABodyReadsPunnedReachesUnownedStorage).
    private const string BodyReadShapes = """
        func usep<T>(p: raw/T) => ()
        func pass<T>(p: ref/T) -> ref/T during p => p
        func stash(slot: raw/(raw/u8), value: raw/u8) => ()
        func at<T>(p: raw/u8) -> raw/T => p@raw/T
        enum PunE
            Ptr(raw/(raw/u8))
            Num(u64)
        struct Node
            public var v: i32
            public init(v: i32) => self.v = v
        #Layout("C")
        struct PS
            public var p: raw/u16
            public var n: u64
            public init()
                self.p = null
                self.n = 7

        """;

    // The partly stated holder forms (ACallsWriteAtPartlyStatedLayersTakesItsOwnVerdict): holders with a raw layer stating `a` beside one
    // stating no Origin (Mixed, MixedQ, Deep), `static` (MixedS) or an abstract part (MixedT), Pin's every layer stated; C-layout ones whose
    // first Field the layout places a store at (CMixedT, CMixedQ: keep, not a zero-sized Loan, keeps `a` from being Phantom).
    private const string CarriedForms = """
        struct Mixed {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public var p: raw/(raw/u8) during a
            public var q: raw/(raw/u8)
            public init(target: uniq/(raw/u8) during a, q: raw/(raw/u8))
                self.p = target@follow@raw
                self.q = q
                self.loan = Loan.init(target)
        struct MixedQ {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public var q: raw/(raw/u8)
            public var p: raw/(raw/u8) during a
            public init(target: uniq/(raw/u8) during a, q: raw/(raw/u8))
                self.q = q
                self.p = target@follow@raw
                self.loan = Loan.init(target)
        struct MixedS {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public var p: raw/(raw/u8) during a
            public var s: raw/(raw/u8) during static
            public init(target: uniq/(raw/u8) during a)
                self.p = target@follow@raw
                self.s = null
                self.loan = Loan.init(target)
        struct MixedT<T> {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public var p: raw/(raw/u8) during a
            public var t: T
            public init(target: uniq/(raw/u8) during a, t: T)
                self.p = target@follow@raw
                self.t = t@move
                self.loan = Loan.init(target)
        struct Pin {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public var p: raw/(raw/u8) during a
            public init(target: uniq/(raw/u8) during a)
                self.p = target@follow@raw
                self.loan = Loan.init(target)
        struct Deep {a}
            let loan: Loan<uniq/(raw/(raw/u8)) during a>
            public var p: raw/(raw/(raw/u8)) during a
            public var q: raw/(raw/u8)
            public init(target: uniq/(raw/(raw/u8)) during a, q: raw/(raw/u8))
                self.p = target@follow@raw
                self.q = q
                self.loan = Loan.init(target)
        struct Carry<T>
            public var w: raw/u8
            public var t: T
            public init(w: raw/u8, t: T)
                self.w = w
                self.t = t@move
        #Layout("C")
        struct CMixedT<T> {a}
            public var p: raw/(raw/u8) during a
            let keep: uniq/(raw/u8) during a
            public var t: T
            public init(target: uniq/(raw/u8) during a, t: T)
                self.p = target@follow@raw
                self.keep = target@move
                self.t = t@move
        #Layout("C")
        struct CMixedQ {a}
            public var q: raw/(raw/u8)
            public var p: raw/(raw/u8) during a
            let keep: uniq/(raw/u8) during a
            public init(target: uniq/(raw/u8) during a, q: raw/(raw/u8))
                self.p = target@follow@raw
                self.keep = target@move
                self.q = q
        func stash(slot: raw/(raw/u8), value: raw/u8) => ()
        func f<T>(t: uniq/T) => ()
        func take<U>(c: uniq/Carry<U>) => ()
        func viaCarriedMixedPOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var other: raw/u8 = null
            var mx = Mixed.init(target: slot@uniq, q: other@raw)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            stash(mx.p, spare)
            kk = 2
        func viaCarriedMixedBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var other: raw/u8 = null
            var mx = Mixed.init(target: slot@uniq, q: other@raw)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            stash(mx.q, spare)
            kk = 2
        func viaCarriedMixedQFirstPOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var other: raw/u8 = null
            var mx = MixedQ.init(target: slot@uniq, q: other@raw)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            stash(mx.p, spare)
            kk = 2
        func viaCarriedMixedQFirstBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var other: raw/u8 = null
            var mx = MixedQ.init(target: slot@uniq, q: other@raw)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            stash(mx.q, spare)
            kk = 2
        func viaCarriedMixedSPOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var mx = MixedS.init(target: slot@uniq)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            stash(mx.p, spare)
            kk = 2
        func viaCarriedMixedTPOk<T>(x: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var mx = MixedT.init(target: slot@uniq, t: x@move)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            stash(mx.p, spare)
            kk = 2
        func viaCarriedPinOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var pin = Pin.init(target: slot@uniq)
            var h: raw/usize = pin@raw@raw/usize
            f(h@uniq)
            stash(pin.p, spare)
            kk = 2
        func viaCarriedMixedReplacedOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var slot2: raw/u8 = null
            var other: raw/u8 = null
            var mx = Mixed.init(target: slot@uniq, q: other@raw)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            mx = Mixed.init(target: slot2@uniq, q: other@raw)
            stash(mx.p, spare)
            kk = 2
        func viaCarriedDeepBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var cell: raw/u8 = null
            var hop: raw/(raw/u8) = cell@raw
            var other: raw/u8 = null
            var mx = Deep.init(target: hop@uniq, q: other@raw)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            unsafe => stash(*mx.p, spare)
            kk = 2
        func viaCarriedMixedQCopyBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var other: raw/u8 = null
            var mx = Mixed.init(target: slot@uniq, q: other@raw)
            var h: raw/usize = mx@raw@raw/usize
            f(h@uniq)
            let w: raw/(raw/u8) = mx.q
            stash(w, spare)
            kk = 2
        func viaParamQBad(x: raw/(raw/u8))
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var mx = Mixed.init(target: slot@uniq, q: x)
            stash(mx.q, spare)
            kk = 2
        func viaParamQPOk(x: raw/(raw/u8))
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var mx = Mixed.init(target: slot@uniq, q: x)
            stash(mx.p, spare)
            kk = 2
        func viaParamQPinOk(x: raw/(raw/u8))
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var pin = Pin.init(target: slot@uniq)
            stash(pin.p, spare)
            kk = 2
        func unknownMixedBad<T>(x: T)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var other: raw/u8 = null
            var mx = Mixed.init(target: slot@uniq, q: other@raw)
            var c = Carry.init(w: mx@raw@raw/u8, t: x@move)
            take(c@uniq)
            stash(mx.p, spare)
            kk = 2
        func placedStatedFirstNoneOk<T>(x: T)
            T is PrimitiveInteger
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var mx = CMixedT<Option<T>>.init(target: slot@uniq, t: .None)
            let w = mx@raw@raw/u8
            unsafe => *w = 0
            stash(mx.p, spare)
            kk = 2
        func placedUnstatedFirstBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var slot: raw/u8 = null
            var other: raw/u8 = null
            var mx = CMixedQ.init(target: slot@uniq, q: other@raw)
            let w = mx@raw@raw/u8
            unsafe => *w = 0
            stash(mx.p, spare)
            kk = 2
        """;

    // Each body read form's statements after its head (BodyRead): kk, its `uniq` borrow r and spare, a raw pointer that holds r's Loan.
    private static readonly Dictionary<string, string> BodyReadForms = new()
    {
        ["uRefBytesBodyBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uUniqBytesBodyBad"] = """
                var y: i32 = 5
                var w: uniq/i32 = y@uniq
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uScalarPartBodyBad"] = """
                var y: i32 = 5
                var w: (u64, ref/i32) = (7@u64, y@ref)
                unsafe
                    let q = *(w.0@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBodyArrBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let q = (*(w@raw@raw/[1 of raw/(raw/u8)]))[0]
                    *q = spare
                kk = 2
            """,
        ["uRefBytesIndexBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let p = w@raw@raw/(raw/(raw/u8))
                unsafe
                    let q = p[0]
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBodySharedBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let rw = w@ref
                unsafe
                    let q = *(rw@follow@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBodyMoveBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let q = (*(w@raw@raw/(raw/(raw/u8))))@move
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBodyWholeArrBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let t = *(w@raw@raw/[1 of raw/(raw/u8)])
                    let q = t[0]
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBorrowBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@ref
                    let q = b@follow
                    *q = spare
                kk = 2
            """,
        ["uRefBytesUniqBorrowBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@uniq
                    let q = b@follow
                    *q = spare
                kk = 2
            """,
        ["uSliceBytesBodyBad"] = """
                let values = [2 of 3@u64]
                var sl: Slice<u64> = values[..]
                unsafe
                    let q = *(sl@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uPtrTupleBodyOk"] = """
                var other: raw/u8 = null
                var w: (raw/u16, u64) = (other@raw@raw/u16, 7@u64)
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uFieldViaBorrowBodyOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let q = (rs@follow).1@raw
                unsafe
                    let v = *q
                    *v = spare
                kk = 2
            """,
        ["uHandleBytesBodyBad"] = """
                var h: obj/Node = Node.init(5)@obj
                unsafe
                    let q = *(h@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uScalarBytesBodyBad"] = """
                var w: u64 = 7
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uPtrBytesBodyBad"] = """
                var other: raw/u8 = null
                var w: raw/u16 = other@raw@raw/u16
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
                usep(other)
            """,
        ["uPtrFieldBodyOk"] = """
                var other: raw/u8 = null
                var w = PS.init()
                w.p = other@raw@raw/u16
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uReborrowFieldBad"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let rr = (rs@follow).1@ref
                unsafe
                    let v = rr@follow
                    *v = spare
                kk = 2
                usep(other)
            """,
        ["uRefBytesBodyOk"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = null
                kk = 2
            """,
        ["uScalarPartBodyOk"] = """
                var y: i32 = 5
                var w: (u64, ref/i32) = (7@u64, y@ref)
                unsafe
                    let q = *(w.0@raw@raw/(raw/(raw/u8)))
                    *q = null
                kk = 2
            """,
        ["uPtrBytesBodyOk"] = """
                var other: raw/u8 = null
                var w: raw/u16 = other@raw@raw/u16
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBodyReadOk"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    let v = *q
                    usep(v)
                kk = 2
            """,
        ["uRefBytesBodyTypedOk"] = """
                var other: u8 = 0
                var pw: raw/u8 = other@raw
                unsafe
                    let q = *(pw@raw)
                    *q = 5
                kk = 2
            """,
        ["uRefBytesBorrowOk"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@ref
                    let q = b@follow
                    *q = null
                kk = 2
            """,
        ["uPtrBorrowTypedOk"] = """
                var o: u8 = 0
                var other: raw/u8 = o@raw
                var pw: raw/(raw/u8) = other@raw
                unsafe
                    let b = (*(pw@raw))@ref
                    let q = b@follow
                    *q = spare
                kk = 2
            """,
        ["uReborrowFieldOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let rr = (rs@follow).1@ref
                unsafe
                    let v = rr@follow
                    *v = spare
                kk = 2
            """,
        ["uRefBytesReborrowBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@uniq
                    let b2 = (b@follow)@uniq
                    let q = b2@follow
                    *q = spare
                kk = 2
            """,
        ["uRefBytesReborrow3Bad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@uniq
                    let b2 = (b@follow)@uniq
                    let b3 = (b2@follow)@ref
                    let q = b3@follow
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBorrowCopyBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@ref
                    let c = b
                    let q = c@follow
                    *q = spare
                kk = 2
            """,
        ["uRefBytesBorrowCallBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@ref
                    let c = pass(b)
                    let q = c@follow
                    *q = spare
                kk = 2
            """,
        ["rvBorrowCallReborrowBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@ref
                    let c = pass(b)
                    let d = (c@follow)@ref
                    let q = d@follow
                    *q = spare
                kk = 2
            """,
        ["rvBorrowRelentTwiceBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                unsafe
                    let b = (*(w@raw@raw/(raw/(raw/u8))))@uniq
                    var c = (b@follow)@ref
                    if flag
                        c = (b@follow)@ref
                    let q = c@follow
                    *q = spare
                kk = 2
            """,
        ["rvMatchPunTupleSharedBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                var ws: (ref/i32, ref/i32) = (y@ref, y@ref)
                unsafe
                    match *(ws@raw@raw/((raw/(raw/u8), raw/(raw/u8))))
                        (let q, let q2) => *(q@follow) = spare
                kk = 2
            """,
        ["rvMatchPunTupleUniqBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                var ws: (ref/i32, ref/i32) = (y@ref, y@ref)
                unsafe
                    match (*(ws@raw@raw/((raw/(raw/u8), raw/(raw/u8)))))@uniq
                        (let q, let q2) => *(q@follow) = spare
                kk = 2
            """,
        ["sOriginLetOk"] = """
                var other: raw/u8 = null
                let ro = other@uniq
                var y: i32 = 5
                var w: ref/i32 = y@ref
                var cell: raw/(raw/u8) during ro = null
                unsafe
                    let q = *(w@raw@raw/(raw/(raw/u8)))
                    cell = q@(raw/(raw/u8) during ro)
                    *cell = spare
                kk = 2
            """,
        ["sOriginGenSpareBad"] = """
                var other: raw/u8 = null
                let ro = other@uniq
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let pp = at<raw/(raw/u8) during ro>(w@raw@raw/u8)
                unsafe
                    let q = *pp
                    *q = spare
                kk = 2
            """,
        ["sOriginGenReadOk"] = """
                var other: raw/u8 = null
                let ro = other@uniq
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let pp = at<raw/(raw/u8) during ro>(w@raw@raw/u8)
                unsafe
                    let q = *pp
                    let z = q
                kk = 2
            """,
        ["oqPartRefBodyBad"] = """
                var y: u64 = 5@u64
                let h = (y@ref, 7@u64)
                let p = (h.0)@follow@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["uReborrowChainOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let rr = (rs@follow).1@uniq
                let rr2 = (rr@follow)@ref
                unsafe
                    let v = rr2@follow
                    *v = spare
                kk = 2
            """,
        ["uReborrowChainBad"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let rr = (rs@follow).1@uniq
                let rr2 = (rr@follow)@ref
                unsafe
                    let v = rr2@follow
                    *v = spare
                kk = 2
                usep(other)
            """,
        ["uReborrowFieldCopyOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let rr = (rs@follow).1@ref
                let c = rr
                unsafe
                    let v = c@follow
                    *v = spare
                kk = 2
            """,
        ["uReborrowFieldTwiceOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                var rr = (rs@follow).1@ref
                if flag
                    rr = (rs@follow).1@ref
                unsafe
                    let v = rr@follow
                    *v = spare
                kk = 2
            """,
        ["absEnumUniqNoWriteOk"] = """
                var other: raw/u8 = null
                var e: PunE = .Ptr(other@raw)
                if flag
                    e = .Num(5)
                let m = e@uniq
                match m@follow
                    .Ptr(let q) => stash(q@follow, spare)
                    .Num(let n) => ()
                kk = 2
            """,
        ["absEnumUniqBad"] = """
                var other: raw/u8 = null
                var e: PunE = .Ptr(other@raw)
                if flag
                    e = .Num(5)
                let m = e@uniq
                match m@follow
                    .Ptr(let q) => stash(q@follow, spare)
                    .Num(let n) => ()
                kk = 2
                usep(other)
            """,
        ["uScalarViaBorrowBodyBad"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let q = (rs@follow).0@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *q
                    *v = spare
                kk = 2
            """,
        ["uReborrowFieldCallOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let rr = (rs@follow).1@ref
                let c = pass(rr)
                unsafe
                    let v = c@follow
                    *v = spare
                kk = 2
            """,
        ["rvCallFieldBad"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                unsafe
                    let c = pass((*((rs@follow).0@raw@raw/(raw/(raw/u8))))@ref)
                    let v = c@follow
                    *v = spare
                kk = 2
            """,
        ["rvIndexViaBorrowOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let q = (rs@follow).1@raw
                unsafe
                    let v = q[0]
                    *v = spare
                kk = 2
            """,
        ["rvIndexViaBorrowBad"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let q = (rs@follow).0@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = q[0]
                    *v = spare
                kk = 2
            """,
        ["rvExposedViaBorrowOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                let rs = s@uniq
                let q = (rs@follow).1@raw
                let qa = q@raw
                unsafe
                    let v = *q
                    *v = spare
                kk = 2
            """,
        ["rvBorrowTwiceMixedBad"] = """
                var y: i32 = 5
                var w: ref/i32 = y@ref
                var other: raw/u8 = null
                var x: raw/(raw/u8) = other@raw
                var c = x@ref
                if flag
                    unsafe => c = (*(w@raw@raw/(raw/(raw/u8))))@ref
                unsafe
                    let q = c@follow
                    *q = spare
                kk = 2
            """,
        ["r3MixRefCastOk"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let h = (a@ref, w@ref)
                let p = (h.0)@follow@raw@raw/u8@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r3MixRefCastBad"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let h = (a@ref, w@ref)
                let p = (h.1)@follow@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r3PartRefsCastOk"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var y: u64 = 5@u64
                let h = (a@ref, y@ref)
                let p = (h.0)@follow@raw@raw/u8@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["oqPartRefsBodyBad"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var y: u64 = 5@u64
                let h = (a@ref, y@ref)
                let p = (h.1)@follow@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["oqPartRefsCallOk"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var y: u64 = 5@u64
                let h = (a@ref, y@ref)
                let c = pass(h.0)
                unsafe
                    let v = c@follow
                    *v = spare
                kk = 2
            """,
        ["oqPartRefsCallBad"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var y: u64 = 5@u64
                let h = (a@ref, y@ref)
                unsafe
                    let c = pass((*((h.1)@follow@raw@raw/(raw/(raw/u8))))@ref)
                    let v = c@follow
                    *v = spare
                kk = 2
            """,
        ["r4MixOpenCastOk"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var other2: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other2@raw)
                let h = (a@ref, s@ref)
                let p = (h.0)@follow@raw@raw/u8@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r4MixOpenS1Ok"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var other2: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other2@raw)
                let h = (a@ref, s@ref)
                let p = ((h.1)@follow).1@raw@raw/u8@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r4MixOpenCastBad"] = """
                var other: raw/u8 = null
                var a: raw/(raw/u8) = other@raw
                var other2: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other2@raw)
                let h = (a@ref, s@ref)
                let p = ((h.1)@follow).0@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r3bOpenRefCastOk"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let h = (s@ref, w@ref)
                let p = ((h.0)@follow).1@raw@raw/u8@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r3bOpenRefCastBad"] = """
                var other: raw/u8 = null
                var s: (u64, raw/(raw/u8)) = (7@u64, other@raw)
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let h = (s@ref, w@ref)
                let p = (h.1)@follow@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r5PunPtrRefCastOk"] = """
                var other: raw/u8 = null
                var b: raw/u16 = other@raw@raw/u16
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let h = (b@ref, w@ref)
                let p = (h.0)@follow@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
        ["r5PunPtrRefCastBad"] = """
                var other: raw/u8 = null
                var b: raw/u16 = other@raw@raw/u16
                var y: i32 = 5
                var w: ref/i32 = y@ref
                let h = (b@ref, w@ref)
                let p = (h.1)@follow@raw@raw/(raw/(raw/u8))
                unsafe
                    let v = *p
                    *v = spare
                kk = 2
            """,
    };

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
    // Field's is not decided (kLayoutFieldRests). A first Field `Option<obj/OBoxP>` places its Some payload there too (IMPL 21.1.3, PLAN
    // G808), so a store past it is typed (opt*Typed; undecided before G808), and the init's own conflict stands (SPEC 15.6: the result's
    // raw `next` is tied to no argument); a Kimigayo-layout or `Option<Option<R>>` Field's is not decided (kOpt*Rests, optOptCastRests).
    // A shared Subject's binding of the payload in place (SPEC 15.1.6; its write runs only under `flag`, having no write authority,
    // SPEC 5.2.1 condition 3) keeps that verdict, a store or a call past it typed (sharedOpt*), through an enum that is no nonnull
    // Option, a Kimigayo-layout Field or `Option<Option<R>>` undecided (sharedEnumRests, sharedKOptRests, sharedOptOptRests). A written
    // array of no element over a pointer is no pun (zeroArrayOk, 5.2: positive-sized). A length that is no constant is not established
    // positive (4.4.2: the body is checked for every binding that meets the public conditions, which may all make it 0: `N - N`,
    // `N * 0`, a bare `N` beside a `[(0 - N) of u8]` parameter), so no pun is decided over one, written or the holder's: a located
    // Unsupported_Kd where the conflict rests on it, never a Language error (lengthArrayRests, lengthZeroExprRests, lengthMulZeroRests,
    // lengthZeroCondRests; lengthHolderRests, lengthZeroExprHolderRests). A holder's typed rows need no length: its element's Type
    // through its address is typed (lengthHolderElemOk, 4.1). A holder of no element, at any depth, or a Tuple or an enum whose only
    // pointers are in one (zeroInTupleHolderOk, zeroPayloadBranchOk: bytes held, not Loans carried, 4.3.3), holds no pointer, and a
    // write at its address, seen or a call's, is no pun (zeroHolder*Ok, zeroNestBranchOk, lengthZeroNestOk;
    // 4.1, 5.2; the write is undefined when it runs, which is no compile-time error, 1.3).
    // An abstract written part (PLAN G792, F1; SPEC 8.15.1: every binding, a Scalar of any size among them) is a punnable write that
    // surely holds a Scalar, judged at its Position: over a pointer, a seen store, a call's write, a Field or an exclusive borrow of it
    // is the conflict (absTakeBackBad, absCallBad, absTupleStoreBad, absArrayStoreBad, absArrayCallBad, absFieldStoreBad,
    // absFieldCallBad, absUniqBorrowBad; off the grid of an all-pointer holder, absOffGridBad). Over a Tuple or an enum that holds a
    // pointer but not only pointers, on its address or off it, the layout and the binding's size decide, as for a written Scalar (the
    // owner's O-205 (a)): a located Unsupported_Kd where the conflict rests on it, never a Language error, on invalid forms
    // (absOnTupleCopyRests, absOnTupleCallRests), unsettled ones (absOffTupleRests, absOffTupleCallRests, absArray13Rests,
    // absEnumPtrRests) and valid ones alike, as their concrete controls (absWideArrayRests and absWideArrayU8Rests: a write past the
    // holder's provenance; absEnumNumRests and absEnumNumU16Rests: the Num Case at the store), and at a holder's length that is no
    // constant (limit (c): absLengthRests, absZeroExprRests, the last valid). After a destruction through tp (`_ = (*tp)@move`, or a
    // non-Copy store's drop of the old value) tp's own address is formed, so its Position is not seen formed (Φ_N) and the write rests;
    // the undecided write's Φ_M is the set's (R4-M198's named limit, PLAN G599 (b)), so a decided conflict beside it rests too
    // (absOnTupleRests, absDropThenCallRests, absOtherSourceRests, absCallThenStoreRests). A struct written whole, a Function value, a
    // read and a pointer to T are no pun; a holder with no pointer byte holds none (the abs*Ok rows, fnStoreOk, fnCallOk).
    // A call that writes through a borrowed holder such a write leaves undecided (a Closure's receiver, IMPL 21.1.1 and R4-M148; a
    // `uniq/` Tuple) may retain what reads through it carry, the other pointer's address included, where the punned pointer reaches
    // (5.2.1 Retained dependencies), and stores spare where that pointer points, other's holder, in either layout. Whether other is
    // live at kk = 2 then rests on the write, so the check is a located Unsupported_Kd (closureMixedRests, borrowedTupleRests;
    // closurePtrBad, whose only part every layout puts the write on, is limit (a)'s); where other is used later, the conflict holds in
    // either layout (closureMixedLiveBad) (PLAN G775). Without the write the call is invalid as well (closureMixedNoWriteOk,
    // selfStoreNoWriteOk; A5 value redesign U3c): the `let` capture q's layer is one the Closure states in its own Fields (SPEC 5.7 Calls,
    // the owner's O-254 Q1 D), so through it the call may store what a raw layer it writes retains; `other`, which it writes, is a raw
    // layer without an Origin, so the call may write its own pointer there and store spare, or keep other's address, in storage the body
    // does not own (15.6.4's composition). The same holds
    // for a location's own store into a holder used after it only through such a write: storing into other a pointer carrying a Loan of
    // other and writing other is valid in every layout (5.2.1: a self-link; 5.5), at most the limit, never a Language error
    // (selfStoreRests); with other's address in storage the body does not own it conflicts (selfStoreEscBad). A decided route stays
    // decided whatever the order the gather reads holders in: p may address aa, bb or cc, which then share one stored set, and on the
    // path where it addresses cc, cc holds r's Loan and is used after kk = 2, a route through no undecided write, so kk = 2 conflicts
    // whether aa and bb are read at the Limit tier through a set carrying Φ_U (via*) or by their own states carrying it (ownRested*),
    // and whether cc is declared before them or after (15.6). A holder that holds r's Loan by its own Place, used after kk = 2, whose
    // stored set (shared with aa and bb) carries Φ_U, conflicts in every layout, but its state carries Φ_U, so R4-M198's set-level
    // named limit makes the record the located Unsupported_Kd (PLAN G599 (b)), the same whichever via of that set it is read as
    // (cutHolderFirstBad, cutHolderLastBad, cutTwoViasBad); without the write it is the conflict (cutNoWriteBad). The same limit holds
    // where the set naming the holder carries Φ_U: other holds r's Loan and its address is in storage the body does not own, but E's set
    // also carries an unrelated rested store (escBesideRestedBad). A holder reached both through E's rested content and by a decided
    // route keeps its decided conflict, also where the location stores one value into it and into a holder reached only through E:
    // that value is taken once per tier (selfStoreLiveBad; tookPerTierBad and tookPerTierSwapBad, the same records in both orders, their
    // second record at `var p` resting on E's content).
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
    [InlineData("closureMixedRests", "closureMixedRests: kk = 2 Unsupported_Kd")]
    [InlineData("closureMixedNoWriteOk", "closureMixedNoWriteOk: kk = 2")] // Invalid without the pun write (U3c; SPEC 5.7 Calls, the owner's O-254 Q1 D).
    [InlineData("closureMixedLiveBad", "closureMixedLiveBad: kk = 2")]
    [InlineData("closurePtrBad", "closurePtrBad: kk = 2 Unsupported_Kd")]
    [InlineData("borrowedTupleRests", "borrowedTupleRests: kk = 2 Unsupported_Kd")]
    [InlineData("selfStoreRests", "selfStoreRests: other = r@follow@raw@raw/u8 Unsupported_Kd")]
    [InlineData("selfStoreEscBad", "selfStoreEscBad: other = r@follow@raw@raw/u8")]
    [InlineData("selfStoreNoEscOk", "")]
    [InlineData("selfStoreNoWriteOk", "selfStoreNoWriteOk: other = r@follow@raw@raw/u8")] // Invalid without the pun write (U3c; as closureMixedNoWriteOk).
    [InlineData("viasRestedFirstBad", "viasRestedFirstBad: kk = 2")]
    [InlineData("viasDecidedFirstBad", "viasDecidedFirstBad: kk = 2")]
    [InlineData("ownRestedViasBad", "ownRestedViasBad: kk = 2")]
    [InlineData("ownRestedCcLastBad", "ownRestedCcLastBad: kk = 2")]
    [InlineData("cutHolderFirstBad", "cutHolderFirstBad: kk = 2 Unsupported_Kd")]
    [InlineData("cutHolderLastBad", "cutHolderLastBad: kk = 2 Unsupported_Kd")]
    [InlineData("cutNoWriteBad", "cutNoWriteBad: kk = 2")]
    [InlineData("cutTwoViasBad", "cutTwoViasBad: kk = 2 Unsupported_Kd")]
    [InlineData("selfStoreLiveBad", "selfStoreLiveBad: other = r@follow@raw@raw/u8")]
    [InlineData("tookPerTierBad", "tookPerTierBad: *p = rp@follow@raw@raw/u8; tookPerTierBad: var p: raw/(raw/u8) = b@raw Unsupported_Kd")]
    [InlineData("tookPerTierSwapBad", "tookPerTierSwapBad: *p = rp@follow@raw@raw/u8; tookPerTierSwapBad: var p: raw/(raw/u8) = b@raw Unsupported_Kd")]
    [InlineData("escBesideRestedBad", "escBesideRestedBad: kk = 2 Unsupported_Kd")]
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
    [InlineData("optCastObjTyped", "optCastObjTyped: kk = 2")]
    [InlineData("optMatchObjTyped", "optMatchObjTyped: kk = 2")]
    [InlineData("kOptCastRests", "kOptCastRests: kk = 2 Unsupported_Kd")]
    [InlineData("kOptMatchRests", "kOptMatchRests: kk = 2 Unsupported_Kd")]
    [InlineData("optOptCastRests", "optOptCastRests: kk = 2 Unsupported_Kd")]
    [InlineData("sharedOptTyped", "sharedOptTyped: kk = 2")]
    [InlineData("sharedOptNoWrite", "sharedOptNoWrite: kk = 2")]
    [InlineData("sharedOptCallTyped", "sharedOptCallTyped: kk = 2")]
    [InlineData("sharedKOptRests", "sharedKOptRests: kk = 2 Unsupported_Kd")]
    [InlineData("sharedEnumRests", "sharedEnumRests: kk = 2 Unsupported_Kd")]
    [InlineData("sharedOptOptRests", "sharedOptOptRests: kk = 2 Unsupported_Kd")]
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
    [InlineData("volatileReadOk", "")] // SPEC 5.6: Raw.readVolatile stores nothing through its storage (its NoStore row).
    [InlineData("volatileWriteBad", "volatileWriteBad: kk = 2")] // Raw.writeVolatile stores its integer there, a pun.
    [InlineData("absTakeBackBad", "absTakeBackBad: kk = 2")]
    [InlineData("absCallBad", "absCallBad: kk = 2")]
    [InlineData("absOnTupleRests", "absOnTupleRests: kk = 2 Unsupported_Kd")]
    [InlineData("absOnTupleCopyRests", "absOnTupleCopyRests: kk = 2 Unsupported_Kd")]
    [InlineData("absOnTupleCallRests", "absOnTupleCallRests: kk = 2 Unsupported_Kd")]
    [InlineData("absWideArrayRests", "absWideArrayRests: kk = 2 Unsupported_Kd")]
    [InlineData("absWideArrayU8Rests", "absWideArrayU8Rests: kk = 2 Unsupported_Kd")]
    [InlineData("absArray13Rests", "absArray13Rests: kk = 2 Unsupported_Kd")]
    [InlineData("absEnumNumRests", "absEnumNumRests: kk = 2 Unsupported_Kd")]
    [InlineData("absEnumNumU16Rests", "absEnumNumU16Rests: kk = 2 Unsupported_Kd")]
    [InlineData("absEnumNoWriteOk", "")]
    [InlineData("absEnumPtrRests", "absEnumPtrRests: kk = 2 Unsupported_Kd")]
    [InlineData("absDropThenCallRests", "absDropThenCallRests: kk = 2 Unsupported_Kd")]
    [InlineData("absOtherSourceRests", "absOtherSourceRests: kk = 2 Unsupported_Kd")]
    [InlineData("absCallThenStoreRests", "absCallThenStoreRests: kk = 2 Unsupported_Kd")]
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

    // SPEC 5.2, 5.2.1 condition 5, 5.6 (C-457 R-c; OSF2's row 27 premise): C-457's peek, a volatile read of a ref's bits through a raw
    // pointer to it and then a follow of the ref through that pointer, is accepted. It does not observe Raw.readVolatile's NoStore row
    // (it is accepted without the row, as is its writeVolatile twin); volatileReadOk and volatileWriteBad are the rows that discriminate.
    [Fact]
    public void AVolatilePeekAtARefsBitsBeforeItsFollowIsAccepted()
    {
        var c = MirBuildTest.Compile(Prelude + "func peek(r: ref/i32) -> i32\n    var held: ref/i32 = r\n    var cell: raw/(ref/i32) = held@raw\n" +
            "    unsafe\n        let bits = Raw.readVolatile(cell@raw@raw/u64)\n        return (*cell)@follow\n");
        Assert.Empty(c.Binding.Issues);
        Assert.Empty(Problems(c, Analyze(c), Enum.GetValues<DiagnosticCode>()));
    }

    // SPEC 5.2 (PLAN G806; the owner's O-220 (a), O-231 (a)): a whole-value write writes the written Type's raw Places (TyCtxt.RawPlaces).
    // A UniqSlice<T>'s stored Fields are raw Places, so a UniqSlice written over the raw pointer `q` holds writes `length`, a Scalar, there
    // (uSliceBad4, rejected since G806; uSliceBad3 beside its own Origin record). A struct whose storage Binding cannot state whole, at
    // Binding's Limit (Nest<u64>) or with an unbound Field (US), is undecided and keeps its published rows, so its Scalar Field still puns
    // beside the other part's own record. Each Ok twin writes over a Tuple holding no raw pointer.
    [Theory]
    [InlineData("uSliceBad4", "src: uniq/[4 of u8] during a", "UniqSlice<u8> during a", "", "uSliceBad4: kk = 2")]
    [InlineData("uSliceOk4", "src: uniq/[4 of u8] during a", "UniqSlice<u8> during a", "", "")]
    [InlineData("uSliceBad3", "", "UniqSlice<u8> during static", "InvalidOriginBinding_Kd", "uSliceBad3: kk = 2")]
    [InlineData("uSliceOk3", "", "UniqSlice<u8> during static", "InvalidOriginBinding_Kd", "")]
    [InlineData("nNestBad", "", "Nest<u64>", "", "nNestBad: kk = 2; nNestBad: viaW(q@raw@raw/(Nest<u64>)) OwnershipDepthLimit_Kd")]
    [InlineData("nNestOk", "", "Nest<u64>", "", "nNestOk: viaW(w@raw@raw/(Nest<u64>)) OwnershipDepthLimit_Kd")]
    [InlineData("bUnboundFieldBad", "", "US", "UnresolvedBinding_Kd", "bUnboundFieldBad: kk = 2")]
    [InlineData("bUnboundFieldOk", "", "US", "UnresolvedBinding_Kd", "")]
    public void AWholeWriteWritesTheWrittenTypesRawPlaces(string form, string parameters, string written, string bound, string expected)
    {
        var over = form.Contains("Bad", StringComparison.Ordinal) ? "q" : "w";
        var c = MirBuildTest.Compile(Prelude + PunForms[..PunForms.IndexOf("func storePunBad(", StringComparison.Ordinal)] +
            (written == "US" ? "struct US\n    public var n: u64\n    public var bad: NoSuchType\n" : string.Empty) +
            $"func viaW(slot: raw/({written.Split(" during ")[0]})) => ()\nfunc {form}({parameters})\n    var kk: u8 = 1\n    let r = kk@uniq\n" +
            "    var spare: raw/u8 = r@follow@raw\n    var other: raw/u8 = null\n    var q: raw/(raw/u8) = other@raw\n" +
            (over == "w" ? "    var w: (u64, u64, u64) = (0, 0, 0)\n" : string.Empty) +
            $"    viaW({over}@raw@raw/({written}))\n    stash(q, spare)\n    kk = 2\n");
        Assert.Equal(bound, string.Join("; ", c.Binding.Issues.Select(static x => $"{x.Code}").Distinct().Order(StringComparer.Ordinal)));
        var program = c.BuildMir();
        new Stratum(c.Types).Run(program);
        var problems = Problems(c, program, [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnershipDepthLimit_Kd]);
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", problems.Order(StringComparer.Ordinal))}]");
    }

    // The step rule (Step; G808 design review N4, the A5 value redesign's U2): a nonnull Option's Some payload is TyCtxt.Leading's one
    // step from the Option, its Downcast and Field, also where a Place's own walk takes them past a raw Deref. No source form builds that
    // Place (a match binding borrows through its Subject Place's Deref, `&((*_s) as case k).j`), so each shared-Subject form's body is
    // copied with its Subject's Deref replaced by the Place the Subject borrows, `&(((*p).link) as case k).j`: a store past the payload
    // keeps its source form's verdict, typed through `Option<obj/OBoxP>` at a C first Field (sharedOptTyped: undecided while the
    // Downcast kept no Position), undecided through an enum that is no nonnull Option, a Kimigayo-layout Field or `Option<Option<R>>`.
    [Theory]
    [InlineData("sharedOptTyped", "sharedOptTyped: kk = 2")]
    [InlineData("sharedOptCallTyped", "sharedOptCallTyped: kk = 2")]
    [InlineData("sharedEnumRests", "sharedEnumRests: kk = 2 Unsupported_Kd")]
    [InlineData("sharedKOptRests", "sharedKOptRests: kk = 2 Unsupported_Kd")]
    [InlineData("sharedOptOptRests", "sharedOptOptRests: kk = 2 Unsupported_Kd")]
    public void APayloadBorrowedInPlacePastARawDerefKeepsLeadingsStep(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + PunForms[..PunForms.IndexOf("func storePunBad(", StringComparison.Ordinal)] + Form(PunForms, form));
        var program = c.BuildMir();
        program.Problems.Clear();
        var built = Body(program, form);
        new Stratum(c.Types).Run(program, Validated(c, InPlace(c.Types, built, Binding(built).Rvalue.Place.Root)));
        var problems = Problems(c, Sound(program), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]).Order(StringComparer.Ordinal);
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", problems)}]");
    }

    // The step rule's other reader (StoreWrite; U2's implementation review, U2-N1): a seen store whose own Place passes the pair past its
    // last Deref. No source form stores through that Place (a binding's store goes through the binding's reference), so each uniq-Subject
    // form's body is copied with its Subject and then its binding rebased onto the Place each borrows (InPlace): the store `b.v = 7` is
    // `(*(((*p) as case 0).0)).v = 7`, typed through `Option<obj/OBoxP>` cast at a C first Field `obj/OBoxP` (hOptMatch: undecided while the
    // Downcast kept no Position), undecided through an enum that is no nonnull Option (hEnumMatch: A5's Limit; the write past the handle's
    // own step is the object's, SPEC 5.2). The binding's Ref stays, a borrow no store goes through, so each verdict is the store's.
    [Theory]
    [InlineData("hOptMatch", "hOptMatch: kk = 2")]
    [InlineData("hEnumMatch", "hEnumMatch: kk = 2 Unsupported_Kd")]
    public void APayloadStoredInPlacePastARawDerefKeepsLeadingsStep(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + PunForms[..PunForms.IndexOf("func storePunBad(", StringComparison.Ordinal)] + Form(PunForms, form));
        var program = c.BuildMir();
        program.Problems.Clear();
        var built = Body(program, form);
        var subject = InPlace(c.Types, built, Binding(built).Rvalue.Place.Root);
        new Stratum(c.Types).Run(program, Validated(c, InPlace(c.Types, subject, Binding(subject).Place.Root)));
        var problems = Problems(c, Sound(program), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]).Order(StringComparer.Ordinal);
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

    // PLAN G785, the pun rule's read site (SPEC 15.6 Holders, example punnedRef; 5.2; 5.5): a raw pointer a body reads punned over bytes
    // no raw pointer wrote reaches storage the body does not own, whatever else the holder carries. Each Bad row stores spare through
    // such a pointer and uses r after (kk = 2): over a reference's bytes (5.5), a `uniq` one's, an `owner` Scalar part's beside a Loan,
    // an element of a punned fixed array, `p[0]`, a Take, the whole array then its element, past a shared layer (the body's store has no
    // shared exception: 15.6), through a raw Place's borrow (5.2: "a read of it, also through its borrow"), its reborrows, its Copy, a
    // call's result of it, a local re-lent twice, a match binding of a punned Tuple, and a claim of an Origin on such a pointer (15.6
    // Holders: a stated Origin bounds no borrow-checking assumption); each accepted before. A Deref whose referent MirBorrows.Lent resolves
    // exactly reads its Place at its own Type (a safe reborrow of a part, its chain, its Copy, a local stored twice). Through a referent
    // Lent does not resolve (a call's result, `p[n]`, an exposed local, a Cast), where the Places the value names hold raw pointer bytes
    // and other bytes, in one holder or across holders where one keeps provenance, or a Place's structure or layout states no bytes, the
    // value cannot tell a typed read from a punned one: the located Unsupported_Kd where the conflict rests on it, never a Language error
    // (the support reduction the owner approved by its rule, O-521 Q1 (a); PLAN G785b). The controls keep their verdicts.
    [Theory]
    [InlineData("uRefBytesBodyBad", "uRefBytesBodyBad: kk = 2")] // I, 15.6's example punnedRef in the body
    [InlineData("uUniqBytesBodyBad", "uUniqBytesBodyBad: kk = 2")] // I, a `uniq` reference's bytes
    [InlineData("uScalarPartBodyBad", "uScalarPartBodyBad: kk = 2")] // I, an `owner` Scalar part of a holder that holds a Loan
    [InlineData("uRefBytesBodyArrBad", "uRefBytesBodyArrBad: kk = 2")] // I, an element of a punned fixed array
    [InlineData("uRefBytesIndexBad", "uRefBytesIndexBad: kk = 2")] // I, `p[0]` (5.3)
    [InlineData("uRefBytesBodySharedBad", "uRefBytesBodySharedBad: kk = 2")] // I, past a shared layer: the body's store has no shared exception
    [InlineData("uRefBytesBodyMoveBad", "uRefBytesBodyMoveBad: kk = 2")] // I, a Take
    [InlineData("uRefBytesBodyWholeArrBad", "uRefBytesBodyWholeArrBad: kk = 2")] // I, the whole punned array, then its element
    [InlineData("uRefBytesBorrowBad", "uRefBytesBorrowBad: kk = 2")] // I, through a raw Place's shared borrow (5.2)
    [InlineData("uRefBytesUniqBorrowBad", "uRefBytesUniqBorrowBad: kk = 2")] // I, through a raw Place's `uniq` borrow
    [InlineData("uSliceBytesBodyBad", "uSliceBytesBodyBad: kk = 2 Unsupported_Kd")] // I, a Slice's bytes are not stated (PlaceBytes Open)
    [InlineData("uPtrTupleBodyOk", "uPtrTupleBodyOk: kk = 2 Unsupported_Kd")] // by layout: w.0's raw pointer (V) or w.1's u64 (I)
    [InlineData("uFieldViaBorrowBodyOk", "")] // V, an exact raw Ref of s.1: its own Type
    [InlineData("uHandleBytesBodyBad", "uHandleBytesBodyBad: kk = 2")] // I, a handle's bytes
    [InlineData("uScalarBytesBodyBad", "uScalarBytesBodyBad: kk = 2")] // I, a lone u64's bytes
    [InlineData("uPtrBytesBodyBad", "uPtrBytesBodyBad: kk = 2")] // I, a raw pointer's bytes: its provenance, other used after
    [InlineData("uPtrFieldBodyOk", "uPtrFieldBodyOk: kk = 2")] // I, PS.init()'s untied raw layer (15.6.4)
    [InlineData("uReborrowFieldBad", "uReborrowFieldBad: kk = 2")] // I, other holds spare and is used after
    [InlineData("uRefBytesBodyOk", "")] // V, nothing stored through q
    [InlineData("uScalarPartBodyOk", "")] // V, as uRefBytesBodyOk
    [InlineData("uPtrBytesBodyOk", "")] // V, a raw pointer's bytes: other, unused after
    [InlineData("uRefBytesBodyReadOk", "")] // V, a read through q, no store
    [InlineData("uRefBytesBodyTypedOk", "")] // V, a typed read
    [InlineData("uRefBytesBorrowOk", "")] // V, as uRefBytesBodyOk through the borrow
    [InlineData("uPtrBorrowTypedOk", "")] // V, a typed raw Place's borrow
    [InlineData("uReborrowFieldOk", "")] // V, a safe reborrow of s.1 at its own Type
    [InlineData("uRefBytesReborrowBad", "uRefBytesReborrowBad: kk = 2")] // I, a reborrow of a raw Place borrow's referent
    [InlineData("uRefBytesReborrow3Bad", "uRefBytesReborrow3Bad: kk = 2")] // I, a chain of three
    [InlineData("uRefBytesBorrowCopyBad", "uRefBytesBorrowCopyBad: kk = 2")] // I, a Copy of the borrow
    [InlineData("uRefBytesBorrowCallBad", "uRefBytesBorrowCallBad: kk = 2")] // I, the borrow through a call's result
    [InlineData("rvBorrowCallReborrowBad", "rvBorrowCallReborrowBad: kk = 2")] // I, a call's result, then reborrowed
    [InlineData("rvBorrowRelentTwiceBad", "rvBorrowRelentTwiceBad: kk = 2")] // I, re-lent by two stores
    [InlineData("rvMatchPunTupleSharedBad", "rvMatchPunTupleSharedBad: kk = 2")] // I, a match binding of a punned Tuple
    [InlineData("rvMatchPunTupleUniqBad", "rvMatchPunTupleUniqBad: kk = 2")] // I, the same through a `uniq` borrow
    [InlineData("sOriginLetOk", "sOriginLetOk: kk = 2")] // I (relabelled after its run), a claim of an Origin on the punned pointer
    [InlineData("sOriginGenSpareBad", "sOriginGenSpareBad: kk = 2")] // I, a punned read at a Type stating an Origin
    [InlineData("oqPartRefBodyBad", "oqPartRefBodyBad: kk = 2")] // I, a non-holder's u64 bytes
    [InlineData("uReborrowChainBad", "uReborrowChainBad: kk = 2")] // I, other holds spare and is used after
    [InlineData("absEnumUniqBad", "absEnumUniqBad: kk = 2")] // I, the same through a match binding
    [InlineData("sOriginGenReadOk", "")] // V, the read at a Type stating an Origin, no store
    [InlineData("uReborrowChainOk", "")] // V, a typed reborrow chain
    [InlineData("uReborrowFieldCopyOk", "")] // V, a Copy of a typed reborrow
    [InlineData("uReborrowFieldTwiceOk", "")] // V, a local stored twice, each store exact
    [InlineData("absEnumUniqNoWriteOk", "")] // V, a match binding of e's payload
    [InlineData("uScalarViaBorrowBodyBad", "uScalarViaBorrowBodyBad: kk = 2 Unsupported_Kd")] // I, s.0 through a Cast past a borrow
    [InlineData("rvCallFieldBad", "rvCallFieldBad: kk = 2 Unsupported_Kd")] // I, s.0's u64 through a call's result
    [InlineData("rvIndexViaBorrowBad", "rvIndexViaBorrowBad: kk = 2 Unsupported_Kd")] // I, s.0's u64 through `q[0]`
    [InlineData("rvBorrowTwiceMixedBad", "rvBorrowTwiceMixedBad: kk = 2 Unsupported_Kd")] // I, x's raw pointer or w's reference bytes
    [InlineData("oqPartRefsBodyBad", "oqPartRefsBodyBad: kk = 2 Unsupported_Kd")] // I, y's u64 beside a, by a Cast
    [InlineData("oqPartRefsCallBad", "oqPartRefsCallBad: kk = 2 Unsupported_Kd")] // I, the same through a call's result
    [InlineData("r3MixRefCastBad", "r3MixRefCastBad: kk = 2 Unsupported_Kd")] // I, w's reference bytes beside a
    [InlineData("r4MixOpenCastBad", "r4MixOpenCastBad: kk = 2 Unsupported_Kd")] // I, s.0's u64 beside a
    [InlineData("r3bOpenRefCastBad", "r3bOpenRefCastBad: kk = 2 Unsupported_Kd")] // I, w's reference bytes beside s
    [InlineData("r5PunPtrRefCastBad", "r5PunPtrRefCastBad: kk = 2 Unsupported_Kd")] // I, w's reference bytes beside b
    [InlineData("uReborrowFieldCallOk", "uReborrowFieldCallOk: kk = 2 Unsupported_Kd")] // V, s.1 through "a call's result"
    [InlineData("rvIndexViaBorrowOk", "rvIndexViaBorrowOk: kk = 2 Unsupported_Kd")] // V, s.1 through "`p + n`/`p[n]`"
    [InlineData("rvExposedViaBorrowOk", "rvExposedViaBorrowOk: kk = 2 Unsupported_Kd")] // V, s.1 through "an exposed local"
    [InlineData("r4MixOpenS1Ok", "r4MixOpenS1Ok: kk = 2 Unsupported_Kd")] // V, s.1 through "a Cast", beside a
    [InlineData("r3bOpenRefCastOk", "r3bOpenRefCastOk: kk = 2 Unsupported_Kd")] // V, s.1 through "a Cast", beside w
    [InlineData("r4MixOpenCastOk", "r4MixOpenCastOk: kk = 2 Unsupported_Kd")] // V, a through "a Cast", beside s: one entry keeps provenance
    [InlineData("r3MixRefCastOk", "r3MixRefCastOk: kk = 2 Unsupported_Kd")] // V, a through "a Cast", beside w
    [InlineData("r3PartRefsCastOk", "r3PartRefsCastOk: kk = 2 Unsupported_Kd")] // V, a through "a Cast", beside y's u64
    [InlineData("oqPartRefsCallOk", "oqPartRefsCallOk: kk = 2 Unsupported_Kd")] // V, a through "a call's result", beside y's u64
    [InlineData("r5PunPtrRefCastOk", "r5PunPtrRefCastOk: kk = 2 Unsupported_Kd")] // V, b's raw pointer bytes (5.2) through "a Cast", beside w
    public void ARawPointerABodyReadsPunnedReachesUnownedStorage(string form, string expected)
    {
        var c = MirBuildTest.Compile(BodyReadShapes + BodyRead(form) + "public func main() => ()\n");
        var problems = Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]).Order(StringComparer.Ordinal);
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", problems)}]");
    }

    // PLAN G1106 (SPEC 15.6 Holders, 15.6.4 route 1, 5.2, 5.2.2, 5.7 Calls, 8.15; O-312 (a), O-230 Q1 (a)): at raw layers partly stated
    // (one stating an Origin other than `static` beside one stating none, or an abstract part), a write the call may make at any layer it
    // reaches keeps its own verdict: f, reaching mx through h's address, may leave its own pointer at the unstated layer, and every value
    // read from any part of mx carries it, the stated mx.p included (OwnWrite; take's abstract part, UnknownWrite). A store the layout
    // places stays undecided there (PlacedAt: the layout decides whether it covers the stated layer), the located Unsupported_Kd, never a
    // Language record: at C-layout mx's first byte it covers the stated p in placedStatedFirstNoneOk (5.2: p's pointer promises what a
    // claim of a promises) and the unstated q in placedUnstatedFirstBad. Every layer stated (Pin) keeps nothing of it. Replacing mx whole after f does not end it: the replacement is
    // another Mixed.init result (15.6.4, 15.4.2), reuses other, and mx stays addressed from what f may leave (15.6 Addresses). Each row is
    // I (relabelled: R4-M221 held viaCarriedMixedPOk valid, the repeated review viaCarriedMixedReplacedOk) but the V rows marked:
    // viaCarriedPinOk, viaParamQPinOk and placedStatedFirstNoneOk.
    [Theory]
    [InlineData("viaCarriedMixedPOk", "viaCarriedMixedPOk: kk = 2; viaCarriedMixedPOk: var slot: raw/u8 = null")] // I, mx.p carries mx.q's
    [InlineData("viaCarriedMixedBad", "viaCarriedMixedBad: kk = 2; viaCarriedMixedBad: var slot: raw/u8 = null")] // I, through mx.q
    [InlineData("viaCarriedMixedQFirstPOk", "viaCarriedMixedQFirstPOk: kk = 2; viaCarriedMixedQFirstPOk: var slot: raw/u8 = null")] // I, Field order
    [InlineData("viaCarriedMixedQFirstBad", "viaCarriedMixedQFirstBad: kk = 2; viaCarriedMixedQFirstBad: var slot: raw/u8 = null")] // I
    [InlineData("viaCarriedMixedSPOk", "viaCarriedMixedSPOk: kk = 2")] // I, s `during static`
    [InlineData("viaCarriedMixedTPOk", "viaCarriedMixedTPOk: kk = 2; viaCarriedMixedTPOk: var slot: raw/u8 = null")] // I, an abstract part
    [InlineData("viaCarriedDeepBad", "viaCarriedDeepBad: kk = 2; viaCarriedDeepBad: var hop: raw/(raw/u8) = cell@raw")] // I, past p to hop
    [InlineData("viaCarriedMixedQCopyBad", "viaCarriedMixedQCopyBad: kk = 2; viaCarriedMixedQCopyBad: var slot: raw/u8 = null")] // I, a Copy
    // I: UnknownWrite gives mx its pun, but take's u8 write through c.w, off mx's Position, is the judge's undecided one (CallWrite).
    [InlineData("unknownMixedBad", "unknownMixedBad: kk = 2 Unsupported_Kd; unknownMixedBad: var slot: raw/u8 = null Unsupported_Kd")]
    // V, a u8 store at mx's first byte, the stated p (PlacedAt's partly stated arm: t's abstract part; T is PrimitiveInteger, so no
    // binding holds a reference, 8.7.4). Accepted without the store; a Language record at kk = 2 without the arm.
    [InlineData("placedStatedFirstNoneOk", "placedStatedFirstNoneOk: kk = 2 Unsupported_Kd")]
    // I (route M, 15.6 Holders), the store at the unstated q: the same arm.
    [InlineData("placedUnstatedFirstBad", "placedUnstatedFirstBad: kk = 2 Unsupported_Kd; placedUnstatedFirstBad: var slot: raw/u8 = null Unsupported_Kd")]
    [InlineData("viaParamQBad", "viaParamQBad: kk = 2; viaParamQBad: var slot: raw/u8 = null")] // I, mx.q holds a parameter's pointer
    [InlineData("viaParamQPOk", "viaParamQPOk: kk = 2; viaParamQPOk: var slot: raw/u8 = null")] // I, mx.p carries it
    [InlineData("viaCarriedPinOk", "")] // V, every layer stated: f leaves at pin.p only what a admits
    [InlineData("viaParamQPinOk", "")] // V, no unstated layer
    // I, as on dev: no V control at OwnWrite's site.
    [InlineData("viaCarriedMixedReplacedOk", "viaCarriedMixedReplacedOk: kk = 2; viaCarriedMixedReplacedOk: var slot2: raw/u8 = null; viaCarriedMixedReplacedOk: var slot: raw/u8 = null")]
    public void ACallsWriteAtPartlyStatedLayersTakesItsOwnVerdict(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + CarriedForms[..CarriedForms.IndexOf("func via", StringComparison.Ordinal)] + Form(CarriedForms, form));
        var problems = Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]).Order(StringComparer.Ordinal);
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", problems)}]");
    }

    // V16 (DECISIONS R4-M140, C-189's readers): after a rebind, the pun rule's key memo, its scans and its walks past a call's first step
    // run again over a store pun, a walk past a holder, an undecided write, a stated layer and a no-store input, and the gather's Limit
    // read keys (a holder reached only through a set carrying Φ_U, its state and its stores; PLAN G775), and a body's punned reads
    // (through a raw Deref, a raw Place's borrow, its reborrow, a typed reborrow and a local stored twice, MirBorrows.Lent's exactness
    // in its own step buffer; PLAN G785), and allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ThePunRuleAllocatesNothingWarm()
    {
        var head = PunForms[..PunForms.IndexOf("func storePunBad(", StringComparison.Ordinal)];
        var c = MirBuildTest.Compile(Prelude + head + Form(PunForms, "storePunBad") + Form(PunForms, "pastPunBad") + Form(PunForms, "arrayMidBad") +
            Form(PunForms, "statedOk") + Form(PunForms, "sliceReadOk") + Form(PunForms, "closureMixedRests") + Form(PunForms, "selfStoreRests") +
            BodyRead("uRefBytesBodyBad") + BodyRead("uRefBytesBorrowBad") + BodyRead("uRefBytesReborrowBad") + BodyRead("uReborrowFieldOk") +
            BodyRead("uReborrowFieldTwiceOk"));
        var stratum = new Stratum(c.Types);
        stratum.Run(c.BuildMir());
        MirProgram? program = null;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            stratum.Run(program = c.BuildMir());
        }));
        Assert.Contains("pastPunBad: kk = 2", Problems(c, program!, LoanCodes));
        Assert.Contains("uRefBytesBorrowBad: kk = 2", Problems(c, program!, LoanCodes));
        Assert.Contains("uRefBytesReborrowBad: kk = 2", Problems(c, program!, LoanCodes));
    }

    // A body read form (BodyReadForms) as its function: the head (a `flag` for the forms that branch), kk, its `uniq` borrow r and spare
    // (a raw pointer holding r's Loan), then its statements.
    private static string BodyRead(string form)
        => $"func {form}(flag: bool)\n    var kk: u8 = 1\n    let r = kk@uniq\n    var spare: raw/u8 = r@follow@raw\n{BodyReadForms[form]}\n";

    // A copy of a body with each Place `(*_x)...` of the local `_x` rebased onto the Place X its Ref `_x = &X` borrows (Copied), every other
    // row as built: a match Subject's (its bindings, discriminant and reads), or a binding's (its stores).
    private static MirBody InPlace(TyCtxt types, MirBody built, int local)
    {
        var borrowed = built.Statements.ToArray().Single(x => x.Place == new MirPlace(local) && x.Rvalue.Kind == MirRvalueKind.Ref).Rvalue.Place;
        return Copied(built, body => place => Map(body, place));

        // The Place's projections, root first, re-made in the copy; past the local's Deref, onto the borrowed Place.
        MirPlace Map(MirBody body, MirPlace place)
        {
            var chain = new List<int>();
            for (var id = place.Projection; id > 0; id = built.Projections[id].Prefix)
            {
                chain.Insert(0, id);
            }

            var rebased = place.Root == local && chain.Count > 0 && built.Projections[chain[0]].Kind == MirProjectionKind.Deref;
            var at = rebased ? Map(body, borrowed) : new MirPlace(place.Root);
            foreach (var id in chain.Skip(rebased ? 1 : 0))
            {
                at = body.Project(types, at, built.Projections[id].Kind, built.Projections[id].Arg);
            }

            return at;
        }
    }

    // A body's first match binding of a borrowed payload: the first Ref whose Place passes a Downcast past a Deref of its root, the Subject.
    private static MirStatement Binding(MirBody body)
    {
        return body.Statements.ToArray().First(x => x.Rvalue is { Kind: MirRvalueKind.Ref, Place.Projection: > 0 } && Payload(x.Rvalue.Place.Projection));

        bool Payload(int id)
        {
            var cast = false;
            for (; body.Projections[id].Prefix > 0; id = body.Projections[id].Prefix)
            {
                cast |= body.Projections[id].Kind == MirProjectionKind.Downcast;
            }

            return cast && body.Projections[id].Kind == MirProjectionKind.Deref;
        }
    }
}
