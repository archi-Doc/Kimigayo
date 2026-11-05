// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>A5-F9's default rules (SPEC 7.2.3, 15.6.4, 15.6.7; docs/dev/DECISIONS.md R4-M90, R4-M93, R4-M96, R4-M98): a recursive default's
/// template call carries the Loans of every preceding argument into its value (P9), and one whose slot holds raw authority beside a Loan is a
/// located Unsupported_Kd (r3); a template's preceding parameters are the caller's slots, which it may only inspect (R26-borrow, R26-access);
/// an omitting call ends its inspection Loans before its checks (R-a).</summary>
public sealed partial class MirBorrowsTest
{
    // Recursive defaults: a generic one, a Function-typed one, one with a reserved input and a Loan-capable value (rr, round 19's
    // dc-reserved-rec), and three that write an input through a raw pointer read from a slot: a raw-only slot (putr), the same beside an
    // input it never writes (putz, z), and a raw pointer inside a holder that holds a Loan (putb, PLAN G160 (a)).
    private const string Recursive = """
        func f<T>(n: i32, x: T, value: T = if n == 0 => x else => f(n - 1, x)) -> T
            T is Copy
            return value
        func g(n: i32, value: () -> i32 = if n == 0 => func () => 7 else => g(n - 1)) -> () -> i32 => value@move
        func rr(x: uniq/i32 during a, z: ref/i32 during a, n: i32, y: ref/i32 during a = if n == 0 => z else => (label w: do
            var local = 0
            var other = 0
            _ = rr(local@uniq, other@ref, n - 1)
            exit to w z
        )) -> ref/i32 during a => y
        func putr(p: raw/(ref/i32 during a), x: ref/i32 during a, n: i32, y: i32 = label w: do
            if n == 0
                unsafe => p[0] = x
                exit to w 0
            exit to w putr(p, x, n - 1)
        ) -> i32 => y
        func putz(p: raw/(ref/i32 during a), x: ref/i32 during a, z: ref/i32 during c, n: i32, y: i32 = label w: do
            if n == 0
                unsafe => p[0] = x
                exit to w z@follow
            exit to w putz(p, x, z, n - 1)
        ) -> i32 => y
        struct RawBox {a}
            public var p: raw/(ref/i32 during a)
            public var r: ref/i32 during a
            public init(p: raw/(ref/i32 during a), r: ref/i32 during a)
                self.p = p
                self.r = r
        func putb(b: ref/(RawBox during a), x: ref/i32 during a, n: i32, y: i32 = label w: do
            if n == 0
                unsafe => b.p[0] = x
                exit to w 0
            exit to w putb(b, x, n - 1)
        ) -> i32 => y

        """;

    // Inline defaults that inspect a preceding slot (SPEC 7.2.3): copies of an existing shared borrow (same, onlyY, pickR, the generic pick
    // and via), and an exclusive input's Reborrow kept in the result (fc, g: the template's R26-borrow, R4-M98) with its generic forms in the
    // uniq case (grab, f2).
    private const string Inspecting = """
        func fc(x: uniq/i32 during a, y: ref/i32 during a = x) => ()
        func same(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y
        func first(x: ref/i32 during a, y: i32 = 0) -> ref/i32 during a => x
        func onlyY(x: ref/i32 during q, y: ref/i32 during p = x) -> ref/i32 during p
            origin q outlives p
            return y
        func pickR(a: ref/i32 during p, b: ref/i32 during p = a) -> ref/i32 during p => b
        func pick<s/T>(a: s/T, b: s/T = a) -> s/T
            s is owner or ref
            T is Copy
            return b
        func grab<s/T>(a: s/T, b: s/T = a) -> i32
            s is ref or uniq
            return 0
        func id<s/T>(x: s/T) -> s/T
            s is owner or ref
            T is Copy
            return x
        func via<s/T>(a: s/T, b: s/T = id(a)) -> s/T
            s is owner or ref
            T is Copy
            return b
        func f2<s/T>(x: s/T, y: s/T = x) -> ()
            s is ref or uniq
            return
        struct Counter
            public var n: i32 = 3
            public func g(self: uniq/Self, d: ref/Counter during self = self) => ()

        """;

    // ReceiverDefaultTest's ReservedMethod: a default calling a method on a reserved input.
    private const string Inspected = """
        struct Meter
            public var raw: i32 = 4
            public func read(self) -> i32 => self.raw
        func fm(m: uniq/Meter, value: i32 = m.read()) -> i32
            m.raw = value + 1
            return value

        """;

    // P9 (SPEC 7.2.3, R4-M93): a template call's value is its slots' values, each input's Content, untied, so it keeps the arguments' Loans
    // and never the prologue's inspection Loans, which the call's Moves of the slots would meet: RecursiveDefaultTest's generic, Function
    // and reference programs and the 1c design's p06 are valid, the reference kept while `n = 4` errs (live), and a reserved input's
    // reservation in the value is not met by its own activation (OQ-F9-1 (a), R4-M68).
    [Fact]
    public void ATemplateCallsValueIsItsSlotsValues()
        => Assert.Equal(
            ["live: n = 4"],
            AllProblems(Recursive + """
                func generic()
                    require f(4, 7) == 7 and f(5, true) else => $abort("generic defaults")
                func closure()
                    let action = g(3)
                    require action() == 7 else => $abort("closure default")
                func reference()
                    var n = 3
                    let r = f(3, n@ref)
                    require r@follow == 3 else => $abort("reference")
                    n = 4
                    require n == 4 else => $abort("last use")
                func live()
                    var n = 3
                    let r = f(3, n@ref)
                    n = 4
                    _ = r@follow
                func p06()
                    var k = 3
                    let r = f(3, k@ref)
                    k = 4
                func reserved()
                    var v = 1
                    var w = 2
                    _ = rr(v@uniq, w@ref, 3)
                """));

