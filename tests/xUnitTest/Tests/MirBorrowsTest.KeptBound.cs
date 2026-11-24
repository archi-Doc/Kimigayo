// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>The kept-Type record bounds an input's reads by the Type the call binds (SPEC 15.6.4's first sentence, "determined by the
/// instantiated public contract", and its read sentences; SPEC 15.6's predicate; the owner's C-527 Q1 (a) and Q2 (b), O-353 (a), O-360 (a);
/// PLAN G841 clause (3)): at the root of a binding of the called function's own abstract Type, a read or chain of reads through an
/// input is kept only where a value of the bound Type can carry a Loan or an address; the input's own value and a handed callable's fill
/// stay in the callee's terms for every binding (F's G935 ruling 16 (2)). The rows are C-527's coverage table, each labelled from SPEC (I
/// invalid, V valid) before any run, with the records the head gives (SPEC 23.1.2).</summary>
public sealed partial class MirBorrowsTest
{
    private static readonly Dictionary<string, string> KeptBoundForms = new()
    {
        ["oneLayer"] = """
            func keep1(slot: raw/(raw/u8), value: raw/u8) => ()
            func oneLayer(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                keep1(cell@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["keepS1OneOk"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func oneLayerShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                keepS1(cell@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["keepS1RawBad"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func oneLayerRaw(y: uniq/i32)
                let w = y@follow@ref
                var c: raw/u8 = w@raw@raw/u8
                var cell: raw/(raw/u8) = null
                keepS1(cell@raw, c@raw)
                y@follow = 5
            """,
        ["keepHalfTBad"] = """
            func keepHT<T>(slot: raw/(raw/T), value: T) => ()
            func twoLayersHalfT(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepHT(hop@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["abstractBad"] = """
            func stT<T>(t: uniq/(raw/((T, u64))), v: T) => ()
            func abstractBad()
                var y: i64 = 2
                var c2: raw/((ref/i64, u64)) = null
                stT(c2@uniq, y@ref)
                y = 5
            """,
        ["keepPairTBad"] = """
            func keepP<T>(slot: raw/(raw/T), value: (T, raw/u8)) => ()
            func pairShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/i32 = null
                keepP(cell@raw, (0, w@raw@raw/u8))
                y@follow = 5
            """,
        ["tripleDuringShownBad"] = """
            func keepQ<T>(slot: raw/(raw/T), value: (T, ref/i32 during a, raw/u8)) => ()
            func tripleDuringShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/i32 = null
                keepQ(cell@raw, (0, w, w@raw@raw/u8))
                y@follow = 5
            """,
        ["tripleDuringCtlOk"] = """
            func keepQ1(slot: raw/(raw/i32), value: (i32, ref/i32 during a, raw/u8)) => ()
            func tripleDuringShownCtl(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/i32 = null
                keepQ1(cell@raw, (0, w, w@raw@raw/u8))
                y@follow = 5
            """,
        ["keepRefTOk"] = """
            func keepR<T>(slot: raw/(raw/T), value: ref/T) => ()
            func refShown(y: uniq/i32)
                let w = y@follow@ref
                let pair = (w, 0)
                var cell: raw/i32 = null
                keepR(cell@raw, pair.1@ref)
                y@follow = 5
            """,
        ["keepValueTBad"] = """
            func keepV<T>(slot: raw/(raw/T), value: T) => ()
            func valueShown(y: uniq/i32)
                let w = y@follow@ref
                let pair = (w, 0)
                var cell: raw/i32 = null
                keepV(cell@raw, pair.1)
                y@follow = 5
            """,
        ["tupShownBad"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func tupShown(y: uniq/i32)
                let w = y@follow@ref
                let b = (w, 0)
                var cell: raw/((ref/i32, i32)) = null
                keepS1(cell@raw, b@raw)
                y@follow = 5
            """,
        ["deepTupShownBad"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func deepTupShown(y: uniq/i32)
                let w = y@follow@ref
                let b = (1, (2, (w, 3)))
                var cell: raw/((i32, (i32, (ref/i32, i32)))) = null
                keepS1(cell@raw, b@raw)
                y@follow = 5
            """,
        ["structShownBad"] = """
            struct RefBox {a}
                public let r: ref/i32 during a
                public init(r: ref/i32 during a)
                    self.r = r
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func structShown(y: uniq/i32)
                let w = y@follow@ref
                let b = RefBox.init(r: w)
                var cell: raw/RefBox = null
                keepS1(cell@raw, b@raw)
                y@follow = 5
            """,
        ["deepStructShownBad"] = """
            struct RefBox {a}
                public let r: ref/i32 during a
                public init(r: ref/i32 during a)
                    self.r = r
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func deepStructShown(y: uniq/i32)
                let w = y@follow@ref
                let b = (1, RefBox.init(r: w))
                var cell: raw/((i32, RefBox)) = null
                keepS1(cell@raw, b@raw)
                y@follow = 5
            """,
        ["holderShownOk"] = """
            struct Holder<T>
                public var value: T
                public init(value: T)
                    self.value = value@move
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func holderShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/Holder<u8> = null
                keepS1(cell@raw, w@raw@raw/Holder<u8>)
                y@follow = 5
            """,
        ["enumShownBad"] = """
            enum EB {a}
                Num(i32)
                Ref(ref/i32 during a)
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func enumShown(y: uniq/i32)
                let w = y@follow@ref
                let b = EB.Ref(w)
                var cell: raw/EB = null
                keepS1(cell@raw, b@raw)
                y@follow = 5
            """,
        ["enumFreeShownOk"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func enumFreeShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/Option<u8> = null
                keepS1(cell@raw, w@raw@raw/Option<u8>)
                y@follow = 5
            """,
        ["neverShownOk"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func neverShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/Never = null
                keepS1(cell@raw, w@raw@raw/Never)
                y@follow = 5
            """,
        ["fixedArrShownBad"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func fixedArrShown(y: uniq/i32)
                let w = y@follow@ref
                let b = [2 of w]
                var cell: raw/[2 of ref/i32] = null
                keepS1(cell@raw, b@raw)
                y@follow = 5
            """,
        ["abstractShownBad"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func abstractShown<U>(y: uniq/i32, x: U)
                let w = y@follow@ref
                let pair = (w, x@move)
                var cell: raw/U = null
                keepS1(cell@raw, pair.1@raw)
                y@follow = 5
            """,
        ["nestedShownOk"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func midS<U>(slot: raw/(raw/U), value: raw/U)
                keepS1(slot, value)
            func nestedShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                midS(cell@raw, w@raw@raw/u8)
                y@follow = 5
            """,
        ["nestedRawBad"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func midS<U>(slot: raw/(raw/U), value: raw/U)
                keepS1(slot, value)
            func nestedRaw(y: uniq/i32)
                let w = y@follow@ref
                var c: raw/u8 = w@raw@raw/u8
                var cell: raw/(raw/u8) = null
                midS(cell@raw, c@raw)
                y@follow = 5
            """,
        ["nestedTupOk"] = """
            func keepS1<T>(slot: raw/(raw/T), value: raw/T) => ()
            func midT<U>(slot: raw/(raw/((U, u8))), value: raw/((U, u8)))
                keepS1(slot, value)
            func nestedTup(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/((u8, u8)) = null
                midT(cell@raw, w@raw@raw/((u8, u8)))
                y@follow = 5
            """,
        ["optResultOk"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useOU(x: Option<u8>) => ()
            func optResultShown(y: uniq/i32)
                let w = y@follow@ref
                let r = mkOT(w@raw@raw/u8)
                y@follow = 5
                useOU(r)
            """,
        ["optResultRawBad"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useOR(x: Option<raw/u8>) => ()
            func optResultRaw(y: uniq/i32)
                let w = y@follow@ref
                var c: raw/u8 = w@raw@raw/u8
                let r = mkOT(c@raw)
                y@follow = 5
                useOR(r)
            """,
        ["optResultRefBad"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useAny<U>(x: ref/U) => ()
            func optResultRef(y: uniq/i32)
                let w = y@follow@ref
                let r = mkOT(w@raw)
                y@follow = 5
                useAny(r@ref)
            """,
        ["itemResultOk"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func inc(x: i32) -> i32 => x + 1
            func useAny<U>(x: ref/U) => ()
            func itemResult(y: uniq/i32)
                let w = y@follow@ref
                let pair = (w, inc)
                let r = mkOT(pair.1@raw)
                y@follow = 5
                useAny(r@ref)
            """,
        ["closureOwnedResultOk"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useAny<U>(x: ref/U) => ()
            func closureOwnedResult(y: uniq/i32)
                let w = y@follow@ref
                let n: i32 = 3
                let c = func [n] () => ()
                let pair = (w, c@move)
                let r = mkOT(pair.1@raw)
                y@follow = 5
                useAny(r@ref)
            """,
        ["closureEmptyResultOk"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useAny<U>(x: ref/U) => ()
            func closureEmptyResult(y: uniq/i32)
                let w = y@follow@ref
                let c = func [] () => ()
                let pair = (w, c@move)
                let r = mkOT(pair.1@raw)
                y@follow = 5
                useAny(r@ref)
            """,
        ["closureResultBad"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useAny<U>(x: ref/U) => ()
            func closureResult(y: uniq/i32)
                let w = y@follow@ref
                var k: i32 = 0
                let c = func [k@ref] () => ()
                let pair = (w, c@move)
                let r = mkOT(pair.1@raw)
                y@follow = 5
                useAny(r@ref)
            """,
        ["closureRawCapBad"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useAny<U>(x: ref/U) => ()
            func closureRawCap(y: uniq/i32)
                let w = y@follow@ref
                let p: raw/u8 = null
                let c = func [p] () => ()
                let pair = (w, c@move)
                let r = mkOT(pair.1@raw)
                y@follow = 5
                useAny(r@ref)
            """,
        ["closureTupleCapOk"] = """
            func mkOT<T>(value: raw/T) -> Option<T> => .None
            func useAny<U>(x: ref/U) => ()
            func closureTupleCap(y: uniq/i32)
                let w = y@follow@ref
                let t = (1, 2)
                let c = func [t] () => ()
                let pair = (w, c@move)
                let r = mkOT(pair.1@raw)
                y@follow = 5
                useAny(r@ref)
            """,
        ["closureValueResultBad"] = """
            func mkV<T>(value: T) -> Option<T> => .None
            func useAny<U>(x: ref/U) => ()
            func closureValueResult(y: uniq/i32)
                let w = y@follow@ref
                let n: i32 = 3
                let c = func [n] () => ()
                let pair = (w, c)
                let r = mkV(pair.1)
                y@follow = 5
                useAny(r@ref)
            """,
        ["fillShownBad"] = """
            func keepF<T>(slot: raw/(raw/T), value: raw/u8, make: (raw/u8) -> T) => ()
            func toI(p: raw/u8) -> i32 => 0
            func fillShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/i32 = null
                keepF(cell@raw, w@raw@raw/u8, toI)
                y@follow = 5
            """,
        ["fillShownRawBad"] = """
            func keepF<T>(slot: raw/(raw/T), value: raw/u8, make: (raw/u8) -> T) => ()
            func toP(p: raw/u8) -> raw/u8 => p
            func fillShownRaw(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/(raw/u8) = null
                keepF(cell@raw, w@raw@raw/u8, toP)
                y@follow = 5
            """,
        ["fillRefShownBad"] = """
            func keepFR<T>(slot: raw/(raw/T), value: (ref/T during a, u8), make: (u8) -> T) => ()
            func toI(n: u8) -> i32 => 0
            func fillRefShown(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/i32 = null
                keepFR(cell@raw, (w, 7), toI)
                y@follow = 5
            """,
        ["fillRefCtlOk"] = """
            func keepFR1(slot: raw/(raw/i32), value: (ref/i32 during a, u8), make: (u8) -> i32) => ()
            func toI(n: u8) -> i32 => 0
            func fillRefShownCtl(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/i32 = null
                keepFR1(cell@raw, (w, 7), toI)
                y@follow = 5
            """,
        ["mkConcWnBad"] = """
            func look<T>(value: ref/T) => ()
            contract MakeW
                func make(p: raw/(raw/u8), q: raw/u8) -> Self
            struct Wn
                Self is MakeW
                public var n: u64
                public init() => self.n = 0
                public func make(p: raw/(raw/u8), q: raw/u8) -> Self => Wn.init()
            func f<T>(dst: uniq/T, p: raw/u8)
                T is MakeW
                return
            func mkConcWnBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var x = Wn.init()
                f(x@uniq, w@raw@raw/u8)
                y = 5
                look(x@ref)
            """,
    };

    // C-527's rows (F\restart12\c527\labels.tsv and the design reviewer's rv\labels-rv.tsv, labelled from SPEC before any run). A V row
    // whose kept T is bound to a Type no value of which can carry a Loan or an address is a class-3 move (rejected at `y@follow = 5` on
    // dev dd138750b, the fail-first base: HiddenKinds kept the read through `value` whatever the binding); the I rows keep their records.
    [Theory]
    [InlineData("oneLayer", "")] // V, SPEC 15.6.4's example: the layer below cell admits only a u8
    [InlineData("keepS1OneOk", "")] // V, C-527's reproducer: oneLayer with T = u8 (15.6.4 "determined by the instantiated public contract"; owner C-527 Q1 (a)); a move
    [InlineData("keepS1RawBad", "oneLayerRaw: y@follow = 5")] // I, T = raw/u8: the layer below cell is a raw layer the call writes, keeping c's and w's addresses (15.6.4)
    [InlineData("keepHalfTBad", "twoLayersHalfT: y@follow = 5")] // I, value a T = raw/u8 stored at T behind the call's own pointer (15.6.4 composition; O-353 (a))
    [InlineData("abstractBad", "abstractBad: y = 5")] // I, T = ref/i64: v, a T, stored in (T, u64).0 behind the call's own pointer (O-353 (a))
    [InlineData("keepPairTBad", "pairShown: y@follow = 5")] // I, the Value row: an i32 storage holds value's i32 part, so value's own value (w's address) is kept (15.6.4; O-359 (a))
    [InlineData("tripleDuringShownBad", "tripleDuringShown: y@follow = 5")] // I, HiddenKinds' raw-input Value row at a binding that can carry nothing (T = i32): value holds T by value beside a borrow and a raw part, and its own value stays in the callee's terms (O-360 (a)'s first half, BU-231; Q1 (a) bounds reads only)
    [InlineData("tripleDuringCtlOk", "")] // V, its non-generic control: the i32 storage is Origin-free as a whole, so it keeps none of an input whose Type states an Origin (O-435 (a))
    [InlineData("keepRefTOk", "")] // V, a borrowed input: i32 storage retains no read through value (15.6.4: no Origin inside i32); a move
    [InlineData("keepValueTBad", "valueShown: y@follow = 5")] // I, a by-value T read from a holder carries its Loans (15.6 Holders; 15.6.4 own value)
    [InlineData("tupShownBad", "tupShown: y@follow = 5")] // I, T = (ref/i32, i32): may retain an Origin inside value's pointee (15.6.4)
    [InlineData("deepTupShownBad", "deepTupShown: y@follow = 5")] // I, a borrow three levels down, the same
    [InlineData("structShownBad", "structShown: y@follow = 5")] // I, T a struct with a borrow Field, the same
    [InlineData("deepStructShownBad", "deepStructShown: y@follow = 5")] // I, that struct inside a Tuple, the same
    [InlineData("holderShownOk", "")] // V, T = Holder<u8>: no Origin, raw layer, callable or abstract part (oneLayer); a move
    [InlineData("enumShownBad", "enumShown: y@follow = 5")] // I, T an enum with a borrow Case: an Origin inside the pointee (15.6.4)
    [InlineData("enumFreeShownOk", "")] // V, T = Option<u8> (oneLayer); a move
    [InlineData("neverShownOk", "")] // V, T = Never (3.1.3; oneLayer); a move
    [InlineData("fixedArrShownBad", "fixedArrShown: y@follow = 5")] // I, T = [2 of ref/i32], the same as tupShownBad
    [InlineData("abstractShownBad", "abstractShown: y@follow = 5")] // I, T bound to the caller's abstract U: checked for every U (8.15.1); a value of an abstract Type can carry a Loan (15.6)
    [InlineData("nestedShownOk", "")] // V, midS's contract is its signature; with U = u8 it is oneLayer (15.6.4); a move
    [InlineData("nestedRawBad", "nestedRaw: y@follow = 5")] // I, the same with U = raw/u8 (keepS1RawBad through midS)
    [InlineData("nestedTupOk", "")] // V, T bound to (U, u8) inside midT (8.15.1); accepted on dev too
    [InlineData("optResultOk", "")] // V, the result Option<u8> retains no read through value (15.6.4); a move
    [InlineData("optResultRawBad", "optResultRaw: y@follow = 5")] // I, T = raw/u8: the payload is a raw layer the call writes in its result (15.6.4)
    [InlineData("optResultRefBad", "optResultRef: y@follow = 5")] // I, T = ref/i32: an Origin inside value's pointee (15.6.4)
    [InlineData("itemResultOk", "")] // V, T a Function Item Type: a Function Item value carries no Loan (15.6); a move
    [InlineData("closureOwnedResultOk", "")] // V, T a Closure capturing an owned i32: its captures hold no borrow (owner C-527 Q2 (b); 3.2.1, 15.8.2); a move
    [InlineData("closureEmptyResultOk", "")] // V, a Closure capturing nothing, the same; a move
    [InlineData("closureResultBad", "closureResult: y@follow = 5")] // I, a Closure capturing k@ref: its Type holds k's Origin (15.8.2), so the result keeps the read, pair's Loans
    [InlineData("closureRawCapBad", "closureRawCap: y@follow = 5")] // I, a Closure capturing an owned raw/u8 carries what its captures hold, an address (Q2 (b)): the raw layer the call writes in its result keeps the read (15.6.4)
    [InlineData("closureTupleCapOk", "")] // V, a Closure capturing an owned (i32, i32) carries nothing (Q2 (b); 3.2.1, 15.8.2); a move
    [InlineData("closureValueResultBad", "closureValueResult: y@follow = 5")] // I, the Value row stays: a T by value read from pair carries its Loans, and Option<C> holds a C (15.6.4 argument sentence; O-360 (a)'s first half)
    [InlineData("fillShownBad", "fillShown: y@follow = 5")] // I, a handed callable's fill stays in the callee's terms for every binding (G935 ruling 16 (2), BU-231)
    [InlineData("fillShownRawBad", "fillShownRaw: y@follow = 5")] // I, the same with T = raw/u8
    [InlineData("fillRefShownBad", "fillRefShown: y@follow = 5")] // I, the fill of an Origin-carrying input's Origin-free part: its R gate reads the callee's-terms row (C-527 B1)
    [InlineData("fillRefCtlOk", "")] // V, its non-generic control: the i32 storage is Origin-free (O-435 (a))
    [InlineData("mkConcWnBad", "mkConcWnBad: y = 5")] // I, T = Wn (a u64 struct) by the callable route in f's terms (G935 ruling 16 (2))
    public void AKeptTypesReadsAreKeptOnlyWhereItsBindingCanCarryALoan(string form, string expected)
    {
        var c = MirBuildTest.Compile(KeptBoundForms[form] + "\npublic func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes))}]");
    }
}
