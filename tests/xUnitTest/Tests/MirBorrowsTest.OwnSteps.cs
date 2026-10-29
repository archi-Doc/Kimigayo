// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's own-step Warning (SPEC 5.2.1 condition 3; DECISIONS R4-M116 OQ-1, OQ-2): an operation that may end the owning value's stay
/// in its holder while a value carrying a pointer formed through that own step may still be used is OwnStepDeparture_Kd.</summary>
public sealed partial class MirBorrowsTest
{
    // The own-step shapes: an object payload's Field, a struct holding an owning handle, a buffer whose destruction releases its storage, a
    // callee taking an owner by uniq with a pointer, and one storing a pointer where its slot refers.
    private const string OwnStepShapes = """
        struct Cell
            public var x: i32 = 0
            public init() => ()
        struct Keeper
            public var h: obj/Cell
            public init(h: obj/Cell) => self.h = h
        struct Store
            internal var data: raw/i32 during self
            public init()
                unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)
            drop
                unsafe => Raw.release(self.data)
        func takeBoth(b: uniq/Buf<i32>, p: raw/i32) => ()
        func keepIn(slot: uniq/(raw/i32), p: raw/i32) => slot@follow = p

        """;

    // SPEC 5.2.1 condition 3 (OQ-1 (a)): a Move of the owning value's Place or of an ancestor with no dereference between (moved, ancestor,
    // objPayload), a Take of a raw Place holding it (taken), a write that replaces it (replaced, through a uniq reference's referent in
    // throughUniq) or its destruction (destroyedAtReturn: a by-value parameter's), and an exclusive Borrow of it (uniqLocal), as a call by
    // uniq forms (byMethod, byExchange), each while a value carrying a pointer formed through the own step may still be used: a live holder
    // (a Raw Place borrow derived from the pointer in rawBorrow), the result, E (stored) or, for a call, its operand (byCall). E keeps the
    // pointer, so the storage end of stored's moved owner warns again (B-N3: a Warning rejects nothing). destroyedAtReturn's warning is at
    // its parameter's Type (DECISIONS BU-48's locations, settled (a): a parameter's record at its Type occurrence, R4-M90 D6's rule extended).
    [Fact]
    public void ADepartureOfTheOwnerWarns()
        => Assert.Equal(
            [
                "moved: b", "ancestor: k", "objPayload: h", "taken: (*q)", "replaced: b = Buf<i32>.init(1)", "throughUniq: r@follow = Buf<i32>.init(1)",
                "destroyedAtReturn: Store", "uniqLocal: b@uniq", "byMethod: b.push(1)",
                "byExchange: Kimi.Intrinsics.exchange(b@uniq, with: Buf<i32>.init(1))", "rawBorrow: b = Buf<i32>.init(1)", "stored: b",
                "stored: var b = Buf<i32>.init(4)", "byCall: takeBoth(b@uniq, b.data)",
            ],
            this.Warnings("""
                func moved()
                    var b = Buf<i32>.init(4)
                    let p = b.data
                    let c = b@move
                    usep(p)
                func ancestor()
                    var k = Keeper.init(h: Kimi.Intrinsics.makeObj<Cell>(Cell.init()))
                    let p = k.h@follow.x@raw
                    let moved = k@move
                    usep(p)
                func objPayload()
                    let h = Kimi.Intrinsics.makeObj<Cell>(Cell.init())
                    let p = h@follow.x@raw
                    let g = h@move
                    usep(p)
                func taken(q: raw/Buf<i32>)
                    unsafe
                        let p = (*q).data
                        let c = (*q)@move
                        usep(p)
                func replaced()
                    var b = Buf<i32>.init(4)
                    let p = b.data
                    b = Buf<i32>.init(1)
                    usep(p)
                func throughUniq()
                    var b = Buf<i32>.init(4)
                    let r = b@uniq
                    let p = r.data
                    r@follow = Buf<i32>.init(1)
                    usep(p)
                func destroyedAtReturn(s: Store) -> raw/i32 => s.data
                func uniqLocal()
                    var b = Buf<i32>.init(4)
                    let p = b.data
                    let r = b@uniq
                    usep(p)
                func byMethod()
                    var b = Buf<i32>.init(4)
                    let p = b.data
                    b.push(1)
                    usep(p)
                func byExchange()
                    var b = Buf<i32>.init(4)
                    let p = b.data
                    _ = Kimi.Intrinsics.exchange(b@uniq, with: Buf<i32>.init(1))
                    usep(p)
                func rawBorrow()
                    var b = Buf<i32>.init(4)
                    b.push(1)
                    unsafe
                        let r = (*(b.data))@ref
                        b = Buf<i32>.init(1)
                        inspect(r)
                func stored(slot: uniq/(raw/i32))
                    var b = Buf<i32>.init(4)
                    keepIn(slot, b.data)
                    let c = b@move
                func byCall()
                    var b = Buf<i32>.init(4)
                    takeBoth(b@uniq, b.data)
                """));