    // SPEC 7.2.3, 5.2.1 Retained dependencies (DECISIONS R4-M116 PQ-5; R4-M93's r3 removed): a default may write through a raw pointer it
    // reads from a slot, and those stores are part of the omitting call, which can make the call invalid only through the effects it
    // contributes: its own rows apply them at the call to the holders the slot's value reaches, while the template call stores nothing.
    // A raw-only slot errs at `v = 5` by the omitting call's own row (rawRec); one beside an input the default never stores is valid
    // (unrelated), its local twin's `var v = 1` SPEC's error (v's end while the call's row keeps v's Loan in raw storage, which may be used
    // after every point, as `rawStoredLocal`'s); a raw pointer in a holder read past a shared layer writes nothing (hid, valid by
    // condition 3); inputs whose values hold no Loan give none (rawParam). Between a template call and its call only other defaults run,
    // and what they read back through the pointer the call retains per argument: a later default reading back the stored Loan into the
    // result keeps it (window1) or into a holder the pointer addresses (window2); with no later use the call is valid (window3).
    [Fact]
    public void ADefaultsRawStoresAreItsCallsOwn()
        => Assert.Equal(
            ["window1: v = 5", "window2: v = 5", "rawRec: v = 5", "unrelatedLocal: var v = 1"],
            AllProblems(Recursive + """
                func store(p: raw/(ref/i32 during a), x: ref/i32 during a) -> i32
                    unsafe => Raw.initialize(p, x)
                    return 0
                func readBack(p: raw/(ref/i32 during a)) -> ref/i32 during a
                    unsafe => return *p
                func putrb(p: raw/(ref/i32 during a), x: ref/i32 during a, y: i32 = store(p, x), z: ref/i32 during a = readBack(p)) -> ref/i32 during a
                    return z
                func window1()
                    var u: i32 = 0
                    var v: i32 = 1
                    var w: ref/i32 = u@ref
                    let r = putrb(w@raw, v@ref)
                    v = 5
                    inspect(r)
                func window2()
                    var u: i32 = 0
                    var v: i32 = 1
                    var w: ref/i32 = u@ref
                    _ = putrb(w@raw, v@ref)
                    v = 5
                    inspect(w)
                func window3()
                    var u: i32 = 0
                    var v: i32 = 1
                    var w: ref/i32 = u@ref
                    _ = putrb(w@raw, v@ref)
                    v = 5
                func rawRec()
                    var v = 1
                    let rp: raw/(ref/i32) = Raw.allocate(1)
                    _ = putr(rp, v@ref, 3)
                    v = 5
                    unsafe => inspect(rp[0])
                func rawParam(v: ref/i32 during q)
                    let rp: raw/(ref/i32 during q) = Raw.allocate(1)
                    _ = putr(rp, v, 3)
                func unrelated(v: ref/i32 during q)
                    var w = 2
                    let rp: raw/(ref/i32 during q) = Raw.allocate(1)
                    _ = putz(rp, v, w@ref, 3)
                    w = 5
                func unrelatedLocal()
                    var v = 1
                    var w = 2
                    let rp: raw/(ref/i32) = Raw.allocate(1)
                    _ = putz(rp, v@ref, w@ref, 3)
                    w = 5
                func hid(z: ref/i32 during q)
                    var v = 1
                    var box = RawBox.init(p: Raw.allocate(1), r: z)
                    let q = box.p
                    _ = putb(box@ref, v@ref, 3)
                    v = 5
                    unsafe => inspect(q[0])
                func hidValid(z: ref/i32 during q)
                    var v = 1
                    var box = RawBox.init(p: Raw.allocate(1), r: z)
                    _ = putb(box@ref, v@ref, 3)
                """));

    // V16 "F9 template calls" (A3 6.2; VERIFICATION has no V16 table): M omitting calls of a recursive default with N preceding parameters
    // in one body, each argument carrying one Loan. A template call reads N Content rows (its scratch N, N × (N + 1) before); the shape's
    // work is dense, as TheWorkScalesWithTheProgram's dense shapes are: a call's N prologue borrows each scan the temps live across them
    // (shape 8's accesses × distinct live nodes, also in the template's own call) and its endings the body's holders (E8's ended points ×
    // holders), so the work in N at M = 8 and in M at N = 8 stays within the dense bound (2e-1 measured 169,071, 355,746, 1,047,000 in N
    // and 169,071, 382,110, 1,205,759 in M; 176,777, 376,636, 1,112,306 and 176,777, 396,656, 1,233,985 before).
    [Fact]
    public void ATemplateCallsWorkStaysDense()
    {
        const string Head = "func fN<T>(«N:a{0}: T, »n: i32, value: T = if n == 0 => a0 else => fN(«N:a{0}, »n - 1)) -> T\n    T is Copy\n" +
            "    return value\nfunc shape()\n    var x = 1\n    let r = x@ref\n";
        long[] inN = [.. new[] { 8, 16, 32 }.Select(n => this.Measured(Prelude + Expand(Head, n, 0) + Calls(n, 8)).Work)];
        long[] inM = [.. new[] { 8, 16, 32 }.Select(m => this.Measured(Prelude + Expand(Head, 8, 0) + Calls(8, m)).Work)];
        output.WriteLine($"N 8, 16, 32 at M 8: work {string.Join(", ", inN)}; M 8, 16, 32 at N 8: work {string.Join(", ", inM)}");
        Assert.True(Grows(inN, 5), $"work in N {string.Join(", ", inN)}");
        Assert.True(Grows(inM, 5), $"work in M {string.Join(", ", inM)}");

        static string Calls(int n, int m) => string.Concat(Enumerable.Repeat(Expand("    _ = fN(«N:r, »3)\n", n, 0), m));
    }

    // V16: template calls, a raw slot's addresses included, allocate nothing on a program version A5 checked before.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ATemplateCallsWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + Recursive + "func live()\n    var n = 3\n    let r = f(3, n@ref)\n    n = 4\n    _ = r@follow\n" +
            "func hid(z: ref/i32 during q)\n    var v = 1\n    var box = RawBox.init(p: Raw.allocate(1), r: z)\n    _ = putb(box@ref, v@ref, 3)\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(["live: n = 4"], Problems(c, program, [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // R26-borrow (SPEC 7.2.3, PD15 (iv); R4-M98 D3): a default's result keeps no new Borrow or Reborrow of a preceding argument, nor does
    // storage the body does not own; the slot step (A3 E17) makes such a Borrow a Loan of the template, found at its Return in the closure
    // of σ(_0) and σ(E): DefaultAccessTest's :27 (fc) and :29–:32 (a local, a Tuple, `shared(x)`'s and `choose`'s results), a Reborrow
    // stored through a raw pointer (round 19's dt-raw-borrow, whose `p[0] = x@ref` of an i32 slot Binding rejects, UnsatisfiedOriginRelation_Kd:
    // the Binding-valid Reborrow of a reserved input instead) and a nested copy's result (dt-nested-bad: inner's value keeps x's Reborrow).
    [Fact]
    public void ATemplateKeepsNoNewBorrowOfASlot()
        => Assert.Equal(
            [
                "fc: x DefaultArgumentBorrow_Kd", "flet: x DefaultArgumentBorrow_Kd", "ftuple: x DefaultArgumentBorrow_Kd",
                "fshared: x DefaultArgumentBorrow_Kd", "fchoose: x DefaultArgumentBorrow_Kd", "keepRaw: z DefaultArgumentBorrow_Kd",
                "outer: x DefaultArgumentBorrow_Kd",
            ],
            AllProblems("""
                func shared(x: ref/i32 during a) -> ref/i32 during a => x
                func choose(c: bool, x: ref/i32 during a, z: ref/i32 during a) -> ref/i32 during a => if c => x else => z
                func fc(x: uniq/i32 during a, y: ref/i32 during a = x) => ()
                func flet(x: uniq/i32 during a, y: ref/i32 during a = label work: do
                    let shared: ref/i32 during a = x
                    exit to work shared
                ) => ()
                func ftuple(x: uniq/i32 during a, y: (ref/i32 during a, i32) = (x, 1)) => ()
                func fshared(x: uniq/i32 during a, y: ref/i32 during a = shared(x)) => ()
                func fchoose(x: uniq/i32 during a, z: ref/i32 during a, y: ref/i32 during a = choose(true, x, z)) => ()
                func keepRaw(z: uniq/i32 during a, p: raw/(ref/i32 during a), y: i32 = label w: do
                    unsafe => p[0] = z
                    exit to w 0
                ) => ()
                func inner(a: ref/i32 during q, b: ref/i32 during q = a) -> ref/i32 during q => b
                func outer(x: uniq/i32 during q, y: ref/i32 during q = inner(x)) => ()
                """));

