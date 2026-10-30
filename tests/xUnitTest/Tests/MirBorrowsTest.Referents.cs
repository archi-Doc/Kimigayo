// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's referent stores (SPEC 4.9.6, 15.6.4; DECISIONS R4-M143, BU-42): an operation of SPEC 4.6 to 4.9 writes through its
/// exclusive receiver only the storage that receiver refers to, so its store reaches the receiver's referents and no holder past them,
/// where the referent destroys shallowly; where the referent may observe a destruction, which the row leaves to its callers, W stays
/// (PLAN G264).</summary>
public sealed partial class MirBorrowsTest
{
    // A writer whose destruction stores through its raw slot (SPEC 16.3), and a consuming callee.
    private const string WriterShapes = """
        struct Writer
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        func consume<T>(x: T) => ()

        """;

    // A raw-linked node; the writer (WriterShapes) in a struct and by a generic maker; an open class whose leaf's destruction does the
    // same, held through its base's handle.
    private const string ReferentShapes = WriterShapes + """
        struct Node
            public var next: raw/Node
            public var data: raw/u8
            public init(next: raw/Node, data: raw/u8)
                self.next = next
                self.data = data
        struct Wrap
            public var w: Writer
            public init(w: Writer) => self.w = w@move
        contract MakeW
            func make(p: raw/(raw/u8), q: raw/u8) -> Self
        open struct WBase
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
        struct WLeaf : WBase
            public init(q: raw/u8) : base(q: q) => ()
            drop
                unsafe => *self.p = self.q
        func setData(a: uniq/Array<raw/Node>, d: raw/u8)
            let p = a[0]
            unsafe => (*p).data = d

        """;

