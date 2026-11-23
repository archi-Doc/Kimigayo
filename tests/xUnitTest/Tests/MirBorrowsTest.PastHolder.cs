// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's W reads a holder past another at every layer of the key it was reached at (SPEC 15.6 Holders, 5.2, 5.7 Calls,
/// 15.6.4; the A5 value redesign's unit U3c, the owner's O-209 and O-230): each element is typed, punned or read nothing at each layer,
/// and a holder it writes takes the record of that layer's storage, not the Place Type of the Loan that reaches it (R4-M154 Revision 8
/// for an element that is no whole borrow); a call input's key is the called function's parameter key, so a layer its own Type
/// parameters' bindings hide adds no reach (TyCtxt.ParameterKey). The rows are the unit's coverage table, each labelled from SPEC (I
/// invalid, V valid), with the records the head gives (SPEC 23.1.2).</summary>
public sealed partial class MirBorrowsTest
{
    private static readonly Dictionary<string, string> PastHolderForms = new()
    {
        ["zPairLayerBad"] = """
            func usep<T>(p: raw/T) => ()
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakL(slot: uniq/((Option<uniq/(ref/i32 during q) during o>, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func zPairLayer()
                var z: i32 = 0
                var y: i32 = 1
                var b: [2 of ref/i32] = [z@ref, z@ref]
                let pb = b@raw
                unsafe
                    var h: (Option<uniq/(ref/i32)>, uniq/[2 of ref/i32]) = (.None, (*pb)@uniq)
                    sneakL(h@uniq, y@ref)
                    y = 5
                    look(b@ref)
            """,
        ["zPairLayerOk"] = """
            func usep<T>(p: raw/T) => ()
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakL(slot: uniq/((Option<uniq/(ref/i32 during q) during o>, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func zPairLayer()
                var z: i32 = 0
                var y: i32 = 1
                var b: [2 of ref/i32] = [z@ref, z@ref]
                let pb = b@raw
                unsafe
                    var h: (Option<uniq/(ref/i32)>, uniq/[2 of ref/i32]) = (.None, (*pb)@uniq)
                    sneakL(h@uniq, y@ref)
                    look(b@ref)
                    y = 5
            """,
        ["uNarrowBad"] = """
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakU(slot: uniq/((uniq/(ref/i32 during q) during o, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func uNarrow()
                var z: i32 = 0
                var y: i32 = 1
                var x: [2 of ref/i32] = [z@ref, z@ref]
                var x2: [2 of ref/i32] = [z@ref, z@ref]
                let p = x@raw@raw/(ref/i32)
                unsafe
                    var h = ((*p)@uniq, x2@uniq)
                    sneakU(h@uniq, y@ref)
                    y = 5
                    look(x@ref)
            """,
        ["uDispBad"] = """
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakU(slot: uniq/((uniq/(ref/i32 during q) during o, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func uDisp()
                var z: i32 = 0
                var y: i32 = 1
                var x: [2 of ref/i32] = [z@ref, z@ref]
                var x2: [2 of ref/i32] = [z@ref, z@ref]
                let p0 = x@raw@raw/(ref/i32)
                unsafe
                    let p = p0 + 1
                    var h = ((*p)@uniq, x2@uniq)
                    sneakU(h@uniq, y@ref)
                    y = 5
                    look(x@ref)
            """,
        ["uWideOk"] = """
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakUW(slot: uniq/((uniq/(ref/i32 during r) during o, Option<uniq/(Option<ref/i32 during q>) during o>)), y: ref/i32 during q) => ()
            func uWide()
                var z: i32 = 0
                var y: i32 = 1
                var x: [2 of ref/i32] = [z@ref, z@ref]
                let p = x@raw@raw/(ref/i32)
                unsafe
                    var h: (uniq/(ref/i32), Option<uniq/(Option<ref/i32>)>) = ((*p)@uniq, .None)
                    sneakUW(h@uniq, y@ref)
                    y = 5
                    look(x@ref)
            """,
        ["zAddrPairBad"] = """
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakP(slot: uniq/((uniq/(ref/i32 during q) during o, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func zAddrPair()
                var z: i32 = 0
                var y: i32 = 1
                var a: ref/i32 = z@ref
                var b: [2 of ref/i32] = [z@ref, z@ref]
                let pa = a@raw
                let pb = b@raw
                unsafe
                    var h = ((*pa)@uniq, (*pb)@uniq)
                    sneakP(h@uniq, y@ref)
                    y = 5
                    look(b@ref)
            """,
        ["zAddrPairOk"] = """
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakP(slot: uniq/((uniq/(ref/i32 during q) during o, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func zAddrPair()
                var z: i32 = 0
                var y: i32 = 1
                var a: ref/i32 = z@ref
                var b: [2 of ref/i32] = [z@ref, z@ref]
                let pa = a@raw
                let pb = b@raw
                unsafe
                    var h = ((*pa)@uniq, (*pb)@uniq)
                    sneakP(h@uniq, y@ref)
                    look(b@ref)
                    y = 5
            """,
        ["zAddrPairNoneOk"] = """
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakN(slot: uniq/((uniq/(Option<ref/i32 during q>) during o, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func zAddrPairNone()
                var z: i32 = 0
                var y: i32 = 1
                var a2: Option<ref/i32> = .None
                var b: [2 of ref/i32] = [z@ref, z@ref]
                let pa = a2@raw
                let pb = b@raw
                unsafe
                    var h = ((*pa)@uniq, (*pb)@uniq)
                    sneakN(h@uniq, y@ref)
                    y = 5
                    look(b@ref)
            """,
        ["oriRefLeafOk"] = """
            func oriRefLeafOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var x: u8 = 0
                var w: ref/u8 = x@ref
                let rw = w@uniq
                let n: u64 = 3
                let q = rw@follow@raw
                var c = func [n, q] (v: raw/u8)
                    unsafe => *q = *q
                c(spare)
                kk = 2
            """,
        ["d4UniqRefLetOk"] = """
            func d4UniqRefLetOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var cell: u8 = 0
                let n: u64 = 3
                let r = cell@uniq
                var c = func [n, r] (v: raw/u8)
                    r@follow = 5
                c(spare)
                kk = 2
            """,
        ["d4NestVarBad"] = """
            func d4NestVarBad()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var x: u8 = 0
                var w: ref/u8 = x@ref
                let n: u64 = 3
                let q: raw/(ref/u8) = w@raw
                var inner = func [n, q] (v: raw/u8)
                    unsafe => *q = *q
                var outer = func [var inner@move] (v: raw/u8)
                    inner(v)
                outer(spare)
                kk = 2
            """,
        ["d4NestLetOk"] = """
            func d4NestLetOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var x: u8 = 0
                var w: ref/u8 = x@ref
                let n: u64 = 3
                let k: u64 = 5
                let q: raw/(ref/u8) = w@raw
                var inner = func [n, q] (v: raw/u8)
                    unsafe => *q = *q
                var outer = func [inner@move, var k] (v: raw/u8)
                    k = 4
                outer(spare)
                kk = 2
            """,
        ["d4AggGenOk"] = """
            func touchT<F>(t: uniq/(F, u8), v: raw/u8) => ()
            func d4AggGenOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var x: u8 = 0
                var w: ref/u8 = x@ref
                let n: u64 = 3
                let z8: u8 = 0
                let q: raw/(ref/u8) = w@raw
                let c = func [n, q] (v: raw/u8) => ()
                var t = (c@move, z8)
                touchT(t@uniq, spare)
                kk = 2
            """,
        ["d4AggGenUseOk"] = """
            func inspect<T>(value: ref/T) => ()
            func touchT<F>(t: uniq/(F, u8), v: raw/u8) => ()
            func d4AggGenUseOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var x: u8 = 0
                var w: ref/u8 = x@ref
                let n: u64 = 3
                let z8: u8 = 0
                let q: raw/(ref/u8) = w@raw
                let c = func [n, q] (v: raw/u8) => ()
                var t = (c@move, z8)
                touchT(t@uniq, spare)
                kk = 2
                inspect(w)
            """,
        ["aggGenShownBad"] = """
            func touchS<F>(t: uniq/(F, raw/(raw/u8)), v: raw/u8) => ()
            func aggGenShownBad()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var other: raw/u8 = null
                let n: u64 = 3
                let c = func [n] (v: raw/u8) => ()
                var t = (c@move, other@raw)
                touchS(t@uniq, spare)
                kk = 2
            """,
        ["letRefLeafOk"] = """
            func letRefLeafOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: u8 = 0
                var w: ref/u8 = x@ref
                let n: u64 = 3
                let q: raw/(ref/u8) = w@raw
                var c = func [n, q] (v: raw/u8)
                    unsafe => *q = *q
                c(spare)
                kk = 2
            """,
        ["dDeepOk"] = """
            func usep<T>(p: raw/T) => ()
            func st(t: uniq/((raw/u32, uniq/(((((((((((((((((((((((((((((((((((((((((i32, i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32)) during c, uniq/((raw/u32, uniq/(((((((((((((((((((((((((((((((((((((((((i32, i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32), i32)) during c2)) during c3, raw/u8)) during e, v: ref/i32 during a) => ()
            func f(p: raw/u8)
                var value = 42
                var x = 1
                var r = (x@ref, 5)
                var n1 = ((((((((((((((((((((((((((((((((((((((((0, 0), 1), 2), 3), 4), 5), 6), 7), 8), 9), 10), 11), 12), 13), 14), 15), 16), 17), 18), 19), 20), 21), 22), 23), 24), 25), 26), 27), 28), 29), 30), 31), 32), 33), 34), 35), 36), 37), 38), 39)
                var n2 = ((((((((((((((((((((((((((((((((((((((((0, 0), 1), 2), 3), 4), 5), 6), 7), 8), 9), 10), 11), 12), 13), 14), 15), 16), 17), 18), 19), 20), 21), 22), 23), 24), 25), 26), 27), 28), 29), 30), 31), 32), 33), 34), 35), 36), 37), 38), 39)
                var d = (r.0@raw@raw/u32, n2@uniq)
                var t = (r.1@raw@raw/u32, n1@uniq, d@uniq, p)
                st(t@uniq, value@ref)
                value = 99
                let z = r
            """,
        ["uUniqBytesYBad"] = """
            func usep<T>(p: raw/T) => ()
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func usei(v: i32) => ()
            func g()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var w: uniq/i32 = y@uniq
                keep2(w@raw@raw/(raw/(raw/u8)), spare)
                kk = 2
                usei(y)
            """,
        ["rAliasBorrowBad"] = """
            func usep<T>(p: raw/T) => ()
            func wa<U>(p: raw/U) => ()
            func g()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: raw/u8 = null
                var b: raw/u8 = null
                var m = a@uniq
                var h = b@uniq
                var x: (raw/(uniq/(raw/u8)), raw/(raw/(raw/u8))) = (m@raw, h@raw@raw/(raw/(raw/u8)))
                wa(x@raw)
                h@follow = spare
                kk = 2
            """,
        ["rAliasBorrowU64Bad"] = """
            func usep<T>(p: raw/T) => ()
            func wa<U>(p: raw/U) => ()
            func g()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: raw/u8 = null
                var b: raw/u8 = null
                var m = a@uniq
                var h = b@uniq
                var x: (raw/(uniq/(raw/u8)), raw/u64) = (m@raw, h@raw@raw/u64)
                wa(x@raw)
                h@follow = spare
                kk = 2
            """,
        ["rAliasBorrowNoEntryBad"] = """
            func usep<T>(p: raw/T) => ()
            func wa<U>(p: raw/U) => ()
            func g()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var b: raw/u8 = null
                var h = b@uniq
                var x: raw/(raw/(raw/u8)) = h@raw@raw/(raw/(raw/u8))
                wa(x@raw)
                h@follow = spare
                kk = 2
            """,
        ["rAliasBorrowNoCallOk"] = """
            func usep<T>(p: raw/T) => ()
            func wa<U>(p: raw/U) => ()
            func g()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: raw/u8 = null
                var b: raw/u8 = null
                var m = a@uniq
                var h = b@uniq
                var x: (raw/(uniq/(raw/u8)), raw/(raw/(raw/u8))) = (m@raw, h@raw@raw/(raw/(raw/u8)))
                h@follow = spare
                kk = 2
            """,
        ["rAliasCleanOk"] = """
            func usep<T>(p: raw/T) => ()
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func wa<U>(p: raw/U) => ()
            func g(out: raw/(raw/u8))
                var kk: u8 = 1
                var b: u8 = 0
                var m0: u8 = 0
                var m = m0@uniq
                var h = b@uniq
                var x: (raw/(uniq/u8), raw/(raw/(raw/u8))) = (m@raw, h@raw@raw/(raw/(raw/u8)))
                wa(x@raw)
                let r = kk@uniq
                h@follow = 5
                let z = r@follow
            """,
        ["rAliasCleanBad"] = """
            func usep<T>(p: raw/T) => ()
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func wa<U>(p: raw/U) => ()
            func g(out: raw/(raw/u8))
                var kk: u8 = 1
                keep(out, kk@raw)
                var b: u8 = 0
                var m0: u8 = 0
                var m = m0@uniq
                var h = b@uniq
                var x: (raw/(uniq/u8), raw/(raw/(raw/u8))) = (m@raw, h@raw@raw/(raw/(raw/u8)))
                wa(x@raw)
                let r = kk@uniq
                h@follow = 5
                let z = r@follow
            """,
        ["rAliasCleanNoEntryBad"] = """
            func usep<T>(p: raw/T) => ()
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func wa<U>(p: raw/U) => ()
            func g(out: raw/(raw/u8))
                var kk: u8 = 1
                keep(out, kk@raw)
                var b: u8 = 0
                var h = b@uniq
                var x: raw/(raw/(raw/u8)) = h@raw@raw/(raw/(raw/u8))
                wa(x@raw)
                let r = kk@uniq
                h@follow = 5
                let z = r@follow
            """,
        ["rAliasCleanNoCallOk"] = """
            func usep<T>(p: raw/T) => ()
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func wa<U>(p: raw/U) => ()
            func g(out: raw/(raw/u8))
                var kk: u8 = 1
                keep(out, kk@raw)
                var b: u8 = 0
                var m0: u8 = 0
                var m = m0@uniq
                var h = b@uniq
                var x: (raw/(uniq/u8), raw/(raw/(raw/u8))) = (m@raw, h@raw@raw/(raw/(raw/u8)))
                let r = kk@uniq
                h@follow = 5
                let z = r@follow
            """,
        ["hPunWriteOk"] = """
            func usep<T>(p: raw/T) => ()
            func keep(slot: raw/(raw/u8), value: raw/u8) => ()
            func wa<U>(p: raw/U) => ()
            func put(p: raw/u8, v: u8) => ()
            func g(out: raw/(raw/u8))
                var kk: u8 = 1
                keep(out, kk@raw)
                var b: u8 = 0
                var m0: u8 = 0
                var m = m0@uniq
                var h: raw/u8 = b@raw
                var x: (raw/(uniq/u8), raw/(raw/(raw/u8))) = (m@raw, h@raw@raw/(raw/(raw/u8)))
                wa(x@raw)
                let r = kk@uniq
                put(h, 5)
                let z = r@follow
            """,
        ["n13ParamBad"] = """
            func stashO(slot: raw/(raw/(raw/u8)) during o, own: uniq/(raw/(raw/u8)) during o, value: raw/u8) => ()
            func n13ParamBad()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var hop2: raw/(raw/u8) = null
                let rh = hop2@uniq
                stashO(rh@follow@raw, rh, spare)
                kk = 2
            """,
        ["n13ParamOk"] = """
            func stashO1(slot: raw/(raw/u8) during o, own: uniq/(raw/u8) during o, value: raw/u8) => ()
            func n13ParamOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var hop2: raw/u8 = null
                let rh = hop2@uniq
                stashO1(rh@follow@raw, rh, spare)
                kk = 2
            """,
        ["n13LensBad"] = """
            struct Lens2 {source}
                internal let loan: Loan<uniq/(raw/(raw/u8)) during source>
                internal let at: raw/(raw/(raw/u8)) during source
                internal init(target: uniq/(raw/(raw/u8)) during source)
                    self.at = target@follow@raw
                    self.loan = Loan.init(target)
            func stashL(lens: uniq/Lens2, value: raw/u8) => ()
            func n13LensBad()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var hop2: raw/(raw/u8) = null
                var lens = Lens2.init(target: hop2@uniq)
                stashL(lens@uniq, spare)
                kk = 2
            """,
        ["n13LensSharedOk"] = """
            struct Lens2 {source}
                internal let loan: Loan<uniq/(raw/(raw/u8)) during source>
                internal let at: raw/(raw/(raw/u8)) during source
                internal init(target: uniq/(raw/(raw/u8)) during source)
                    self.at = target@follow@raw
                    self.loan = Loan.init(target)
            func stashS(lens: ref/Lens2, value: raw/u8) => ()
            func n13LensSharedOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var hop2: raw/(raw/u8) = null
                var lens = Lens2.init(target: hop2@uniq)
                stashS(lens@ref, spare)
                kk = 2
            """,
        ["uNarrowTwoBad"] = """
            func look(a: ref/[2 of ref/i32 during k]) => ()
            func sneakU(slot: uniq/((uniq/(ref/i32 during q) during o, uniq/([2 of ref/i32 during r]) during o)), y: ref/i32 during q) => ()
            func uNarrowTwo()
                var z: i32 = 0
                var y: i32 = 1
                var y2: i32 = 2
                var x: [2 of ref/i32] = [z@ref, z@ref]
                var x2: [2 of ref/i32] = [z@ref, z@ref]
                var u: [2 of ref/i32] = [z@ref, z@ref]
                var u2: [2 of ref/i32] = [z@ref, z@ref]
                let p = x@raw@raw/(ref/i32)
                let pu = u@raw@raw/(ref/i32)
                unsafe
                    var h = ((*p)@uniq, x2@uniq)
                    sneakU(h@uniq, y@ref)
                    var h2 = ((*pu)@uniq, u2@uniq)
                    sneakU(h2@uniq, y2@ref)
                    y = 5
                    y2 = 6
                    look(x@ref)
                    look(u@ref)
            """,
        ["zIntMixBad"] = """
            func sneakI(slot: raw/((raw/(raw/u8), raw/u8)), value: raw/u8) => ()
            func zIntMixBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let n: usize = 64
                var h: (raw/(raw/u8), raw/u8) = (n@raw/(raw/u8), null)
                sneakI(h@raw, spare)
                kk = 2
            """,
        ["zIntMixOk"] = """
            func sneakI1(slot: raw/((raw/u8, raw/u8)), value: raw/u8) => ()
            func zIntMixOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let n: usize = 64
                var h: (raw/u8, raw/u8) = (n@raw/u8, null)
                sneakI1(h@raw, spare)
                kk = 2
            """,
        ["lBorrowRefBad"] = """
            func sneakU(slot: uniq/(uniq/(raw/(raw/u8)) during o), value: raw/u8) => ()
            func lBorrowRefBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var w = y@ref
                let p = w@raw@raw/(raw/(raw/u8))
                unsafe
                    var h = (*p)@uniq
                    sneakU(h@uniq, spare)
                    kk = 2
                let done = 0
            """,
        ["lBorrowScalarPartBad"] = """
            func sneakU(slot: uniq/(uniq/(raw/(raw/u8)) during o), value: raw/u8) => ()
            func lBorrowScalarPartBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var w = (7@u64, y@ref)
                let p = w@raw@raw/(raw/(raw/u8))
                unsafe
                    var h = (*p)@uniq
                    sneakU(h@uniq, spare)
                    kk = 2
                let done = 0
            """,
        ["lBorrowRefOk"] = """
            func sneakU1(slot: uniq/(uniq/(raw/u8) during o), value: raw/u8) => ()
            func lBorrowRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var w = y@ref
                let p = w@raw@raw/(raw/u8)
                unsafe
                    var h = (*p)@uniq
                    sneakU1(h@uniq, spare)
                    kk = 2
                let done = 0
            """,
        ["kPastRawRefOk"] = """
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func kPastRawRefOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var y: i32 = 5
                var h = y@ref
                var p: raw/(ref/i32) = h@raw
                keep1(p@raw@raw/(raw/u8), spare)
                kk = 2
            """,
        ["otherOriginNullOk"] = """
            func stO(t: uniq/(raw/(ref/i32 during a)), v: ref/i64 during b) => ()
            func otherOriginNullOk()
                var y: i64 = 2
                var c2: raw/(ref/i32) = null
                stO(c2@uniq, y@ref)
                y = 5
            """,
        ["otherOriginNullBad"] = """
            func stO2(t: uniq/(raw/(ref/i32 during a)), v: ref/i32 during a) => ()
            func otherOriginNullBad()
                var y: i32 = 2
                var c2: raw/(ref/i32) = null
                stO2(c2@uniq, y@ref)
                y = 5
            """,
        ["otherOriginOwnPtrOk"] = """
            func stO(t: uniq/(raw/(ref/i32 during a)), v: ref/i64 during b) => ()
            func otherOriginOwnPtrOk()
                var x: i32 = 1
                var y: i64 = 2
                var cell: ref/i32 = x@ref
                var c2 = cell@raw
                stO(c2@uniq, y@ref)
                y = 5
            """,
        ["otherOriginChainBad"] = """
            func stO(t: uniq/(raw/(ref/i32 during a)), v: ref/i64 during b) => ()
            func otherOriginChainBad()
                var x: i32 = 1
                var y: i64 = 2
                var cell: ref/i32 = x@ref
                var c2 = cell@raw
                stO(c2@uniq, y@ref)
                x = 5
            """,
        ["keepHalfOk"] = """
            func keepH<T>(slot: raw/(raw/T), value: raw/u8) => ()
            func twoLayersHalf(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepH(hop@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["keepShownBad"] = """
            func keepS<T>(slot: raw/(raw/(raw/T)), value: raw/T) => ()
            func twoLayersShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepS(hop@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["gHidOwnPtrOk"] = """
            func keepG<T>(slot: raw/(raw/T), value: raw/u8) => ()
            func gHidOwnPtrOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepG(hop@raw, spare)
                kk = 2
            """,
        ["gShownOwnPtrBad"] = """
            func keepG2<T>(slot: raw/(raw/(raw/T)), value: raw/u8) => ()
            func gShownOwnPtrBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepG2(hop@raw, spare)
                kk = 2
            """,
        ["keepTHopUsedOk"] = """
            func usepp(p: raw/(raw/u8)) => ()
            func keepT<T>(slot: raw/T, value: raw/u8) => ()
            func twoLayersTHopUsed(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepT(hop@raw, w@raw@raw/u8)
                y@follow = 5
                usepp(hop)
            """,
        ["twoLayersBad"] = """
            func keep2(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
            func twoLayers(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keep2(hop@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["zUniqPastEnumHOk"] = """
            func usep<T>(p: raw/T) => ()
            func viaU(slot: raw/(uniq/((raw/u8, Option<raw/(raw/u8)>)) during o), value: raw/u8) => ()
            func zUniqPastEnumHOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: (raw/u64, raw/u64, raw/u64) = (x@raw@raw/u64, null, null)
                let p = h@raw@raw/((raw/u8, Option<raw/(raw/u8)>))
                unsafe
                    var g = (*p)@uniq
                    viaU(g@raw, spare)
                    kk = 2
                    usep(h.0)
            """,
        ["zUniqPastRefHOk"] = """
            func usep<T>(p: raw/T) => ()
            func viaB(slot: raw/(uniq/((raw/u8, ref/u64 during k)) during o), value: raw/u8) => ()
            func zUniqPastRefHOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: (raw/u64, raw/u64, raw/u64) = (x@raw@raw/u64, null, null)
                let p = h@raw@raw/((raw/u8, ref/u64))
                unsafe
                    var g = (*p)@uniq
                    viaB(g@raw, spare)
                    kk = 2
                    usep(h.0)
            """,
        ["zUniqPastTupBad"] = """
            func usep<T>(p: raw/T) => ()
            func viaT(slot: raw/(uniq/((raw/u8, u64)) during o), value: raw/u8) => ()
            func zUniqPastTupBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x: raw/u8 = null
                var h: (raw/u64, raw/u64, raw/u64) = (x@raw@raw/u64, null, null)
                let p = h@raw@raw/((raw/u8, u64))
                unsafe
                    var g = (*p)@uniq
                    viaT(g@raw, spare)
                    kk = 2
                    usep(h.0)
            """,
        ["cloVarPtrU8Bad"] = """
            func cloVarPtrU8Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let m: raw/u8 = null
                var c = func [var m] (v: raw/u8)
                    m = v
                c(spare)
                kk = 2
            """,
        ["cloVarPtrU8CellBad"] = """
            func cloVarPtrU8CellBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var cell: u8 = 0
                let m: raw/u8 = cell@raw
                var c = func [var m] (v: raw/u8)
                    m = v
                c(spare)
                kk = 2
            """,
        ["cloVarPtrU8NullWriteBad"] = """
            func cloVarPtrU8NullWriteBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let m: raw/u8 = null
                var c = func [var m] (v: raw/u8)
                    m = null
                c(spare)
                kk = 2
            """,
        ["cloVarPtrU8OtherVarBad"] = """
            func cloVarPtrU8OtherVarBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let m: raw/u8 = null
                var n: i32 = 0
                var c = func [var m, var n] (v: raw/u8)
                    n += 1
                c(spare)
                kk = 2
            """,
        ["cloVarPtrU8SharedOk"] = """
            func usep(p: raw/u8) => ()
            func cloVarPtrU8SharedOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let m: raw/u8 = null
                var c = func [var m] (v: raw/u8)
                    usep(m)
                c(spare)
                kk = 2
            """,
        ["cloVarI32Ok"] = """
            func cloVarI32Ok()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var n: i32 = 0
                var c = func [var n] (v: raw/u8)
                    n += 1
                c(spare)
                kk = 2
            """,
        ["cloVarPtrU8NullArgOk"] = """
            func cloVarPtrU8NullArgOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let m: raw/u8 = null
                var c = func [var m] (v: raw/u8)
                    m = v
                c(null)
                kk = 2
            """,
        ["cloVarTupPtrU8Bad"] = """
            func cloVarTupPtrU8Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                let m: (raw/u8, i32) = (null, 0)
                var c = func [var m] (v: raw/u8)
                    m = (v, 1)
                c(spare)
                kk = 2
            """,
        ["tupParamPtrU8Ok"] = """
            func viaTup(t: uniq/((raw/u8, i32)), v: raw/u8) => ()

            func tupParamPtrU8Ok()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var t: (raw/u8, i32) = (null, 0)
                viaTup(t@uniq, spare)
                kk = 2
            """,
        ["enumPayloadPtrU8Bad"] = """
            enum Slot
                Has(raw/u8)
                Empty

            func viaSlot(s: uniq/Slot, v: raw/u8) => ()

            func enumPayloadPtrU8Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s: Slot = .Has(null)
                viaSlot(s@uniq, spare)
                kk = 2
            """,
        ["enumPayloadPtrU8Ok"] = """
            func viaRaw(s: uniq/(raw/u8), v: raw/u8) => ()

            func enumPayloadPtrU8Ok()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s: raw/u8 = null
                viaRaw(s@uniq, spare)
                kk = 2
            """,
        ["r4MixHopBad"] = """
            func keep2m(slot: raw/((raw/(raw/u8), raw/u8 during static)), value: raw/u8) => ()

            func mixHop(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: (raw/(raw/u8), raw/u8 during static) = (cell@raw, null)
                keep2m(hop@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["r4MixHopOk"] = """
            func keep1m(slot: raw/((raw/u8, raw/u8 during static)), value: raw/u8) => ()

            func mixHopOne(y: uniq/i32)
                let w = y@follow@ref
                var hop: (raw/u8, raw/u8 during static) = (null, null)
                keep1m(hop@raw, w@raw@raw/u8)
                y@follow = 5
            """,
    };

    // The coverage table's rows (U3c; the state file a5v-u3c-state.md, sections 1 and 4; its implementation review's rows). Not rows
    // here, since no row asserts an invalid program's acceptance: the three environment rows the Front's A5-F builds (an environment's
    // raw layers stated, a `let` binding's own layer marked) and a holder whose address a hidden binding carries, written with the
    // record of its own storage Type (the owner's O-312 (a); a Binding fact), each a PLAN row of its unit. PLAN G785's rows
    // (a5v-r3ref-state.md): past a holder through a borrow layer, a punned read over a reference's or an `owner` Scalar's bytes reaches
    // storage the body does not own.
    [Theory]
    [InlineData("zPairLayerBad", "zPairLayer: y = 5")] // I
    [InlineData("zPairLayerOk", "")] // V
    [InlineData("uNarrowBad", "uNarrow: y = 5")] // I
    [InlineData("uDispBad", "uDisp: y = 5")] // I
    [InlineData("uWideOk", "")] // V
    [InlineData("zAddrPairBad", "zAddrPair: y = 5")] // I
    [InlineData("zAddrPairOk", "")] // V
    [InlineData("zAddrPairNoneOk", "zAddrPairNone: y = 5")] // I
    [InlineData("oriRefLeafOk", "")] // V
    [InlineData("d4UniqRefLetOk", "")] // V
    [InlineData("d4NestVarBad", "d4NestVarBad: kk = 2; d4NestVarBad: var x: u8 = 0")] // I: the `var` binding inner may hold the call's own pointer
    [InlineData("d4NestLetOk", "")] // V
    [InlineData("d4AggGenOk", "")] // V
    [InlineData("d4AggGenUseOk", "d4AggGenUseOk: kk = 2")] // I by the owner's O-312 (a): the carried addresses are reached, hidden or not, so touchT may write w
    [InlineData("aggGenShownBad", "aggGenShownBad: kk = 2")] // I: a raw layer shown beside the hidden part may hold the call's own pointer
    [InlineData("letRefLeafOk", "")] // V
    [InlineData("dDeepOk", "f: value = 99; f: var d = (r.0@raw@raw/u32, n2@uniq); f: var n2 = ((((((((((((((((((((((((((((((((((((((((0, 0), 1), 2), 3), 4), 5), 6), 7), 8), 9), 10), 11), 12), 13), 14), 15), 16), 17), 18), 19), 20), 21), 22), 23), 24), 25), 26), 27), 28), 29), 30), 31), 32), 33), 34), 35), 36), 37), 38), 39); f: var n1 = ((((((((((((((((((((((((((((((((((((((((0, 0), 1), 2), 3), 4), 5), 6), 7), 8), 9), 10), 11), 12), 13), 14), 15), 16), 17), 18), 19), 20), 21), 22), 23), 24), 25), 26), 27), 28), 29), 30), 31), 32), 33), 34), 35), 36), 37), 38), 39); f: var x = 1")] // V by SPEC; records beside the depth limit: E's record by the reading layer (R3d.5) is not in this unit
    [InlineData("uUniqBytesYBad", "g: kk = 2; g: usei(y); g: var y: i32 = 5")] // I, PLAN G785: E keeps w's uniq Loan on y (15.6.4, reading (a))
    [InlineData("rAliasBorrowBad", "g: kk = 2")] // I
    [InlineData("rAliasBorrowU64Bad", "g: kk = 2")] // I
    [InlineData("rAliasBorrowNoEntryBad", "g: kk = 2")] // V by SPEC; rejected as on dev (valid by O-230); not reached by this unit
    [InlineData("rAliasBorrowNoCallOk", "")] // V
    [InlineData("rAliasCleanOk", "")] // V
    [InlineData("rAliasCleanBad", "")] // V
    [InlineData("rAliasCleanNoEntryBad", "")] // V
    [InlineData("rAliasCleanNoCallOk", "")] // V
    [InlineData("hPunWriteOk", "")] // V
    [InlineData("n13ParamBad", "n13ParamBad: kk = 2; n13ParamBad: var hop2: raw/(raw/u8) = null")] // I: hop2 may hold the call's own pointer
    [InlineData("n13ParamOk", "")] // V
    [InlineData("n13LensBad", "n13LensBad: kk = 2; n13LensBad: var hop2: raw/(raw/u8) = null")] // I
    [InlineData("n13LensSharedOk", "")] // V
    [InlineData("uNarrowTwoBad", "uNarrowTwo: y = 5; uNarrowTwo: y2 = 6")] // I
    [InlineData("zIntMixBad", "zIntMixBad: kk = 2")] // I
    [InlineData("zIntMixOk", "")] // V
    [InlineData("lBorrowRefBad", "lBorrowRefBad: kk = 2; lBorrowRefBad: var y: i32 = 5")] // I, past a holder through a borrow layer, over a reference's bytes
    [InlineData("lBorrowScalarPartBad", "lBorrowScalarPartBad: kk = 2; lBorrowScalarPartBad: var y: i32 = 5")] // I, the same over an owner Scalar's bytes (the first clause)
    [InlineData("lBorrowRefOk", "")] // V, only a u8 is stored through the row
    [InlineData("kPastRawRefOk", "")] // V, p retains spare, not used after; h is not reached
    [InlineData("otherOriginNullOk", "")] // V by SPEC: the call's own pointer at c2's layer stores a ref/i32 during a, which v is not; accepted: stO's entry for that storage (G935's rule R, which rule 4 reads) no longer retains v
    [InlineData("otherOriginNullBad", "otherOriginNullBad: y = 5")] // I: v fits that layer's pointee
    [InlineData("otherOriginOwnPtrOk", "otherOriginOwnPtrOk: var x: i32 = 1")] // I: storage the body does not own keeps a read through t, cell's Loan on x; the false `y = 5` (that entry's, as otherOriginNullOk) is gone
    [InlineData("otherOriginChainBad", "otherOriginChainBad: x = 5")] // I, the same; the false `var y` (that entry's) is gone
    [InlineData("keepHalfOk", "")] // V, the owner's O-353 (a): hop's layer raw/T is shown, its pointee T's binding adds no reach
    [InlineData("keepShownBad", "twoLayersShown: y@follow = 5")] // I: both layers above T are the parameter's own (twoLayers)
    [InlineData("gHidOwnPtrOk", "")] // V, as keepHalfOk
    [InlineData("gShownOwnPtrBad", "gShownOwnPtrBad: kk = 2")] // I, as keepShownBad
    [InlineData("keepTHopUsedOk", "twoLayersTHopUsed: y@follow = 5")] // V by the owner's O-353 (a); rejected: hop takes keepT<raw/(raw/u8)>'s instantiated Own record at the first step (Binding's, PLAN G841)
    [InlineData("twoLayersBad", "twoLayers: y@follow = 5")] // I, SPEC 15.6.4's example twoLayers with its error line
    [InlineData("zUniqPastEnumHOk", "")] // V: through a borrow layer a non-punnable referent designates nothing at h
    [InlineData("zUniqPastRefHOk", "")] // V, the same
    [InlineData("zUniqPastTupBad", "zUniqPastTupBad: kk = 2")] // I: a punnable referent is read punned over h.0's bytes
    [InlineData("cloVarPtrU8Bad", "cloVarPtrU8Bad: kk = 2")] // I, PLAN G841 rule 4 (4): the Exclusive call may write its own pointer at var m, a raw layer without an Origin of its environment, and store spare through it in storage the body does not own (5.7 Calls, 15.6.4, 15.8.2; O-373 (a)); c is not used after kk = 2
    [InlineData("cloVarPtrU8CellBad", "cloVarPtrU8CellBad: kk = 2")] // I, the same with m addressing a local
    [InlineData("cloVarPtrU8NullWriteBad", "cloVarPtrU8NullWriteBad: kk = 2")] // I, the same: the contract, not the body, decides (15.6.4)
    [InlineData("cloVarPtrU8OtherVarBad", "cloVarPtrU8OtherVarBad: kk = 2")] // I, the same through another var binding: the call may write var m (15.8.2)
    [InlineData("cloVarTupPtrU8Bad", "cloVarTupPtrU8Bad: kk = 2")] // I, the same at a Tuple binding's raw element
    [InlineData("cloVarPtrU8SharedOk", "")] // V, a Shared call writes nothing past ref/Self
    [InlineData("cloVarI32Ok", "")] // V, an environment with no raw layer
    [InlineData("cloVarPtrU8NullArgOk", "")] // V, nothing carries r's Loan into the call
    [InlineData("tupParamPtrU8Ok", "")] // V, a parameter's own Tuple layer: past it only a u8 (15.6.4 oneLayer; Binding's entry is the bound)
    [InlineData("enumPayloadPtrU8Bad", "enumPayloadPtrU8Bad: kk = 2")] // I, rule 4 (4) at a Case payload's layer a declaration states (5.7 Calls; O-373 (a))
    [InlineData("enumPayloadPtrU8Ok", "")] // V, a parameter's own layer (15.6.4 oneLayer)
    [InlineData("r4MixHopBad", "mixHop: y@follow = 5")] // I, rule 4 (4) at a key that keeps the declared step: hop.0, a layer without an Origin beside a stated one (5.7 Calls decides per layer)
    [InlineData("r4MixHopOk", "")] // V, its twin: past hop.0 only a u8
    public void TheReadingKeyPastAHolderReadsEveryLayer(string form, string expected)
    {
        var c = MirBuildTest.Compile(PastHolderForms[form] + "\npublic func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes))}]");
    }
}