    // SPEC 5.2.1 condition 3: no Warning where the pointer is no longer used (usedBefore), for a read or a shared Borrow of the owner
    // (shared), a write or Move of a Place inside the owner rather than of it or an ancestor (sibling, partial), or a Move of an ancestor
    // across a dereference (a uniq reference moved after a read of the layer through it: its referent stays in its holder).
    [Fact]
    public void AnAccessThatKeepsTheOwnerDoesNotWarn()
        => Assert.Empty(this.Warnings("""
            func usedBefore()
                var b = Buf<i32>.init(4)
                let p = b.data
                usep(p)
                let c = b@move
            func shared()
                var b = Buf<i32>.init(4)
                let p = b.data
                let n = b.length
                inspect(b@ref)
                usep(p)
            func sibling()
                var b = Buf<i32>.init(4)
                let p = b.data
                b.length = 0
                usep(p)
            func partial()
                var b = Buf<i32>.init(4)
                let p = b.data
                let q = b.data@move
                usep(p)
                usep(q)
            func referenceMoved()
                var b = Buf<i32>.init(4)
                let r = b@uniq
                let p = r.data
                let s = r@move
                usep(p)
            """));

    // B′ §5 (as err(ended), R4-M97): a departure whose operation and the pointer's formation lie in one inline default copy is its
    // template's, which reports it at its own declaration, and the omitting call adds nothing; one whose formation lies outside the copy
    // (a pointer into a static's buffer, which the default replaces) is the body's, at the omitting call. A copy reaches a pending slot
    // only through a reference of its own, so a root that a caller and a copy share is a static.
    [Fact]
    public void ADepartureInADefaultCopyIsItsTemplatesOrTheCallsWithItsOperation()
        => Assert.Equal(
            ["withLocal: b", "evaluated: withStatic()"],
            this.Warnings("""
                func withLocal(n: i32, m: i32 = label w: do
                    var b = Buf<i32>.init(1)
                    let p = b.data
                    let c = b@move
                    usep(p)
                    exit to w n
                ) => ()
                func copied() => withLocal(1)
                group G
                    public var buf: Buf<i32> = Buf<i32>.init(1)
                func withStatic(n: i32 = label w: do
                    G.buf = Buf<i32>.init(2)
                    exit to w 1
                ) => ()
                func evaluated()
                    let p = G.buf.data
                    withStatic()
                    usep(p)
                """));

