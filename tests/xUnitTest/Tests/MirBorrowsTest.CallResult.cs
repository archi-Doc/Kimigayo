// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's rule 4 at a call's result (PLAN G841 clause (3); SPEC 15.6.4 "a raw layer that the call may write, in its result, a
/// nominal Type's Fields included, ... reaches storage the body does not own", 5.7 Calls; the owner's O-365 (a) and O-524 (a)): where the
/// call composes, it may write its own pointer at a raw layer without an Origin of its result and store through it at the next layer,
/// in storage the body does not own, what that layer's entry of the result's record keeps; a value stored there may be used after
/// every point, whether the result is used, discarded or passed on. A layer the result's Type expression writes (a bare raw result, a
/// Tuple or array element, an Option payload, a Type argument an instance's Field holds) bounds that store to what its pointee admits;
/// through a layer the result's declaration states (a nominal Type's Field or Case payload) the call may store what a raw layer it
/// writes retains, whatever its pointee (SPEC 5.7 Calls and its example throughField; DECISIONS R4-M241). A layer behind a borrow in
/// the result is lent, not written. The
/// rows are the unit's coverage table, each labelled from SPEC (I invalid, V valid) before any run, with the records the head gives
/// (SPEC 23.1.2).</summary>
public sealed partial class MirBorrowsTest
{
    private static readonly Dictionary<string, string> CallResultForms = new()
    {
        ["resultTwoBad"] = """
            func mk2(value: raw/u8) -> raw/(raw/u8) => null
            func resultTwo(y: uniq/i32)
                let w = y@follow@ref
                let r = mk2(w@raw@raw/u8)
                y@follow = 5
            """,
        ["resultOneOk"] = """
            func mk1(value: raw/u8) -> raw/u8 => null
            func resultOne(y: uniq/i32)
                let w = y@follow@ref
                let r = mk1(w@raw@raw/u8)
                y@follow = 5
            """,
        ["resultStructBad"] = """
            struct Box2
                public var p: raw/(raw/u8)
                public init(p: raw/(raw/u8))
                    self.p = p
            func mkB(value: raw/u8) -> Box2 => Box2.init(p: null)
            func resultStruct(y: uniq/i32)
                let w = y@follow@ref
                let r = mkB(w@raw@raw/u8)
                y@follow = 5
            """,
        ["resultStructU8Bad"] = """
            struct Box4
                public var p: raw/u8
                public init(p: raw/u8)
                    self.p = p
            func mkB4(value: raw/u8) -> Box4 => Box4.init(p: null)
            func resultStructU8(y: uniq/i32)
                let w = y@follow@ref
                let r = mkB4(w@raw@raw/u8)
                y@follow = 5
            """,
        ["valueStructU8Bad"] = """
            struct Box4
                public var p: raw/u8
                public init(p: raw/u8)
                    self.p = p
            func valueStructU8(y: uniq/i32, g: (raw/u8) -> Box4)
                let w = y@follow@ref
                let r = g(w@raw@raw/u8)
                y@follow = 5
            """,
        ["resultHalfOk"] = """
            func mkT<T>(value: raw/u8) -> raw/T => null
            func resultHalf(y: uniq/i32)
                let w = y@follow@ref
                let r = mkT<raw/u8>(w@raw@raw/u8)
                y@follow = 5
            """,
        ["resultHalfTBad"] = """
            func mkTV<T>(value: T) -> raw/T => null
            func resultHalfT(y: uniq/i32)
                let w = y@follow@ref
                let r = mkTV<raw/u8>(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2OptResultBad"] = """
            func mkO(value: raw/u8) -> Option<raw/(raw/u8)> => .None
            func optResult(y: uniq/i32)
                let w = y@follow@ref
                let r = mkO(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2OptResultOk"] = """
            func mkO1(value: raw/u8) -> Option<raw/u8> => .None
            func optResultOne(y: uniq/i32)
                let w = y@follow@ref
                let r = mkO1(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2TupResultBad"] = """
            func mkTup(value: raw/u8) -> (raw/(raw/u8), i32) => (null, 0)
            func tupResult(y: uniq/i32)
                let w = y@follow@ref
                let r = mkTup(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2HolderResultBad"] = """
            struct Holder<T>
                public var value: T
                public init(value: T)
                    self.value = value@move
            func mkH(value: raw/u8) -> Holder<raw/(raw/u8)> => Holder<raw/(raw/u8)>.init(value: null)
            func holderResult(y: uniq/i32)
                let w = y@follow@ref
                let r = mkH(w@raw@raw/u8)
                y@follow = 5
            """,
        ["q1WrapU8Ok"] = """
            struct Wrap<T>
                public var value: T
                public init(value: T)
                    self.value = value@move
            func mkW(value: raw/u8) -> Wrap<raw/u8> => Wrap.init(value: null)
            func wrapU8(y: uniq/i32)
                let w = y@follow@ref
                let r = mkW(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2ShownResultBad"] = """
            func mkS<T>(value: raw/T) -> raw/(raw/T) => null
            func shownResult(y: uniq/i32)
                let w = y@follow@ref
                let r = mkS(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2ShownResultOk"] = """
            func mkS1<T>(value: raw/T) -> raw/T => null
            func shownResultOne(y: uniq/i32)
                let w = y@follow@ref
                let r = mkS1(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2ShownResultRawBad"] = """
            func mkS2<T>(value: raw/T) -> raw/T => null
            func shownResultRaw(y: uniq/i32)
                let w = y@follow@ref
                var c: raw/u8 = w@raw@raw/u8
                let r = mkS2(c@raw)
                y@follow = 5
            """,
        ["h2ClosureResultBad"] = """
            func closureResult(y: uniq/i32, g: (raw/u8) -> raw/(raw/u8))
                let w = y@follow@ref
                let r = g(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2ClosureResultOk"] = """
            func closureResultOne(y: uniq/i32, g: (raw/u8) -> raw/u8)
                let w = y@follow@ref
                let r = g(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2DiscardBad"] = """
            func mk2(value: raw/u8) -> raw/(raw/u8) => null
            func discard(y: uniq/i32)
                let w = y@follow@ref
                mk2(w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2LentOk"] = """
            func lend(s: ref/(raw/(raw/u8)), value: raw/u8) -> ref/(raw/(raw/u8)) during s => s
            func lent(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                let r = lend(hop@ref, w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2StoreThroughOk"] = """
            func mk0() -> raw/(raw/u8) => null
            func storeThroughOther(y: uniq/i32)
                let w = y@follow@ref
                var z: u8 = 0
                let r = mk0()
                unsafe => *r = z@raw
                y@follow = 5
            """,
        ["h2RefPointeeOk"] = """
            func mkR(x: ref/i32, value: raw/u8) -> raw/(ref/i32 during x) => null
            func refPointee(y: uniq/i32)
                let w = y@follow@ref
                var k: i32 = 0
                let r = mkR(k@ref, w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2RefPointeeParamOk"] = """
            func mkR(x: ref/i32, value: raw/u8) -> raw/(ref/i32 during x) => null
            func refPointeeParam(y: uniq/i32, kp: ref/i32)
                let w = y@follow@ref
                let r = mkR(kp, w@raw@raw/u8)
                y@follow = 5
            """,
        ["h2RefPointeeBad"] = """
            func mkR(x: ref/i32, value: raw/u8) -> raw/(ref/i32 during x) => null
            func refPointeeW(y: uniq/i32)
                let w = y@follow@ref
                var z: u8 = 0
                let r = mkR(w, z@raw)
                y@follow = 5
            """,
        ["ctorMinBad"] = """
            struct Quiet
                public var p: raw/(raw/u8)
                public var q: raw/u8
                public init(q: raw/u8)
                    self.p = null
                    self.q = q
            func ctorMin(y: uniq/i32)
                let w = y@follow@ref
                var x = Quiet.init(q: w@raw@raw/u8)
                y@follow = 5
            """,
        ["a1MethodResultBad"] = """
            struct Maker
                public var n: i32
                public init(n: i32)
                    self.n = n
                public func mk(self: ref/Self, value: raw/u8) -> raw/(raw/u8) => null
            func methodResult(y: uniq/i32)
                let w = y@follow@ref
                let m = Maker.init(n: 0)
                let r = m.mk(w@raw@raw/u8)
                y@follow = 5
            """,
        ["methodOneOk"] = """
            struct Maker
                public var n: i32
                public init(n: i32)
                    self.n = n
                public func mk1(self: ref/Self, value: raw/u8) -> raw/u8 => null
            func methodOne(y: uniq/i32)
                let w = y@follow@ref
                let m = Maker.init(n: 0)
                let r = m.mk1(w@raw@raw/u8)
                y@follow = 5
            """,
        ["xResultDeclBad"] = """
            enum Slot
                Has(raw/u8)
                Empty

            func mkS(v: raw/u8) -> Slot => .Empty

            func xResultDeclBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var s = mkS(spare)
                kk = 2
            """,
        ["xResultDeclOtherOk"] = """
            enum Slot
                Has(raw/u8)
                Empty

            func mkS(v: raw/u8) -> Slot => .Empty

            func xResultDeclOtherOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var other: u8 = 3
                let o: raw/u8 = other@raw
                var s = mkS(o)
                kk = 2
            """,
        ["loanFieldWindowBad"] = """
            struct Window<T> {source}
                let loan: Loan<ref/T during source>
                let data: raw/()
                let length: isize
                public init(values: ref/[3 of T] during source)
                    self.loan = Loan<ref/T during source>.init(values[0]@ref)
                    self.data = values[0]@raw@raw/()
                    self.length = 3
                public func get(self: ref/Self, index: isize) -> ref/T during source
                    require 0 <= index and index < self.length else => $abort("index")
                    unsafe => return (self.data@raw/T)[index]@ref
            func loanFieldWindow()
                var numbers: [3 of i32] = [10, 20, 30]
                let window = Window<i32>.init(numbers@ref)
                require window.get(2) == 30 else => $abort("element")
            """,
    };

    [Theory]
    [InlineData("resultTwoBad", "resultTwo: y@follow = 5")] // I: mk2 may write its own pointer at the result's raw layer and store value (w's address) through it in E (15.6.4 composition at the result)
    [InlineData("resultOneOk", "")] // V, its twin: the next layer is u8 (oneLayer); r unused below
    [InlineData("resultStructBad", "resultStruct: y@follow = 5")] // I, the same at a nominal Type's result Field (15.6.4 "a nominal Type's Fields included")
    [InlineData("resultStructU8Bad", "resultStructU8: y@follow = 5")] // I, the Field a raw/u8: p is a layer Box4 states, through which the call may store what a raw layer it writes retains, whatever its pointee (SPEC 5.7 Calls, throughField; R4-M241)
    [InlineData("valueStructU8Bad", "valueStructU8: y@follow = 5")] // I, the same through a Closure value: direct and indirect calls alike (SPEC 5.7 Calls)
    [InlineData("resultHalfOk", "")] // V by the owner's O-353 (a): the shown layer's pointee T holds no argument of another declared Type
    [InlineData("resultHalfTBad", "resultHalfT: y@follow = 5")] // I: value, a T, stored at T behind the call's own pointer (O-353 (a))
    [InlineData("h2OptResultBad", "optResult: y@follow = 5")] // I, through a Case payload of the result (5.7 Calls)
    [InlineData("h2OptResultOk", "")] // V, Option<raw/u8>: the next layer is u8
    [InlineData("h2TupResultBad", "tupResult: y@follow = 5")] // I, through a Tuple element of the result
    [InlineData("h2HolderResultBad", "holderResult: y@follow = 5")] // I, through a generic struct's Field (a Type argument)
    [InlineData("q1WrapU8Ok", "")] // V, its twin: the Field holds the Type argument raw/u8 the result's Type expression writes, which bounds the store to a u8 (SPEC 5.7 Calls)
    [InlineData("h2ShownResultBad", "shownResult: y@follow = 5")] // I, a generic callee's shown result layers (T = u8)
    [InlineData("h2ShownResultOk", "")] // V, raw/T with T = u8: the next layer admits only a u8 (15.6.4 instantiated contract; the kept-Type record's bound, C-527)
    [InlineData("h2ShownResultRawBad", "shownResultRaw: y@follow = 5")] // I, T = raw/u8: the storage behind the call's own pointer is a raw layer it may write, keeping c's and w's addresses (15.6.4 twoLayers)
    [InlineData("h2ClosureResultBad", "closureResult: y@follow = 5")] // I, an indirect call: direct and indirect calls alike
    [InlineData("h2ClosureResultOk", "")] // V, `-> raw/u8`
    [InlineData("h2DiscardBad", "discard: y@follow = 5")] // I, the result discarded: the call still wrote E
    [InlineData("h2LentOk", "")] // V: a shared borrow in the result is lent and reaches no E (R-G841-4)
    [InlineData("h2StoreThroughOk", "")] // V: the body stores a Loan-free local's address through the result
    [InlineData("h2RefPointeeOk", "refPointee: var k: i32 = 0")] // I by the owner's O-524 (a): E may hold x, a Copy of k@ref, used after every point (15.6.4), so the end of k's storage destroys a Place a live ref Loan covers (15.6.2, 15.6.6); no error at y@follow = 5
    [InlineData("h2RefPointeeParamOk", "")] // V, its twin: a parameter's borrow has no access path in the body (15.6 Loans); ref/i32 during x cannot hold w's address
    [InlineData("h2RefPointeeBad", "refPointeeW: y@follow = 5")] // I, x = w: E may hold a Copy of w
    [InlineData("ctorMinBad", "ctorMin: y@follow = 5")] // I, a constructor's result Field (O-365 (a))
    [InlineData("a1MethodResultBad", "methodResult: y@follow = 5; methodResult: let m = Maker.init(n: 0)")] // I, a method's result: E may also hold the receiver's own value, m's shared Loan, used after every point, so the end of m's storage destroys a Place a live ref Loan covers (15.6.4, 15.6.2, 15.6.6; O-524 (a)'s reasoning)
    [InlineData("methodOneOk", "")] // V, its twin, `-> raw/u8`: the next layer is u8 and keeps nothing
    [InlineData("xResultDeclBad", "xResultDeclBad: kk = 2")] // I, the result's Case payload, a layer Slot states: mkS may store spare through its own pointer there (15.6.4; O-365 (a))
    [InlineData("xResultDeclOtherOk", "")] // V, its control: the argument addresses `other`
    [InlineData("loanFieldWindowBad", "loanFieldWindow: var numbers: [3 of i32] = [10, 20, 30]")] // I by the owner's O-528 (a), LoanFieldTest's filed covariant view: Window states data: raw/() without an Origin, so its constructor may store numbers' shared Loan through its own pointer there, used after every point, and the end of numbers' storage conflicts (5.7 Calls, 15.6.4, 15.6.6); its valid form makes the view from a parameter's borrow (LoanFieldWindow, h2RefPointeeParamOk)
    public void ACallMayStoreThroughItsOwnPointerAtItsResultsRawLayers(string form, string expected)
    {
        var c = MirBuildTest.Compile(CallResultForms[form] + "\npublic func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes))}]");
    }

    // PLAN G841's scaling row (Revision 3's review, C4): N calls, each with a by-value Tuple input of K two-layer raw elements (each
    // addressing a local holder) and a composing result with a raw Field. W reads each input once per own layer of its key (clause (1))
    // and the result once at its key (clause (3)), so the work in N at K = 8 and in K at N = 8 stays within the class's bound.
    [Fact]
    public void AResultAndInputLayersWorkStaysLinearInCallsAndLayers()
    {
        const string Shape = "struct BoxK\n    public var p: raw/(raw/u8)\n    public init(p: raw/(raw/u8))\n        self.p = p\n" +
            "func mkK(slot: («K:raw/(raw/u8), »i32), value: raw/u8) -> BoxK => BoxK.init(p: null)\n" +
            "func shape(v: raw/u8)\n«K:    var c{0}: raw/u8 = null\n»    let t = («K:c{0}@raw, »0)\n«N:    mkK(t, v)\n»";
        long[] inN = [.. new[] { 8, 16, 32 }.Select(n => Measured(Prelude + Expand(Shape, n, 8)).Work)];
        long[] inK = [.. new[] { 8, 16, 32 }.Select(k => Measured(Prelude + Expand(Shape, 8, k)).Work)];
        output.WriteLine($"N 8, 16, 32 at K 8: work {string.Join(", ", inN)}; K 8, 16, 32 at N 8: work {string.Join(", ", inK)}");
        Assert.True(Grows(inN, 5), $"work in N {string.Join(", ", inN)}");
        Assert.True(Grows(inK, 5), $"work in K {string.Join(", ", inK)}");
    }
}
