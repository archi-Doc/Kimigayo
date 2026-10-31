// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The shared acceptance tests of PLAN G264 U1 (WORKFLOW Q11; written from the problem statement before either candidate):
/// a destruction retains as a call of each <c>drop</c> it runs, whose <c>self</c> is a <c>uniq/Self</c> receiver referring to the
/// destroyed part, and a published operation's cleanup is the destruction its row lists, composed at the call (SPEC 15.6.4, 16.3.1,
/// 16.3.3, 16.4, 4.9.6). Storage the body does not own follows the whole holder (SPEC 15.6, the owner's 「(B)」 of 2026-10-30, DECISIONS
/// BU-67): a value an untied constructor made reaches it through every raw Field, so a Writer's drop that stores its q through its p
/// stores y's Loan there, where it may be used after every point. Each row names the destruction site of the G264 design's §5.2 it
/// covers. The Bad rows of the reassignment, scope, discard and part sites are accepted on dev (A5 composes no drop at a Drop) and fail
/// by design until U1; every other row holds on dev and must hold after.</summary>
public sealed partial class MirBorrowsTest
{
    // The forms' Types: Writer stores its q through its raw slot p when destroyed (SPEC 15.6.4's example), Quiet holds the same Fields
    // without a drop; Outer holds a Writer by value and OuterD's own drop rewrites its inner Writer's slot before the Field's drop runs
    // (SPEC 16.3.2's order); BaseW's drop runs for a Derived's base part; Holder<T> is a generic Type with the drop; Setter's drop writes its
    // borrowed value through its borrowed target (no raw layer, so nothing reaches storage the body does not own); WKey is a Dictionary
    // key with Writer's drop.
    private const string DropShapes = """
        func inspect<T>(value: ref/T) => ()
        func usep<T>(p: raw/T) => ()
        func consume<T>(x: T) => ()
        struct Writer
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        struct Quiet
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
        struct Outer
            public var inner: Writer
            public var n: i32
            public init(q: raw/u8)
                self.inner = Writer.init(q: q)
                self.n = 0
        struct OuterD
            public var inner: Writer
            public var r: raw/(raw/u8)
            public init(q: raw/u8)
                self.inner = Writer.init(q: q)
                self.r = null
            drop
                self.inner.p = self.r
        open struct BaseW
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        struct Derived : BaseW
            public init(q: raw/u8) : base(q: q) => ()
        struct Holder<T>
            public var item: T
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(item: T, q: raw/u8)
                self.item = item@move
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        struct Setter {a}
            public var target: uniq/(ref/i32 during a) during a
            public var value: ref/i32 during a
            public init(target: uniq/(ref/i32 during a) during a, value: ref/i32 during a)
                self.target = target
                self.value = value
            drop
                self.target@follow = self.value
        struct WKey
            Self is Equatable
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public var n: i32
            public init(n: i32, q: raw/u8)
                self.p = null
                self.q = q
                self.n = n
            public func equals(self: ref/Self, other: ref/Self) -> bool => self.n == other.n
            drop
                unsafe => *self.p = self.q

        """;