    // SPEC 4.9.6, 15.6.4: an Array's append (arrNodeAppendOk, appendNullOk, appendIntoOk, appendMiddleOk) and literal (arrNode1Ok, rawPairOk:
    // one aggregate since DECISIONS R5-M129, which writes no receiver store), pop (pop2Ok), an explicit reserve over a non-empty Array
    // (reserveLateOk; a literal reserves nothing since R5-M129), swap (arrSwapOk),
    // a Dictionary's insertOrReplace (dictInsertOk), an Array of exclusive references (safeElemOk), a fixed array's sliceUniq (fixedSliceOk)
    // and a UniqSlice's indexUniq (uniqSliceIndexOk) write only their receiver's element storage, so a Loan an element's pointee keeps is not
    // written into the receiver's other holders: each is valid, and its twin that uses the elements after `y = 5` (`t = q@ref`) errs there.
    // A store past the referent stays W's: a receiver reaching E (appendParamBad) and a user function's store (userWriteBad).
    [Fact]
    public void AReferentStoreWritesOnlyWhatItsReceiverRefersTo()
        => Assert.Equal(
            [
                "arrNode1Bad: y = 5", "arrNodeAppendBad: y = 5", "rawPairBad: y = 5", "appendMiddleBad: y = 5", "pop2Bad: y = 5",
                "reserveLateBad: y = 5", "arrSwapBad: y = 5", "dictInsertBad: y = 5", "safeElemBad: t = q@ref", "fixedSliceBad: y = 5",
                "appendParamBad: y = 5", "userWriteBad: y = 5",
            ],
            Problems(RawShapes + ReferentShapes + """
                func arrNode1Ok()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    let a = [n@raw]
                    inspect(a)
                    y = 5
                func arrNode1Bad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    let a = [n@raw]
                    y = 5
                    inspect(a)
                func arrNodeAppendOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var a: Array<raw/Node> = []
                    a.append(m@raw)
                    a.append(n@raw)
                    inspect(a)
                    y = 5
                func arrNodeAppendBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var a: Array<raw/Node> = []
                    a.append(m@raw)
                    a.append(n@raw)
                    y = 5
                    inspect(a)
                func rawPairOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h2: raw/u8 = w@raw@raw/u8
                    var h1: raw/(raw/u8) = h2@raw
                    let a = [h1, h1]
                    inspect(a)
                    y = 5
                func rawPairBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h2: raw/u8 = w@raw@raw/u8
                    var h1: raw/(raw/u8) = h2@raw
                    let a = [h1, h1]
                    y = 5
                    inspect(a)
                func appendNullOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h: raw/u8 = w@raw@raw/u8
                    var a: Array<raw/(raw/u8)> = []
                    a.append(h@raw)
                    a.append(null)
                    h = null
                    y = 5
                func appendIntoOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h: raw/u8 = w@raw@raw/u8
                    var g: raw/u8 = null
                    var a: Array<raw/(raw/u8)> = []
                    a.append(h@raw)
                    a.append(g@raw)
                    h = null
                    y = 5
                    let used = g == null
                func appendMiddleOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var a: Array<raw/Node> = []
                    a.append(m@raw)
                    a.append(n@raw)
                    inspect(a)
                    y = 5
                    inspect(m)
                func appendMiddleBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    m.next = n@raw
                    var a: Array<raw/Node> = []
                    a.append(m@raw)
                    inspect(a)
                    y = 5
                    inspect(m)
                func pop2Ok()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h: raw/u8 = w@raw@raw/u8
                    var g: raw/u8 = null
                    var a: Array<raw/(raw/u8)> = [h@raw, g@raw]
                    let x = a.pop()
                    let z = a.pop()
                    inspect(x)
                    inspect(z)
                    h = null
                    y = 5
                func pop2Bad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h: raw/u8 = w@raw@raw/u8
                    var g: raw/u8 = null
                    var a: Array<raw/(raw/u8)> = [h@raw, g@raw]
                    let x = a.pop()
                    let z = a.pop()
                    y = 5
                    inspect(x)
                    inspect(z)
                func reserveLateOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h: raw/u8 = null
                    var a: Array<raw/(raw/u8)> = []
                    a.append(h@raw)
                    h = w@raw@raw/u8
                    a.reserve(4)
                    let x = a.pop()
                    inspect(a)
                    h = null
                    inspect(x)
                    y = 5
                func reserveLateBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h: raw/u8 = null
                    var a: Array<raw/(raw/u8)> = []
                    a.append(h@raw)
                    h = w@raw@raw/u8
                    a.reserve(4)
                    let x = a.pop()
                    inspect(a)
                    y = 5
                    inspect(x)
                func arrSwapOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var a: Array<raw/Node> = []
                    a.append(m@raw)
                    a.append(n@raw)
                    a.swap(0, 1)
                    inspect(a)
                    y = 5
                    inspect(m)
                func arrSwapBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var a: Array<raw/Node> = []
                    a.append(m@raw)
                    a.append(n@raw)
                    a.swap(0, 1)
                    y = 5
                    inspect(a)
                func dictInsertOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var d: Dictionary<i32, raw/Node> = [:]
                    let r1 = d.insertOrReplace(1, m@raw)
                    let r2 = d.insertOrReplace(2, n@raw)
                    inspect(d)
                    y = 5
                func dictInsertBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var d: Dictionary<i32, raw/Node> = [:]
                    let r1 = d.insertOrReplace(1, m@raw)
                    let r2 = d.insertOrReplace(2, n@raw)
                    y = 5
                    inspect(d)
                func safeElemOk()
                    var z: i32 = 1
                    var q: i32 = 2
                    var s: ref/i32 = z@ref
                    var t: ref/i32 = z@ref
                    var a: Array<uniq/(ref/i32)> = []
                    a.append(s@uniq)
                    a.append(t@uniq)
                    inspect(a)
                    t = q@ref
                    inspect(s)
                func safeElemBad()
                    var z: i32 = 1
                    var q: i32 = 2
                    var s: ref/i32 = z@ref
                    var t: ref/i32 = z@ref
                    var a: Array<uniq/(ref/i32)> = []
                    a.append(s@uniq)
                    a.append(t@uniq)
                    t = q@ref
                    inspect(a)
                func fixedSliceOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var fa: [2 of raw/Node] = [m@raw, n@raw]
                    let s = fa.sliceUniq()
                    inspect(s)
                    y = 5
                    inspect(m)
                func fixedSliceBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var fa: [2 of raw/Node] = [m@raw, n@raw]
                    let s = fa.sliceUniq()
                    inspect(s)
                    y = 5
                    inspect(fa)
                func uniqSliceIndexOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    var m = Node.init(next: null, data: null)
                    var a: Array<raw/Node> = []
                    a.append(m@raw)
                    a.append(n@raw)
                    var s = a.sliceUniq()
                    let k: isize = 0
                    let e = s.indexUniq(k@ref)
                    inspect(e)
                    y = 5
                    inspect(m)
                func appendParamBad(a: uniq/Array<raw/Node>)
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    n.data = w@raw@raw/u8
                    a.append(n@raw)
                    y = 5
                func userWriteBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var n = Node.init(next: null, data: null)
                    var a: Array<raw/Node> = []
                    a.append(n@raw)
                    setData(a@uniq, w@raw@raw/u8)
                    y = 5
                    inspect(n)
                """));