    // R26-access (SPEC 7.2.3; R4-M98 D4): a default cannot modify a preceding slot or borrow it exclusively: DefaultAccessTest's :25 (fa,
    // the reservation of increment's input) and :26 (fb, a write through the reserved input, in a view with no Loan, which the stratum still
    // checks, D5), a bare capture entry Reborrowing an exclusive input (SPEC 7.7.2; round 19's dt-cap-uniq), and a write that stores a
    // Reborrow of another slot into storage the body does not own,
    // both failures kept (dt-twice, whose `x@follow = z@ref` of an i32 slot Binding rejects, UnsatisfiedOriginRelation_Kd: the Reborrow of a
    // reserved input instead). A Loan R26-borrow reports is not an Access too (ExclusiveCase below).
    [Fact]
    public void ATemplateNeitherWritesNorExclusivelyBorrowsASlot()
        => Assert.Equal(
            [
                "fa: x DefaultArgumentAccess_Kd", "fb: x@follow = 9 DefaultArgumentAccess_Kd", ": func [x]() => x@follow DefaultArgumentAccess_Kd",
                "twice: x@follow = z DefaultArgumentAccess_Kd", "twice: z DefaultArgumentBorrow_Kd",
            ],
            AllProblems("""
                func increment(x: uniq/i32) -> i32
                    x@follow += 1
                    return x@follow
                func fa(x: uniq/i32, y: i32 = increment(x)) => ()
                func fb(x: uniq/i32, y: i32 = label work: do
                    x@follow = 9
                    exit to work 1
                ) => ()
                func capUniq(x: uniq/i32, y: i32 = label w: do
                    let c = func [x] () => x@follow
                    exit to w c()
                ) => ()
                func twice(z: uniq/i32 during a, x: uniq/(ref/i32 during a) during b, y: i32 = label w: do
                    x@follow = z
                    exit to w 0
                ) => ()
                """));

    // SPEC 7.2.3: a default may read a preceding slot, Copy a stored shared borrow and inspect a reserved input through a shared Borrow
    // that ends inside it: DefaultAccessTest's :16–:20 and the plan base's :44 (equalOrigins); a Borrow past a shared Deref forms no Loan
    // (:28, fd: valid by SPEC 15.6.3, PD15 (i); the old path's Borrow is OldRule); SPEC 7.2.3's label and erased; a shared capture entry
    // (dt-cap-ref); a write through a raw pointer read from a slot (dt-raw-copy) and a Borrow of raw storage it reaches (dt-raw-ref), which
    // have their own anchor (SPEC 15.6.2, 5.2.1); a nested copy of an inspecting default (dt-nested, ReceiverDefaultTest's NestedOrigins).
    [Fact]
    public void ATemplateMayInspectItsSlots()
        => Assert.Empty(AllProblems("""
            func increment(x: uniq/i32) -> i32
                x@follow += 1
                return x@follow
            struct Counter
                public var n: i32 = 3
                public func increment(self: uniq/Self) -> i32
                    self.n += 1
                    return self.n
            struct Meter
                public var raw: i32 = 4
                public func read(self) -> i32 => self.raw
            func inc(value: i32) -> i32 => value + 1
            func read(x: uniq/i32, y: i32 = x) -> i32
                x@follow = y + 1
                return y
            func copyRef(x: uniq/(ref/i32 during a) during b, y: ref/i32 during a = x) -> i32 => y@follow
            func share(x: uniq/i32 during a, y: i32 = label read: do
                let r: ref/i32 during a = x
                exit to read r@follow
            ) -> i32
                x@follow = y + 1
                return y
            func independent(value: i32 = label work: do
                var local = 3
                exit to work increment(local@uniq)
            ) -> i32 => value
            func receiver(value: i32 = label work: do
                var local = Counter.init()
                exit to work local.increment()
            ) -> i32 => value
            func equalOrigins(x: uniq/(ref/i32 during a) during a, y: ref/i32 during a = x) -> i32 => y@follow
            func fd(x: ref/i32 during a, y: ref/i32 during a = x@follow@ref) => ()
            func label(x: string, y: string = Text.toString(x)) => ()
            func erased(action: (i32) -> i32 = inc) => ()
            func capRef(x: i32, y: i32 = label w: do
                let c = func [x@ref] () => x@follow
                exit to w c()
            ) => ()
            func putRaw(p: raw/(ref/i32 during a), x: ref/i32 during a, y: i32 = label w: do
                unsafe => p[0] = x
                exit to w 0
            ) => ()
            func rawRef(p: raw/i32, y: ref/i32 = label w: do
                unsafe => exit to w p[0]@ref
            ) => ()
            func leaf(m: ref/Meter, value: i32 = m.read()) -> i32 => value
            func nested(a: ref/Meter, b: ref/Meter, value: i32 = leaf(a) + leaf(b)) -> i32 => value
            """));

    // SPEC 7.2.3, 8.15: a generic default is checked per Semantics case: `y: s/T = x` copies the slot for s = owner and s = ref (audit-calls
    // row 26) and keeps its exclusive Reborrow for s = uniq, one failure with one code (SemanticsDefaultTest's ExclusiveCase, round 19's
    // dt-sem-u), as DefaultAccessTest's grab in its uniq case.
    [Fact]
    public void AGenericTemplateFailsOnlyInItsExclusiveCase()
    {
        var c = MirBuildTest.Compile(Prelude + """
            func excl<s/T>(x: s/T, y: s/T = x) -> ()
                s is owner or ref or uniq
                T is Copy
                return
            func grab<s/T>(a: s/T, b: s/T = a) -> i32
                s is ref or uniq
                return 0
            """);
        var program = Analyze(c);
        Assert.Equal(
            ["x DefaultArgumentBorrow_Kd views 4", "a DefaultArgumentBorrow_Kd views 2"],
            program.Problems.All.ToArray().Where(x => DefaultCodes.Contains(x.Code) && x.Subject.Module == 0)
                .Select(x => $"{Line(c, x.Subject)} {x.Code} views {x.Views}"));
    }