    // The design's forms (R4 notes g264\probes: d2src d1-sites, d2-parts, d3-arrays, d4-generic, d5-borrow, d10-escape; d2bu d93-drop2;
    // r5 lit; new k1-key), with Ok twins valid under SPEC 15.6's whole-holder text where one exists, and the published destroying
    // calls' null twins. Bad writes y before the value a drop stored y's Loan into is used; Ok stores nothing that carries y, or has no
    // later access.
    private const string DropForms = """
        func localBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = Writer.init(q: null)
            y = 5
            usep(slot)
        func localOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = Writer.init(q: null)
            usep(slot)
            y = 5
        func quietOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Quiet.init(q: w@raw@raw/u8)
            x.p = slot@raw
            x = Quiet.init(q: null)
            y = 5
            usep(slot)
        func nullQOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            x = Writer.init(q: null)
            y = 5
            usep(slot)
        func scopeBad(b: bool)
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            if b
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
            y = 5
            usep(slot)
        func endOk()
            var slot: raw/u8 = null
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
        func endParamOk(y: uniq/i32)
            let w = y@follow@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
        func discardBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            _ = x@move
            y = 5
            usep(slot)
        func setterScopeBad(b: bool)
            var z: i32 = 0
            var t: ref/i32 = z@ref
            var y: i32 = 1
            if b
                var s = Setter.init(target: t@uniq, value: z@ref)
                s.value = y@ref
            y = 5
            inspect(t@ref)
        func setterScopeOk(b: bool)
            var z: i32 = 0
            var t: ref/i32 = z@ref
            var y: i32 = 1
            if b
                var s = Setter.init(target: t@uniq, value: z@ref)
                s.value = y@ref
            inspect(t@ref)
            y = 5
        func setterDiscardBad()
            var z: i32 = 0
            var t: ref/i32 = z@ref
            var y: i32 = 1
            var s = Setter.init(target: t@uniq, value: z@ref)
            s.value = y@ref
            _ = s@move
            y = 5
            inspect(t@ref)
        func setterDiscardOk()
            var z: i32 = 0
            var t: ref/i32 = z@ref
            var y: i32 = 1
            var s = Setter.init(target: t@uniq, value: z@ref)
            s.value = y@ref
            _ = s@move
            inspect(t@ref)
            y = 5
        func escapeBad(p: raw/(raw/u8))
            var y: i32 = 1
            let w = y@ref
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = p
        func escapeOk(p: raw/(raw/u8))
            var x = Writer.init(q: null)
            x.p = p
        func escapeLaterBad(p: raw/(raw/u8))
            var y: i32 = 1
            let w = y@ref
            var slot: raw/(raw/u8) = p
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot
            x = Writer.init(q: null)
            usep(slot)
        func fieldBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o = Outer.init(q: w@raw@raw/u8)
            o.inner.p = slot@raw
            o = Outer.init(q: null)
            y = 5
            usep(slot)
        func orderBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var o = OuterD.init(q: w@raw@raw/u8)
            o.r = slot@raw
            o = OuterD.init(q: null)
            y = 5
            usep(slot)
        func baseBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var d = Derived.init(q: w@raw@raw/u8)
            d.p = slot@raw
            d = Derived.init(q: null)
            y = 5
            usep(slot)
        func baseOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var d = Derived.init(q: null)
            d.p = slot@raw
            d = Derived.init(q: null)
            y = 5
            usep(slot)
        func tupleBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var t = (x@move, 1)
            t = (Writer.init(q: null), 2)
            y = 5
            usep(slot)
        func optionBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var o: Option<Writer> = .Some(x@move)
            o = .None
            y = 5
            usep(slot)
        func holderBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var h = Holder<i32>.init(item: 1, q: w@raw@raw/u8)
            h.p = slot@raw
            h = Holder<i32>.init(item: 2, q: null)
            y = 5
            usep(slot)
        func closureBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            do
                let f = func [x@move] () => ()
            y = 5
            usep(slot)
        func arrReassignBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a = []
            y = 5
            usep(slot)
        func arrScopeBad(b: bool)
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            if b
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var a: Array<Writer> = []
                a.append(x@move)
            y = 5
            usep(slot)
        func litScopeBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            do
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                let a: Array<Writer> = [x@move]
            y = 5
            usep(slot)
        func arrClearBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.clear()
            y = 5
            usep(slot)
        func replaceBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            Kimi.Intrinsics.replace(x@uniq, with: Writer.init(q: null))
            y = 5
            usep(slot)
        func keyParamBad(d: uniq/Dictionary<WKey, i32>)
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var k = WKey.init(n: 1, q: w@raw@raw/u8)
            k.p = slot@raw
            _ = d.insertOrReplace(k@move, 2)
            y = 5
            usep(slot)
        func clearNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.clear()
            y = 5
            usep(slot)
        func truncateNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.truncate(0)
            y = 5
            usep(slot)
        func removeAllNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.removeAll(matching: func (v) => true)
            y = 5
            usep(slot)
        func replaceNullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: null)
            x.p = slot@raw
            Kimi.Intrinsics.replace(x@uniq, with: Writer.init(q: null))
            y = 5
            usep(slot)
        func keyParamNullOk(d: uniq/Dictionary<WKey, i32>)
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var k = WKey.init(n: 1, q: null)
            k.p = slot@raw
            _ = d.insertOrReplace(k@move, 2)
            y = 5
            usep(slot)

        """;