    // The destruction guard (DECISIONS R4-M143; PLAN G264): where a referent store's referent may observe a destruction, which the row
    // leaves to its callers (SPEC 4.9.6, 16.3), W stays. clear over an object whose leaf's destruction stores through its raw slot, held
    // through its base's handle (whose payload is not proven Sealed), errs at `y = 5` (clearObjBad), as its twin does where the slot is
    // used before (clearObjOk, a correct error by SPEC 15.6, the owner's 「(B)」: the payload holds `WLeaf.init`'s result, which reaches
    // storage the body does not own, so `clear`'s erased destruction may store y's Loan there); a destruction a struct, an Option, a
    // Tuple, a nested Array or a Dictionary runs keeps its error (clearLeafBad, clearWrapBad, clearOptBad, clearTupBad, clearArrBad,
    // dictClearBad, dictReplaceBad); over an abstract `T` not proven Copy, whose instance decides, W past refs(S) is undecided where it
    // would store a Loan or an address (R4-M147): `a.clear()`, whose receiver holds the element's addresses, is the located
    // Unsupported_Kd and stores into refs(S) alone, so clearGenOk reports nothing else and clearGenBad its true `y = 5` (append met an
    // empty Array); where it would store only Φ_E, W stays and changes nothing (reversedGen, Array.appendAll's form, valid). The
    // classifier answers per definition: one generic struct's element Type destroys shallowly in a method proving it Copy (copied, valid)
    // and not in one that does not (kept: the record, and the true `y = 5` its Array's later drop causes).
    [Fact]
    public void ADestructionAReferentMayObserveKeepsW()
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + ReferentShapes + """
            func clearObjBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var o: obj/WLeaf = Kimi.Intrinsics.makeObj<WLeaf>(WLeaf.init(q: w@raw@raw/u8))
                o.p = slot@raw
                var b: obj/WBase = o@move@obj/WBase
                var a: Array<obj/WBase> = []
                a.append(b@move)
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func clearObjOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var o: obj/WLeaf = Kimi.Intrinsics.makeObj<WLeaf>(WLeaf.init(q: w@raw@raw/u8))
                o.p = slot@raw
                var b: obj/WBase = o@move@obj/WBase
                var a: Array<obj/WBase> = []
                a.append(b@move)
                a.clear()
                consume(a@move)
                usep(slot)
                y = 5
            func clearLeafBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var o: obj/WLeaf = Kimi.Intrinsics.makeObj<WLeaf>(WLeaf.init(q: w@raw@raw/u8))
                o.p = slot@raw
                var a: Array<obj/WLeaf> = []
                a.append(o@move)
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func clearWrapBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var a: Array<Wrap> = []
                a.append(Wrap.init(w: x@move))
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func clearOptBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var a: Array<Option<Writer>> = []
                a.append(.Some(x@move))
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func clearTupBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var a: Array<(Writer, i32)> = []
                a.append((x@move, 1))
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func clearArrBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var inner: Array<Writer> = []
                inner.append(x@move)
                var a: Array<Array<Writer>> = []
                a.append(inner@move)
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func dictClearBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var d: Dictionary<i32, Writer> = [:]
                _ = d.tryInsert(1, x@move)
                d.clear()
                consume(d@move)
                y = 5
                usep(slot)
            func dictReplaceBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var d: Dictionary<i32, Writer> = [:]
                _ = d.tryInsert(1, x@move)
                _ = d.insertOrReplace(1, Writer.init(q: null))
                consume(d@move)
                y = 5
                usep(slot)
            func clearGenBad<T>()
                T is MakeW
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var a: Array<T> = []
                a.append(T.make(p: slot@raw, q: w@raw@raw/u8))
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func clearGenOk<T>()
                T is MakeW
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var a: Array<T> = []
                a.append(T.make(p: slot@raw, q: w@raw@raw/u8))
                a.clear()
                consume(a@move)
                usep(slot)
                y = 5
            func reversedGen<T>(other: Array<T>)
                var source = other@move
                source.reverse()
            struct Gen<E>
                E is MakeW
                public init() => ()
                public func copied(self: ref/Self)
                    E is Copy
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var a: Array<E> = []
                    a.append(E.make(p: slot@raw, q: w@raw@raw/u8))
                    a.clear()
                    let gone = a@move
                    let used = slot == null
                    y = 5
                public func kept(self: ref/Self)
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var a: Array<E> = []
                    a.append(E.make(p: slot@raw, q: w@raw@raw/u8))
                    a.clear()
                    let gone = a@move
                    let used = slot == null
                    y = 5
            """);
        Assert.Equal(
            [
                "kept: a.clear() Unsupported_Kd", "kept: y = 5", "clearObjBad: y = 5", "clearObjOk: y = 5", "clearLeafBad: y = 5", "clearWrapBad: y = 5",
                "clearOptBad: y = 5", "clearTupBad: y = 5", "clearArrBad: y = 5", "dictClearBad: y = 5", "dictReplaceBad: y = 5",
                "clearGenBad: a.clear() Unsupported_Kd", "clearGenBad: y = 5", "clearGenOk: a.clear() Unsupported_Kd",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // DECISIONS R5-M129: an Array literal is one aggregate, which places its elements at construction through no receiver store, so their
    // Loans reach only the Array: litPairOk, a generic literal of a pair layer's value removed again, is valid (fail-before: it erred at
    // `v@follow = x`, the literal's append keeping W). litDropOk, lit2DropOk (another element first) and litObjOk (an object through its
    // base's handle), whose element's drop stores through its raw slot, consumed, then the slot used before `y = 5`, are correct errors
    // at `y = 5` by SPEC 15.6 (the owner's 「(B)」 of 2026-10-30, PLAN G264 S1; DECISIONS BU-67): the element, an untied constructor's
    // result (Writer.init's or WLeaf.init's), reaches storage the body does not own through the whole holder, also after its raw Field
    // was rewritten, so the consuming call's cleanup (PLAN G266) may store y's Loan there. The default pipeline's old analysis reports no
    // conflict at `y = 5`.
    [Fact]
    public void AnArrayLiteralPlacesItsElementsThroughNoReceiverStore()
        => Assert.Equal(["litDropOk: y = 5", "lit2DropOk: y = 5", "litObjOk: y = 5"], Problems(RawShapes + ReferentShapes + """
            func litDropOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var a: Array<Writer> = [x@move]
                consume(a@move)
                usep(slot)
                y = 5
            func lit2DropOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var z = Writer.init(q: null)
                var a: Array<Writer> = [z@move, x@move]
                consume(a@move)
                usep(slot)
                y = 5
            func litObjOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var o: obj/WLeaf = Kimi.Intrinsics.makeObj<WLeaf>(WLeaf.init(q: w@raw@raw/u8))
                o.p = slot@raw
                var b: obj/WBase = o@move@obj/WBase
                var a: Array<obj/WBase> = [b@move]
                consume(a@move)
                usep(slot)
                y = 5
            func litPairOk<s/T>(value: s/T, x: T) -> ()
                s is owner or uniq
                T is Copy
                var v = value@move
                let child = v
                do
                    var arr: Array<s/T> = [child@move]
                    let e = arr@uniq.remove(0)
                    _ = e@follow
                v@follow = x

            """));

    // V16 (DECISIONS R4-M143): N appends of distinct Nodes' addresses onto one Array<raw/Node>. Each append's store reaches the receiver's
    // referent only, so the work's growth per doubling stays within the receiver's states the rows read (on dev each append's W passes wrote
    // the receiver's growing chain into every element's holder, ×7 per doubling).
    [Fact]
    public void TheReferentStoresStayDense()
    {
        const string Shape = "func shape()\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n«N:    var n{0} = Node.init(next: null, data: null)\n»" +
            "    n0.data = w@raw@raw/u8\n    var a: Array<raw/Node> = []\n«N:    a.append(n{0}@raw)\n»    inspect(a)\n    y = 5\n";
        long[] work = [.. new[] { 32, 64, 128 }.Select(n => this.Measured(Prelude + RawShapes + ReferentShapes + Expand(Shape, n, 0)).Work)];
        output.WriteLine($"N 32, 64, 128: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 4), $"work {string.Join(", ", work)}");
    }
}