    // PD31 (R4-M90 D6): an R26 record's one fact is the parameter, its Anchor at the parameter's Type node, where the template's
    // PreparedInput is located; no witness.
    [Fact]
    public void ATemplateSlotProblemNamesItsParameter()
    {
        var c = MirBuildTest.Compile(Prelude + """
            func fc(x: uniq/i32 during a, y: ref/i32 during a = x) => ()
            func fb(n: i32, x: uniq/i32, y: i32 = label work: do
                x@follow = n
                exit to work 1
            ) => ()
            """);
        var program = Analyze(c);
        Assert.Equal(
            ["x: Anchor uniq/i32 during a", "x@follow = n: Anchor uniq/i32"],
            program.Problems.All.ToArray().Select((x, i) => (x, i)).Where(static x => DefaultCodes.Contains(x.x.Code) && x.x.Subject.Module == 0).Select(x =>
                $"{Line(c, x.x.Subject)}: {string.Join(", ", program.Problems.Facts(x.i).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)}"))}"));
    }

    // V16 "F9 prepared inputs" (A3 6.2; VERIFICATION has no V16 table): a default keeping a Reborrow of each of N preceding parameters in its
    // result. Its work is dense, as its twin's, a function returning the same Tuple: each Reborrow's check scans the temps live across it
    // (shape 8's accesses × distinct live nodes); the template rules' own work, the template's over its twin's (R26-borrow's one gather per
    // Return and R26-access's pass over the Loans), is linear in N.
    [Fact]
    public void APreparedInputsWorkStaysLinear()
    {
        const string Template = "func fN(«N:x{0}: uniq/i32 during q, »y: («N:ref/i32 during q, »i32) = («N:x{0}, »0)) => ()\n";
        const string Twin = "func fN(«N:x{0}: uniq/i32 during q, ») -> («N:ref/i32 during q, »i32) => («N:x{0}, »0)\n";
        long[] template = [.. new[] { 16, 32, 64 }.Select(n => this.Measured(Prelude + Expand(Template, n, 0)).Work)];
        long[] twin = [.. new[] { 16, 32, 64 }.Select(n => this.Measured(Prelude + Expand(Twin, n, 0)).Work)];
        long[] rules = [.. template.Zip(twin, static (x, y) => x - y)];
        output.WriteLine($"N 16, 32, 64: work {string.Join(", ", template)}, twin {string.Join(", ", twin)}");
        Assert.True(Grows(template, 5), $"work {string.Join(", ", template)}");
        Assert.True(Grows(rules, 2), $"the rules' work {string.Join(", ", rules)}");
    }

    // V16: the template rules allocate nothing on a program version A5 checked before.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ATemplatesWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + "func fc(x: uniq/i32 during a, y: ref/i32 during a = x) => ()\n" +
            "func fb(x: uniq/i32, y: i32 = label work: do\n    x@follow = 9\n    exit to work 1\n) => ()\n" +
            "func excl<s/T>(x: s/T, y: s/T = x) -> ()\n    s is owner or ref or uniq\n    T is Copy\n    return\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(
            ["fc: x DefaultArgumentBorrow_Kd", "fb: x@follow = 9 DefaultArgumentAccess_Kd", "excl: x DefaultArgumentBorrow_Kd"],
            Problems(c, program, DefaultCodes));
    }

    // R-a (SPEC 7.2.3, 15.6.7; R4-M90 D10): an omitting call ends its inspection Loans, those of the pending slots it moves, before its plan
    // and checks, so its Moves of the slots meet none, and a copy's value keeps the Loans its slots' values carry. The 1c design's p15 (fc's
    // copy keeps a Reborrow through x's slot), its form with an implicitly acquired reservation (reservedBad, round 19's dc-reserved-bad),
    // grab's from a uniq argument (reborrowed) and a two-view generic caller's uniq view (gen, dc-gen2-bad-u) add nothing at the call: the
    // template's Borrow is the one record (R26-borrow, R4-M98). A Loan a copy's value keeps is the caller's Language problem, also where one
    // holder joins it with another value (joined; j1 on the path that keeps the copy; j1Clean, the copy in another holder), through a
    // generic copy (copied, copiedGeneric), a call inside the copy (called) and a result that retains the copy alone (onlyY, dc-copied);
    // an explicit argument's (given) and one another holder also keeps (both, copiedClean) stay. A destroying call's observing destruction
    // (err(destroy)) reports the Loan whichever holder comes first (dz, dzSwapped). A copied existing borrow keeps its caller Loan, the
    // problem's anchor (copied). R4-M70's default taint made each copied conflict a located Unsupported_Kd at the omitting call before.
    [Fact]
    public void AnOmittingCallEndsItsInspectionLoans()
    {
        var c = MirBuildTest.Compile(Prelude + Inspecting + """
            func p15()
                var n = 3
                fc(n@uniq)
            func reservedBad()
                var counter = Counter.init()
                counter.g()
            func reborrowed(u: uniq/i32) -> i32 => grab(u)
            func gen<s/T>(v: s/T)
                s is ref or uniq
                f2(v)
            func both()
                var k = 1
                let p = k@ref
                let r = same(p)
                k = 2
                inspect(r)
                inspect(p)
            func joined()
                var k = 1
                let r = same(k@ref)
                k = 2
                inspect(r)
            func j1(c: bool)
                var x = 1
                var o = 2
                let r = x@ref
                var h = r
                if c
                    h = pickR(o@ref)
                x = 2
                inspect(h)
            func j1Clean(c: bool)
                var x = 1
                var o = 2
                let r = x@ref
                var h = r
                var g = o@ref
                if c
                    g = pickR(o@ref)
                x = 2
                inspect(h)
                inspect(g)
            func given()
                var k = 1
                let r = first(k@ref)
                k = 2
                inspect(r)
            func copied()
                var k = 1
                let r = k@ref
                let y = pick(r)
                k = 2
                inspect(y)
            func copiedClean()
                var k = 1
                let r = k@ref
                let y = pick(r)
                k = 2
                inspect(r)
                inspect(y)
            func copiedGeneric<s/T>(z: s/T)
                s is owner or ref
                T is Copy
                var k = 1
                let r = k@ref
                let y = pick(r)
                k = 2
                inspect(y)
            func called()
                var k = 1
                let r = k@ref
                let y = via(r)
                k = 2
                inspect(y)
            func copiedY()
                var k = 1
                let r = onlyY(k@ref)
                k = 2
                inspect(r)
            func dz()
                var x: Array<i32> = [1, 2]
                var y: Array<i32> = [3]
                var s = C.init(r: x@uniq)
                let t = same(s.r@follow[0]@ref)
                let u = s.r@follow[0]@ref
                Kimi.Intrinsics.replace(s@uniq, with: C.init(r: y@uniq))
                inspect(t)
                inspect(u)
            func dzSwapped()
                var x: Array<i32> = [1, 2]
                var y: Array<i32> = [3]
                var s = C.init(r: x@uniq)
                let u = s.r@follow[0]@ref
                let t = same(s.r@follow[0]@ref)
                Kimi.Intrinsics.replace(s@uniq, with: C.init(r: y@uniq))
                inspect(t)
                inspect(u)
            """);
        var program = Analyze(c);
        Assert.Equal(
            [
                "g: self DefaultArgumentBorrow_Kd", "fc: x DefaultArgumentBorrow_Kd", "grab: a DefaultArgumentBorrow_Kd", "f2: x DefaultArgumentBorrow_Kd",
                "both: k = 2", "joined: k = 2", "j1: x = 2", "j1Clean: x = 2", "given: k = 2", "copied: k = 2", "copiedClean: k = 2",
                "copiedGeneric: k = 2", "called: k = 2", "copiedY: k = 2", "dz: Kimi.Intrinsics.replace(s@uniq, with: C.init(r: y@uniq))",
                "dzSwapped: Kimi.Intrinsics.replace(s@uniq, with: C.init(r: y@uniq))",
            ],
            Problems(c, program, DefaultCodes));
        Assert.Equal(["Anchor k@ref", "Use inspect(y)"], FactsOf(c, program, "copied"));
    }

    // R-a: a copy that only inspects its slots adds nothing at its omitting call (SPEC 7.2.3): ReceiverDefaultTest's ReservedMethod (round
    // 19's dc-reserved-inspect: a method call on the reserved input), the 1c design's fe (a read through the reserved slot), and the
    // omissions of p13's fb and DefaultAccessTest's fa, whose templates' Access is the one record (R4-M98).
    [Fact]
    public void AnInspectingCopyAddsNothingAtItsCall()
        => Assert.Equal(
            ["fb: x@follow = 9 DefaultArgumentAccess_Kd", "fa: x DefaultArgumentAccess_Kd"],
            AllProblems(Inspected + """
                func fe(x: uniq/i32, y: i32 = x@follow) => ()
                func fb(x: uniq/i32, y: i32 = label work: do
                    x@follow = 9
                    exit to work 1
                ) => ()
                func increment(x: uniq/i32) -> i32
                    x@follow += 1
                    return x@follow
                func fa(x: uniq/i32, y: i32 = increment(x)) => ()
                func reservedInspect()
                    var m = Meter.init()
                    _ = fm(m@uniq)
                func pe()
                    var m = 4
                    fe(m@uniq)
                func p13()
                    var n = 3
                    fb(n@uniq)
                func omittedFa()
                    var n = 1
                    fa(n@uniq)
                """));

    // R-a's root set (R4-M90 D10, the design review's B form): every argument of an omitting call is its moved pending slot, a reserved one's
    // reservation moved into its slot, so the prologue's inspection is a shared Borrow of a PendingSlot the call moves: ReceiverDefaultTest's
    // ReservedMethod shape, a method call on the reserved input.
    [Fact]
    public void AnInspectionBorrowsAPendingSlot()
    {
        var c = MirBuildTest.Compile(Prelude + Inspected + "func reservedInspect()\n    var m = Meter.init()\n    _ = fm(m@uniq)\n");
        var body = Body(Analyze(c), "reservedInspect");
        var call = body.Blocks.ToArray().Select(x => x.Terminator).Single(x => x is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "fm" });
        var moved = body.Operands.Slice(call.Start, call.Count).ToArray();
        Assert.All(moved, x => Assert.Equal(
            MirLocalRole.PendingSlot,
            x is { Kind: MirOperandKind.Move, Place.Projection: 0 } ? body.Locals[x.Place.Root].Role : default));
        var inspection = Assert.Single(
            body.Statements.ToArray(),
            x => x is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Ref, Op: (byte)MirRefKind.Shared, Place.Projection: 0 } } &&
                body.Locals[x.Rvalue.Place.Root].Role == MirLocalRole.PendingSlot);
        Assert.Equal(moved[0].Place, inspection.Rvalue.Place);
    }

    // SPEC 23.1.2 (DECISIONS R4-M127, R4-M139): a default the call's substitution does not instantiate is an Opaque value at the call
    // (MirBuildTest's ACopyTheCallDoesNotInstantiateIsAnOpaqueValue), and one over storage Binding rejected a copy (its
    // ACopyOverRejectedStorageIsBuilt), so no validator finding hides the caller's independent records: `v = 2` stays, over storage Binding
    // rejected and in a complete program past the storage depth, as in the twins. The depth limit at lim's and lim64's calls (the owner's
    // 「G272: (1)」, R4-M153) is A5's own, their retention past the depth bound (CallRetention.Limit, a genuine limit); a call over storage Binding rejected
    // keeps its other rows instead (DECISIONS BU-36, SPEC 23.1.2). Before, the copies' Type findings left both programs unanalyzed.
    [Fact]
    public void AnUninstantiableDefaultHidesNoRecord()
    {
        Assert.Equal(
            ["dep: v = 2", "dep3: v = 2", "dep2: v = 2", "val: v = 2", "val2: v = 2"],
            Analyzed(MirBuildTest.RejectedCopies));
        Assert.Equal(
            ["lim: f(b) OwnershipDepthLimit_Kd", "lim: v = 2", "lim64: f(b) OwnershipDepthLimit_Kd", "lim64: v = 2", "n30: v = 2"],
            Analyzed(MirBuildTest.LimitCopies));

        // The pipeline's own analyses (MirAnalysis), which analyze nothing in a program with a validator finding.
        static string[] Analyzed(string source)
        {
            var c = MirBuildTest.Compile(source);
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            return Problems(c, program, [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnershipDepthLimit_Kd]);
        }
    }

    // V16 "F9 omitting calls" (A3 6.2; VERIFICATION has no V16 table): N omitting calls in one body, each moving a slot its default inspected.
    // R-a's term is one memoized Remove per holder at each such call in every solve pass and in the replay (E8's dense term, N × holders);
    // it replaces the taint's two scope walks per pending-slot store. The shape's work is dense, as E8's ended points × holders are (2e-3
    // measured 92,830, 108,960, 165,923; 91,673, 104,859, 149,790 with the taint before).
    [Fact]
    public void AnOmittingCallsWorkStaysDense()
    {
        const string Shape = "func fI(a: ref/i32, n: i32 = size(a)) -> i32 => n\nfunc shape()\n    var x = 1\n«N:    _ = fI(x@ref)\n»";
        long[] work = [.. new[] { 16, 32, 64 }.Select(n => this.Measured(Prelude + Expand(Shape, n, 0)).Work)];
        output.WriteLine($"N 16, 32, 64: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
    }

    // V16: R-a's endings allocate nothing on a program version A5 checked before.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AnOmittingCallsWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + Inspecting + "func joined()\n    var k = 1\n    let r = same(k@ref)\n    k = 2\n    inspect(r)\n" +
            "func p15()\n    var n = 3\n    fc(n@uniq)\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(
            [
                "g: self DefaultArgumentBorrow_Kd", "fc: x DefaultArgumentBorrow_Kd", "grab: a DefaultArgumentBorrow_Kd", "f2: x DefaultArgumentBorrow_Kd",
                "joined: k = 2",
            ],
            Problems(c, program, DefaultCodes));
    }

    // Round 20's group State (2e-D §5; draft/Sketches/A5 Step 0/prototype.md, round 20): the mutable statics counter and other; poke borrows
    // its argument exclusively, touch writes counter (A4's key), peek and peekCount read it. Templates: add's default pokes counter (PLAN
    // G171; addOther and addRead the disjoint and read-only twins, Counter.add the effect form), f's omits g's (§4 row 1), f2 keeps its own
    // Loan across g's copy (row 2), g5 and add5 conflict inside themselves (r before the poke),
    // f5 omits g5's (row 5), local3 conflicts on its own local, addg is generic, addh has e17's generic loop, then g5's conflict, addl's
    // default passes A4's Limit, setd replaces a static whose old value's drop reads it (R3), gd's guard lets its candidate escape, keepd
    // copies a preceding argument (DefaultAccessTest :17's form) and effOwn's touch meets its own Loan of counter (err(effect)).
    private const string Copies = """
        struct Counter
            public var count: i32 = 0
            public init() => ()
            public func add(self: uniq/Self, x: i32 = State.touch()) => ()
            public func addO(self: uniq/Self, x: i32 = State.touchOther()) => ()
            public func addR(self: uniq/Self, x: i32 = State.peekCount()) => ()
            public func view(self: ref/Self) -> ref/i32 during self => self.count@ref
        struct Watch
            public var n: i32 = 0
            public init() => ()
            drop => _ = State.watch.n
        group Lim
            public func run() => _ = Nest<i32>.init(value: 1)
        group State
            public var counter: Counter = Counter.init()
            public var other: Counter = Counter.init()
            public var watch: Watch = Watch.init()
            public func inspect<T>(value: ref/T) => ()
            public func storeL(l: uniq/Loan<uniq/(ref/(ref/i32 during p) during q) during a>, v: ref/(ref/i32 during p) during q) => ()
            public func poke(c: uniq/Counter) -> i32 => 1
            public func peek(c: ref/Counter) -> i32 => 1
            public func touch() -> i32
                counter.count = 1
                return 1
            public func touchOther() -> i32
                other.count = 1
                return 1
            public func peekCount() -> i32 => counter.count
            public func add(c: uniq/Counter, x: i32 = State.poke(counter@uniq)) => ()
            public func addOther(c: uniq/Counter, x: i32 = State.poke(other@uniq)) => ()
            public func addRead(c: uniq/Counter, x: i32 = State.peek(counter@ref)) => ()
            public func g(x: i32 = State.poke(counter@uniq)) -> i32 => x
            public func f(y: i32 = State.g()) -> i32 => y
            public func f2(y: i32 = label w: do
                let r = counter@ref
                exit to w State.g() + r.count
            ) -> i32 => y
            public func g5(x: i32 = label w: do
                let r = counter@ref
                exit to w State.poke(counter@uniq) + r.count
            ) -> i32 => x
            public func f5(y: i32 = State.g5()) -> i32 => y
            public func add5(c: uniq/Counter, x: i32 = label w: do
                let r = counter@ref
                exit to w State.poke(counter@uniq) + r.count
            ) => ()
            public func local3(n: i32, y: i32 = label w: do
                var t = n
                let r = t@ref
                t = 2
                exit to w r@follow
            ) -> i32 => y
            public func addg<s/T>(a: s/T, c: uniq/Counter, x: i32 = State.poke(counter@uniq)) -> ()
                s is ref or uniq
                return
            public func addh(c: uniq/Counter, x: i32 = label w: do
                var x0 = 1
                var z: ref/i32 = x0@ref
                var w: ref/(ref/i32) = z@ref
                var lh = Loan<uniq/(ref/(ref/i32))>.init(w@uniq)
                var b: ref/i32 = x0@ref
                State.storeL(lh@uniq, b@ref)
                let r = counter@ref
                exit to w State.poke(counter@uniq) + r.count
            ) => ()
            public func addl(c: uniq/Counter, x: i32 = label w: do
                Lim.run()
                exit to w 1
            ) => ()
            public func setd(x: i32 = label w: do
                watch = Watch.init()
                exit to w 0
            ) -> i32 => x
            public func gd(y: i32 = label w: do
                let packet: Packet = .Data(Data.init())
                let other = Data.init()
                var saved = other@ref
                match packet
                    .Data(let d) if (label g: do
                        saved = d
                        exit to g true
                    ) => ()
                    _ => ()
                exit to w saved.count
            ) -> i32 => y
            public func keepd(a: ref/Data during p, b: ref/Data during p = a) -> ref/Data during p => b
            public func effOwn(y: i32 = label w: do
                let r = counter@ref
                let t = State.touch()
                exit to w t + r.count
            ) -> i32 => y

        """;

    // D11, D13 (SPEC 7.2.3, 15.6, 23.1.1; R4-M97, round 20): a failure found in an inline default copy is the caller's unless every event
    // taking part lies in one evaluation of one copy, and the caller's problem in a copy is recorded at the outermost omitting call with the
    // default's operation as its Evaluated fact. R4-M90 PQ-1: until P42 Binding reports every Borrow of counter or other here, written in a
    // default or a caller or a receiver's (fx), as the located Unsupported_Kd, so A5 meets none of their Loans: the caller-owned rows (G171,
    // its effect form, n1, twiceBoth, first, gen, held's omitting call) and the templates' static conflicts (f2, g5, add5, addh, effOwn, the
    // Limit addl's copy meets) are ACopysFailureIsItsTemplatesOnlyWithinOneEvaluationRooted's (R4-M107). Kept, with local Loans only: local3's
    // own conflict, held's and limit's callers' own conflicts (held's call rests on its written Borrow's record and inlines no copy) and a
    // guard across a copy (guardAcross, the caller's EndBorrow); template-owned
    // and omitted in the caller with the template's record: R3 in a copy (setd) and a guard escaping inside a copy (gd). Not Binding-valid, as
    // their earlier or sibling Loan rides in a default's value (SPEC 15.6: a Borrow of mutable static storage is bounded by the body): dc20-n3,
    // -n3top, -n4, -sib, -twice, -indep, dc20-loop, -loop-nested and the review's loop rows (dc20r-loop-e, -fx, -slot, -closure,
    // -nested-entry, -loop-guard); ALoopedCopysEarlierLoanIsTheCallers builds the loops' shape at the MIR layer.
    [Fact]
    public void ACopysFailureIsItsTemplatesOnlyWithinOneEvaluation()
    {
        var c = MirBuildTest.Compile(Prelude + Copies + """
                public func g171() => State.add(counter@uniq)
                public func g171Disjoint() => State.addOther(counter@uniq)
                public func g171Read() => State.addRead(counter@uniq)
                public func g171Explicit() => State.add(counter@uniq, State.poke(counter@uniq))
                public func fx() => counter.add()
                public func fxDisjoint() => counter.addO()
                public func fxRead() => counter.addR()
                public func fxExplicit() => counter.add(State.touch())
                public func n1() => State.add(counter@uniq, State.f())
                public func twiceBoth()
                    State.add(counter@uniq)
                    State.add(counter@uniq)
                public func first() => State.add5(counter@uniq)
                public func gen<s/T>(v: s/T)
                    s is ref or uniq
                    State.addg(v, counter@uniq)
                public func held()
                    var y0 = 1
                    let r0 = y0@ref
                    y0 = 2
                    inspect(r0)
                    State.addh(counter@uniq)
                public func limit()
                    var y0 = 1
                    let r0 = y0@ref
                    State.addl(counter@uniq)
                    y0 = 2
                    inspect(r0)
                public func guardAcross()
                    let packet: Packet = .Data(Data.init())
                    let other = Data.init()
                    var w = other@ref
                    match packet
                        .Data(let d) if (label g: do
                            w = State.keepd(d)
                            exit to g true
                        ) => ()
                        _ => ()
                    inspect(w)
                public func n2() => _ = State.f2()
                public func n5() => _ = State.f5()
                public func loopOwn(c: bool)
                    while c
                        _ = State.g5()
                public func loopLocal(c: bool)
                    while c
                        _ = State.local3(1)
                public func r3() => _ = State.setd()
                public func guardIn() => _ = State.gd()
                public func callEffOwn() => _ = State.effOwn()
                public func loopEffOwn(c: bool)
                    while c
                        _ = State.effOwn()
            """);
        var program = AnalyzeUnsupported(
            c, [.. Enumerable.Repeat("counter", 4), .. Enumerable.Repeat("counter@ref", 6), .. Enumerable.Repeat("counter@uniq", 18), "other@uniq"]);
        Assert.Equal(
            ["local3: t = 2", "setd: watch = Watch.init()", "gd: let d", "held: y0 = 2", "limit: y0 = 2", "guardAcross: let d"],
            Problems(c, program, DefaultCodes));
    }

    // D11's entry test and selection at the MIR layer (R4-M97): Binding admits no default whose value keeps a Loan its copy issued (SPEC
    // 15.6: a Borrow of mutable static storage is bounded by the body), so round 20's loops are hand-built bodies, as AR-9's are
    // (MirValidatorTest), each valid MIR. dc20-loop ("loop"): a loop holds the copy B, which writes x and then issues k = &x, and the
    // caller reads prev, then keeps k in it, so the next evaluation's write meets the last one's Loan, which prev carries into B's entry:
    // the caller's problem at B's omitting call (its source, 900), the default's operation its Evaluated fact. The same body is
    // dc20-loop-nested's template, f6's, whose own loop it is: there the record is f6's at keep2's call. "nested": that loop inside an
    // outer copy O (800), as f6's copy in its caller: O's template reports it, so the caller omits it (B's boundary moves out to O, whose
    // entry carries no earlier Loan). "own" (dc20-loop-own): B issues k = &x, writes x and reads k, its template's own failure; the caller
    // keeps k in prev but overwrites prev before its next use, so prev, dead at B's entry, carries nothing in (the gather reads the holders
    // live into the entry) and the failure stays B's. The gather's two other routes: "viaE", B stores k through a raw pointer argument, so
    // the earlier Loan reaches the entry in E; "closure", the caller keeps k in cell and only prev = &cell is live, so it reaches the entry
    // through the closure of prev's direct Loan (the group review's N3). R4-M69 dropped all five as their copies' (fail-before: "loop",
    // "viaE", "closure"; under R20Own alone those three would be omitted).
    [Fact]
    public void ALoopedCopysEarlierLoanIsTheCallers()
    {
        Assert.Equal(["ComparisonLoanConflict_Kd 900 [Anchor 12; Use 13; Evaluated 11]"], HandBuilt("loop"));
        Assert.Empty(HandBuilt("nested"));
        Assert.Empty(HandBuilt("own"));
        Assert.Equal(["ComparisonLoanConflict_Kd 900 [Anchor 12; Store 11; Evaluated 11]"], HandBuilt("viaE"));
        Assert.Equal(["ComparisonLoanConflict_Kd 900 [Anchor 12; Use 20; Evaluated 11]"], HandBuilt("closure"));
    }

    // D11's selection at the MIR layer (dc20-first, -first-rev): a covered candidate is skipped and the scan goes on, so the caller's Loan h
    // the copy's write meets is reported whether the copy's own Loan k comes first in the scan (the copy's holder after the caller's, the
    // order a built body has: "first") or second ("firstRev", the holders declared the other way round). R4-M69 dropped both.
    [Fact]
    public void ACoveredCandidateHidesNoLaterOne()
    {
        Assert.Equal(["ComparisonLoanConflict_Kd 900 [Anchor 1; Use 13; Evaluated 11]"], HandBuilt("first"));
        Assert.Equal(["ComparisonLoanConflict_Kd 900 [Anchor 1; Use 13; Evaluated 11]"], HandBuilt("firstRev"));
    }

    // A hand-built caller body, validated, through A5 (its sources are SourceRefs of module 0 by number; the copy B is the Inlined scope
    // whose source is 900, inside O, 800, for "nested"): see ALoopedCopysEarlierLoanIsTheCallers and ACoveredCandidateHidesNoLaterOne.
    // Each problem: its code, subject and facts by number.
    private static string[] HandBuilt(string form)
    {
        var c = MirBuildTest.Compile(
            "func w(f: bool)\n    var o = 3\n    var cell = o@ref\n    var prev = cell@ref\n    let p: raw/(ref/i32) = Raw.allocate(1)\n" +
            "    inspect(prev)\n    unsafe => inspect(p[0])\nfunc inspect<T>(value: ref/T) => ()\n");
        var program = c.BuildMir();
        program.Problems.Clear();
        var shape = Body(program, "w");
        var refs = shape.Statements.ToArray().Where(static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref }).ToArray();
        var (reference, twice, flagType) = (refs[0].Rvalue.Type!, refs[1].Rvalue.Type!, shape.Locals[1].Type); // ref/i32, ref/(ref/i32)
        var raw = shape.Locals.ToArray().First(static x => x.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw }).Type;
        var body = new MirBody();
        body.Reset(shape.Key, default);
        var outer = form == "nested" ? body.AddScope(0, MirScopeKind.Inlined, new(0, 800)) : 0;
        var copy = body.AddScope(outer, MirScopeKind.Inlined, new(0, 900));
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, default));
        MirPlace Local(BoundType type, MirLocalKind kind = MirLocalKind.Var) => new(body.AddLocal(new(type, kind, default)));
        SourceInfo At(int id, int scope) => new(new(0, id), (ushort)scope, true);
        int Constant(MirConstKind kind, BoundType type, long value) => body.AddOperand(MirOperand.Const(body.Const(new(kind, type, value))));
        void Assign(int block, MirPlace place, MirRvalue value, int id, int scope) => body.Push(block, MirStatement.Assign(place, value, At(id, scope)));
        void Write(int block, MirPlace place, int id, int scope)
            => Assign(block, place, MirRvalue.Use(Constant(MirConstKind.Integer, BoundType.I32, 2)), id, scope);
        void Borrow(int block, MirPlace into, MirPlace place, int id, int scope)
            => Assign(block, into, MirRvalue.Ref(MirRefKind.Shared, place, reference), id, scope);
        void Read(int block, MirPlace place, int id, int scope) => body.Push(block, MirStatement.Of(MirStatementKind.FakeRead, place, At(id, scope)));
        var entry = body.AddBlock();
        if (form is "first" or "firstRev")
        {
            var x = Local(BoundType.I32);
            var first = Local(reference, form == "first" ? MirLocalKind.Var : MirLocalKind.Temp); // The scan reads the later holder first.
            var (h, k) = form == "first" ? (first, Local(reference, MirLocalKind.Temp)) : (Local(reference), first);
            Write(entry, x, 2, 0);
            Borrow(entry, h, x, 1, 0); // The caller's Loan h.
            Borrow(entry, k, x, 12, copy); // B's own Loan k, then its write of x, which meets both.
            Write(entry, x, 11, copy);
            Read(entry, k, 16, copy);
            Read(entry, h, 13, 0);
        }
        else
        {
            var p = form == "viaE" ? Local(raw, MirLocalKind.Arg) : default; // A raw pointer argument, whose referent is E.
            var (flag, x, o, cell, k) = (Local(flagType), Local(BoundType.I32), Local(BoundType.I32), Local(reference), Local(reference, MirLocalKind.Temp));
            var prev = form == "closure" ? Local(twice) : cell; // For "closure", prev = &cell, through which cell is reached.
            var (head, loop, exit) = (body.AddBlock(), body.AddBlock(), body.AddBlock());
            Write(entry, x, 2, outer);
            Write(entry, o, 3, outer);
            Borrow(entry, cell, o, 4, outer);
            if (form == "closure")
            {
                Assign(entry, prev, MirRvalue.Ref(MirRefKind.Shared, cell, twice), 7, outer);
            }

            body.Terminate(entry, MirTerminator.Goto(head, At(5, outer)));
            body.AddCase(0, exit);
            body.Terminate(head, MirTerminator.SwitchInt(MirOperand.Copy(flag), body.Cases.Length - 1, 1, loop, At(6, outer)));
            if (form == "own")
            {
                Borrow(loop, k, x, 12, copy);
                Write(loop, x, 11, copy);
                Read(loop, k, 16, copy);
            }
            else
            {
                Write(loop, x, 11, copy);
                Borrow(loop, k, x, 12, copy);
                if (form != "viaE")
                {
                    Read(loop, prev, 13, outer);
                }
            }

            var (into, scope) = form == "viaE" ? (body.Project(p, MirProjectionKind.Deref, 0), copy) : (cell, outer);
            Assign(loop, into, MirRvalue.Use(body.AddOperand(MirOperand.Copy(k))), 14, scope);
            if (form == "closure")
            {
                Assign(loop, prev, MirRvalue.Ref(MirRefKind.Shared, cell, twice), 20, outer);
            }

            body.Terminate(loop, MirTerminator.Goto(head, At(15, outer)));
            Read(exit, prev, 17, outer);
            entry = exit;
        }

        Assign(entry, new(0), MirRvalue.Use(Constant(MirConstKind.Unit, BoundType.Unit, 0)), 18, 0);
        body.Terminate(entry, MirTerminator.Return(At(19, 0)));
        new Stratum(c.Types).Run(program, Validated(c, body));
        var problems = program.Problems.All.ToArray();
        return [.. problems.Select((x, i) =>
            $"{x.Code} {x.Subject.Id} [{string.Join("; ", program.Problems.Facts(i).ToArray().Select(static f => $"{f.Kind} {f.Site.Id}"))}]")];
    }

    // A hand-built body, which A5 reads only once MirValidator accepts it (HandBuilt, Rooted).
    private static MirBody Validated(Compilation c, MirBody body)
    {
        var findings = new List<MirFinding>();
        new MirValidator(c.Types).Validate(body, findings);
        Assert.Empty(findings);
        return body;
    }

    // Copies that issue Loans: fI's default borrows its own local, and fO's copy holds fI's (depth 2).
    private const string Issuing = """
        func fI(n: i32, y: i32 = label w: do
            var t = n
            let r = t@ref
            exit to w r@follow
        ) -> i32 => y
        func fO(n: i32, y: i32 = fI(n)) -> i32 => y

        """;

    // V16 "F9 copies" (A3 6.2; VERIFICATION has no V16 table): N Loan-issuing copies at depth 2 in a loop. Entry(B)'s terms are the pre-pass's
    // one pass over the Loans and the locations, one gather per issuing copy's entry in the replay (a check's Based pass, with the live words),
    // and an Add only for a Loan carried in from an earlier evaluation (none here); Covered runs only for a conflicting candidate. The copies'
    // work over their explicit-argument twin's stays within the body's dense terms (E8's ended points × holders, as each copy adds a holder).
    [Fact]
    public void ACopysEntryWorkStaysDense()
    {
        const string Loop = "func shape(c: bool)\n    while c\n";
        long[] copies = [.. new[] { 16, 32, 64 }.Select(n => this.Measured(Prelude + Issuing + Loop + Expand("«N:        _ = fO(1)\n»", n, 0)).Work)];
        long[] twin = [.. new[] { 16, 32, 64 }.Select(n => this.Measured(Prelude + Issuing + Loop + Expand("«N:        _ = fO(1, 2)\n»", n, 0)).Work)];
        long[] own = [.. copies.Zip(twin, static (x, y) => x - y)];
        output.WriteLine($"N 16, 32, 64: work {string.Join(", ", copies)}, twin {string.Join(", ", twin)}");
        Assert.True(Grows(copies, 5), $"work {string.Join(", ", copies)}");
        Assert.True(Grows(own, 5), $"the copies' work {string.Join(", ", own)}");
    }

    // V16: the copies' entries allocate nothing on a program version A5 checked before (nested copies in a loop, and a caller's loop around
    // local3's copy, whose own failure is its template's; R4-M90 PQ-1: the caller's loop of round 20's G171 form borrows a static, so its
    // warm run is ACopysCallerRecordWarmRunAllocatesNothing's, R4-M107).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ACopysEntriesWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + Issuing + "func shape(c: bool)\n    while c\n        _ = fO(1)\n        _ = fO(2)\n" + Copies +
            "    public func loopLocal(c: bool)\n        while c\n            _ = State.local3(1)\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Contains("local3: t = 2", Problems(c, program, DefaultCodes));
    }

    // A function's one problem's facts: kind and its site's first line.
    private static string[] FactsOf(Compilation c, MirProgram program, string function)
    {
        var problem = Array.FindIndex(program.Problems.All.ToArray(), x => program.Bodies[x.Body].Key.Decl?.Name == function);
        return [.. program.Problems.Facts(problem).ToArray().Select(x => $"{x.Kind} {Line(c, x.Site)}")];
    }

    // The codes of A5's Loan stratum, its template rules and its limit records (a located Unsupported_Kd, the depth limit).
    private static DiagnosticCode[] DefaultCodes => [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnershipDepthLimit_Kd,
        DiagnosticCode.DefaultArgumentAccess_Kd, DiagnosticCode.DefaultArgumentBorrow_Kd];

    // The Loan, default and Unsupported_Kd problems of a program's own bodies.
    private static string[] AllProblems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        return Problems(c, Analyze(c), DefaultCodes);
    }
}