    // DECISIONS R4-M116 OQ-2 (PD31's fact rows): the Anchor is the pointer's first formation (the location reading the layer); the witness
    // is a live holder's next use (none where that lies in a later chain, as past a copy), the store of a value E keeps, or the call for an
    // operand it takes; inside a copy the default's operation is Evaluated at the omitting call.
    [Fact]
    public void TheWarningCarriesItsFacts()
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + OwnStepShapes + """
            func moved()
                var b = Buf<i32>.init(4)
                let p = b.data
                let c = b@move
                usep(p)
            func stored(slot: uniq/(raw/i32))
                var b = Buf<i32>.init(4)
                keepIn(slot, b.data)
                let c = b@move
            func byCall()
                var b = Buf<i32>.init(4)
                takeBoth(b@uniq, b.data)
            group G
                public var buf: Buf<i32> = Buf<i32>.init(1)
            func withStatic(n: i32 = label w: do
                G.buf = Buf<i32>.init(2)
                exit to w 1
            ) => ()
            func evaluated()
                let p = G.buf.data
                withStatic()
                usep(p)
            """);
        var program = Analyze(c);
        var problems = program.Problems.All.ToArray();
        Assert.Equal(
            [
                "b: Anchor b.data, Use usep(p)", "b: Anchor keepIn(slot, b.data), Store b",
                "var b = Buf<i32>.init(4): Anchor keepIn(slot, b.data), Store var b = Buf<i32>.init(4)",
                "takeBoth(b@uniq, b.data): Anchor takeBoth(b@uniq, b.data), Use takeBoth(b@uniq, b.data)",
                "withStatic(): Anchor G.buf.data, Evaluated G.buf = Buf<i32>.init(2)",
            ],
            problems.Select((x, i) => (x, i)).Where(static x => x.x.Code == DiagnosticCode.OwnStepDeparture_Kd && x.x.Subject.Module == 0).Select(x =>
                $"{Line(c, x.x.Subject)}: {string.Join(", ", program.Problems.Facts(x.i).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)}"))}"));
    }

    // V16 (B §7, shape 3): N pointers into N owning Places of one root (a Tuple of N buffers) live to the end and N writes replacing the
    // buffers: each write scans the entries found on its root at its check and meets one, so the work stays within the dense bound
    // (accesses × entries found on the root, as the conflict scan over a root's candidates).
    [Fact]
    public void TheDepartureScanStaysDense()
    {
        const string Shape = "func shape()\n    var t = («N:Buf<i32>.init(1), »0)\n«N:    let p{0} = t.{0}.data\n»«N:    t.{0} = Buf<i32>.init(2)\n»" +
            "«N:    usep(p{0})\n»";
        var runs = new[] { 32, 64, 128 }.Select(n => this.Measured(Prelude + RawShapes + Expand(Shape, n, 0))).ToArray();
        long[] work = [.. runs.Select(static x => x.Work)];
        long[] bytes = [.. runs.Select(static x => x.Bytes)];
        output.WriteLine($"N 32, 64, 128: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, 5), $"bytes {string.Join(", ", bytes)}");
    }

    // D5 (DECISIONS R4-M141): own-step entries make a view a producer only where a use may depart one, so a view no access may depart
    // records nothing, as any view no conflict can depend on: a retention Limit's call where no access departs the owner (ownLimit, as
    // unlent in AnUnclassifiedDestructionOrCallIsUnsupported), while the same call where the local owner's destruction may depart it keeps
    // its limit record (ownLimitLocal), as does a destruction past the walk's bound of a root the entries borrow (ownDeep: d.n's old
    // value; DeepBuf.init's Limit stays too), and a departure warns (ownWriteThrough). The limit records are OwnershipDepthLimit_Kd (the
    // owner's 「G272: (1)」, R4-M153).
    [Fact]
    public void AnOwnStepViewIsAProducerOnlyWhereAnAccessMayDepart()
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + OwnStepShapes + """
            struct DeepBuf
                internal var data: raw/i32 during self
                internal var n: Nest<i32>
                public init()
                    unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)
                    self.n = Nest<i32>.init(value: 1)
            func ownLimit(b: uniq/Buf<i32>, x: ref/i32)
                let p = b.data
                let n = Nest<ref/i32>.init(value: x)
                usep(p)
            func ownLimitLocal(x: ref/i32)
                var b = Buf<i32>.init(4)
                let p = b.data
                let n = Nest<ref/i32>.init(value: x)
                usep(p)
            func ownDeep(d: uniq/DeepBuf)
                let p = d.data
                d.n = Nest<i32>.init(value: 2)
                usep(p)
            func ownWriteThrough(b: uniq/Buf<i32>)
                let p = b.data
                b@follow = Buf<i32>.init(1)
                usep(p)
            """);
        Assert.Equal(
            [
                "init: Nest<i32>.init(value: 1) OwnershipDepthLimit_Kd", "ownLimitLocal: Nest<ref/i32>.init(value: x) OwnershipDepthLimit_Kd",
                "ownDeep: Nest<i32>.init(value: 2) OwnershipDepthLimit_Kd", "ownDeep: d.n = Nest<i32>.init(value: 2) OwnershipDepthLimit_Kd",
                "ownWriteThrough: b@follow = Buf<i32>.init(1) OwnStepDeparture_Kd",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnershipDepthLimit_Kd, DiagnosticCode.OwnStepDeparture_Kd]));
    }

    // D5 (DECISIONS R4-M141): a view that reads a `during self` layer N times and departs no own-step entry runs no stratum: it makes no
    // Loan set at any N, and the pre-pass's work, the departure test's walk included, grows linearly.
    [Fact]
    public void AViewNoAccessMayDepartRunsNoStratum()
    {
        const string Shape = "func shape(b: uniq/Buf<i32>)\n«N:    let p{0} = b.data\n»«N:    usep(p{0})\n»";
        var runs = new[] { 32, 64, 128 }.Select(n => this.Measured(Prelude + RawShapes + Expand(Shape, n, 0))).ToArray();
        long[] work = [.. runs.Select(static x => x.Work)];
        long[] bytes = [.. runs.Select(static x => x.Bytes)];
        output.WriteLine($"N 32, 64, 128: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(bytes[0] == bytes[1] && bytes[1] == bytes[2], $"bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 2), $"work {string.Join(", ", work)}");
    }

    // V16 (DECISIONS R4-M141): N owner roots, each with its own-step entry and two writes of a Place inside its owner, which depart nothing:
    // each write tests the entries on its own root only, so the departure test's work grows linearly in N (a test of every entry per write
    // grows quadratically), and no stratum runs.
    [Fact]
    public void TheMayDepartScanStaysDense()
    {
        const string Shape = "func shape(«N:b{0}: uniq/Buf<i32>, »n: isize)\n«N:    let p{0} = b{0}.data\n»" +
            "«N:    b{0}.length = n\n    b{0}.length = n + 1\n»«N:    usep(p{0})\n»";
        var runs = new[] { 32, 64, 128 }.Select(n => this.Measured(Prelude + RawShapes + Expand(Shape, n, 0))).ToArray();
        long[] work = [.. runs.Select(static x => x.Work)];
        long[] bytes = [.. runs.Select(static x => x.Bytes)];
        output.WriteLine($"N 32, 64, 128: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(bytes[0] == bytes[1] && bytes[1] == bytes[2], $"bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 2), $"work {string.Join(", ", work)}");
    }

    // The own-step Warnings of a program's own bodies, with the raw and own-step shapes.
    private string[] Warnings(string source)
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + OwnStepShapes + source);
        var found = Problems(c, Analyze(c), [DiagnosticCode.OwnStepDeparture_Kd]);
        output.WriteLine(string.Join("\n", found));
        return [.. found.Select(static x => x.Replace(" OwnStepDeparture_Kd", string.Empty, StringComparison.Ordinal))];
    }
}