    // SPEC 15.6.4, 16.3.1, 16.3.2, 16.4 and 15.6 (the owner's 「(B)」): each destruction site of the design's §5.2 composes the drop of
    // every destroyed part as a call over a `uniq/Self` receiver. A Writer from `Writer.init` reaches storage the body does not own through
    // every raw Field, so its drop stores q (which addresses w, which carries y's Loan) into slot and there, where it may be used after
    // every point: a later write of y errs at the write, and with no write at y's storage end (its declaration, as A5 locates a storage
    // end). The rows: Drop at `=` (localBad, which stands for localDrop2Bad, reset and k2's resetSpecBad, the same source; localOk, the
    // twin using slot first, is a correct error by the whole-holder text, as Raw's viaOk; quietOk without a drop and nullQOk whose drop
    // stores null are valid); Drop at a scope end (scopeBad; endOk, y's storage end after x's drop, a correct error by the same text;
    // endParamOk, SPEC 15.6.4's valid shape: a parameter referent's Loan in that storage with no later access; escapeBad and
    // escapeLaterBad store through a parameter's raw layer; escapeOk stores null); Drop of `_ = x@move` (discardBad); a drop writing
    // through a borrow Field (Setter, no raw layer): retention is per argument (SPEC 15.6.4), so the drop's receiver Content, its own
    // `t@uniq` Loan included, is stored into t, and `t@ref` errs after it in Bad and Ok alike (dev's setterConsumeBad through Binding's
    // consuming-callee rows, BU-52); parts: a Field (fieldBad), an outer drop rewriting its Field's slot before the Field's drop (orderBad,
    // SPEC 16.3.2), a base (baseBad respelled `: base(q: q)`; baseOk stores null), Tuple and Option payloads, a generic Type's drop
    // (holderBad), a Closure's capture (closureBad respelled: the Closure ends with its `do` block), Array elements at a reassignment
    // (arrReassignBad, also scopeDrop2Bad) and at a scope end (arrScopeBad, litScopeBad from an Array literal).
    [Theory]
    [InlineData("localBad", "localBad: y = 5")] // Drop at `=`
    [InlineData("localOk", "localOk: y = 5")] // Drop at `=`
    [InlineData("quietOk", "")] // Drop at `=`
    [InlineData("nullQOk", "")] // Drop at `=`
    [InlineData("scopeBad", "scopeBad: y = 5")] // scope end
    [InlineData("endOk", "endOk: var y: i32 = 1")] // scope end
    [InlineData("endParamOk", "")] // scope end
    [InlineData("escapeBad", "escapeBad: var y: i32 = 1")] // scope end
    [InlineData("escapeOk", "")] // scope end
    [InlineData("escapeLaterBad", "escapeLaterBad: var y: i32 = 1")] // Drop at `=`
    [InlineData("discardBad", "discardBad: y = 5")] // `_ = x@move`
    [InlineData("setterScopeBad", "setterScopeBad: y = 5; setterScopeBad: t@ref")] // scope end
    [InlineData("setterScopeOk", "setterScopeOk: t@ref")] // scope end
    [InlineData("setterDiscardBad", "setterDiscardBad: y = 5; setterDiscardBad: t@ref")] // `_ = x@move`
    [InlineData("setterDiscardOk", "setterDiscardOk: t@ref")] // `_ = x@move`
    [InlineData("fieldBad", "fieldBad: y = 5")] // Field part
    [InlineData("orderBad", "orderBad: y = 5")] // Field part after the outer drop
    [InlineData("baseBad", "baseBad: y = 5")] // base part
    [InlineData("baseOk", "")] // base part
    [InlineData("tupleBad", "tupleBad: y = 5")] // Tuple element
    [InlineData("optionBad", "optionBad: y = 5")] // Case payload
    [InlineData("holderBad", "holderBad: y = 5")] // generic Type's drop
    [InlineData("closureBad", "closureBad: y = 5")] // Closure capture
    [InlineData("arrReassignBad", "arrReassignBad: y = 5")] // Array elements at `=`
    [InlineData("arrScopeBad", "arrScopeBad: y = 5")] // Array elements at a scope end
    [InlineData("litScopeBad", "litScopeBad: y = 5")] // Array literal's elements at a scope end
    public void ADestructionRetainsAsItsDropsCall(string form, string expected)
    {
        var c = MirBuildTest.Compile(DropShapes + Form(DropForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]))}]");
    }

    // SPEC 4.9.6, 15.6.4, 16.3.3: a published destroying call composes the destruction its row lists at the call, after its own rows,
    // over the pre-call state: `clear` of an Array, `replace`'s old target, `insertOrReplace`'s unused key (keyParamBad: a parameter's
    // Dictionary, so 4.9.6's key dependencies also reach storage the body does not own); `truncate` and `removeAll` have their Bad rows in
    // ATruncateOrRemoveAllWhoseReferentMayObserveKeepsW. Each Bad errs at `y = 5` today through the referent guard (DECISIONS R4-M143) or
    // the key's dependencies, and must after through the composition; each null twin (the destroyed element's q is null, so its drop
    // stores nothing that carries y) is valid, so no guard or composition may add a Loan the drop does not store.
    [Theory]
    [InlineData("arrClearBad", "arrClearBad: y = 5")]
    [InlineData("clearNullOk", "")]
    [InlineData("truncateNullOk", "")]
    [InlineData("removeAllNullOk", "")]
    [InlineData("replaceBad", "replaceBad: y = 5")]
    [InlineData("replaceNullOk", "")]
    [InlineData("keyParamBad", "keyParamBad: y = 5")]
    [InlineData("keyParamNullOk", "")]
    public void APublishedDestroyingCallComposesItsRowsDestruction(string form, string expected)
    {
        var c = MirBuildTest.Compile(DropShapes + Form(DropForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]))}]");
    }

    // SPEC 15.6.4's example (tests/diagnostics/spec-cases.json 15.6.4-drop-retention): as written it is valid, and with `y@follow = 5`
    // A5 reports the conflict there, the record the SPEC gives (the example's own expectation, not its recorded MIR difference, which U1
    // removes).
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheDropRetentionExampleGetsItsLoanCodes(int variant)
        => this.TheSpecExamplesGetTheirLoanCodes($"15.6.4-drop-retention/{variant}");

    // V16 (the design's §5.7): N locals whose drops store a parameter referent's Loan through their raw slot, each destroyed by a
    // reassignment (N = 8, 16, 32; SPEC 15.6.4's valid shape). Each destruction composes its drop's rows once, so the work grows at most
    // 3 times as much from 16 to 32 as from 8 to 16 (dev, which composes none: 102,043, 106,611, 116,179, a factor 2.09; quadratic is 4).
    [Fact]
    public void DestructionsWorkStaysLinearInTheirNumber()
    {
        const string Shape = "func shape(y: uniq/i32)\n    var slot: raw/u8 = null\n" +
            "«N:    let w{0} = y@follow@ref\n    var x{0} = Writer.init(q: w{0}@raw@raw/u8)\n    x{0}.p = slot@raw\n    x{0} = Writer.init(q: null)\n»" +
            "    usep(slot)\npublic func main() => ()\n";
        long[] work = [.. new[] { 8, 16, 32 }.Select(n => this.Measured(DropShapes + Expand(Shape, n, 0)).Work)];
        output.WriteLine($"N 8, 16, 32: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 3), $"work {string.Join(", ", work)}");
    }

    // V16: A5 on a program version it checked before allocates nothing at destructions: every site's composition, the published
    // destroying calls and a drop writing through a borrow Field.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmDestructionAllocatesNothing()
    {
        string[] forms = ["localBad", "scopeBad", "discardBad", "escapeLaterBad", "setterScopeBad", "fieldBad", "orderBad", "baseBad", "tupleBad",
            "optionBad", "holderBad", "closureBad", "arrReassignBad", "litScopeBad", "arrClearBad", "replaceBad", "keyParamBad"];
        var c = MirBuildTest.Compile(DropShapes + string.Concat(forms.Select(static x => Form(DropForms, x))) + "public func main() => ()\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.NotEmpty(Problems(c, program, LoanCodes));
    }
}
