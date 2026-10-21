// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
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
        func rr(x: uniq/i32 during a, z: ref/i32 during a, n: i32, y: ref/i32 during a = if n == 0 => z else => label w: do
            var local = 0
            var other = 0
            _ = rr(local@uniq, other@ref, n - 1)
            exit to w z
        ) -> ref/i32 during a => y
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

    // r3 (R4-M93; PLAN G160, registered for X-2): a default may write through a raw pointer it reads from a slot (SPEC 5.2.1), which no
    // row of its template call follows, so a template call whose slot's Type holds raw authority while its inputs' values hold a Loan is a
    // located Unsupported_Kd at the call, never a Language problem (storing every input into E would make the valid `w = 5` an error,
    // R4-M93): a raw-only slot (rawRec, which still errs at `v = 5` by the omitting call's own row, rule G's raw clause),
    // one beside an input the default never stores (unrelated: `w = 5` is valid; in its local twin `var v = 1` is SPEC's error, v's end
    // while the call's own row keeps v's Loan in raw storage, which may be used after every point, SPEC 15.6, as `rawStoredLocal`'s), a
    // raw pointer in a holder that holds a Loan (hid, an unsound acceptance before) and its valid twin; inputs whose values hold no Loan
    // give none (rawParam).
    [Fact]
    public void ARawSlotAtATemplateCallIsUnsupported()
        => Assert.Equal(
            [
                "rawRec: putr(rp, v@ref, 3) Unsupported_Kd", "rawRec: v = 5", "unrelated: putz(rp, v, w@ref, 3) Unsupported_Kd",
                "unrelatedLocal: putz(rp, v@ref, w@ref, 3) Unsupported_Kd", "unrelatedLocal: var v = 1", "hid: putb(box@ref, v@ref, 3) Unsupported_Kd",
                "hidValid: putb(box@ref, v@ref, 3) Unsupported_Kd",
            ],
            AllProblems(Recursive + """
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

    // V16: template calls, r3's walk included, allocate nothing on a program version A5 checked before.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ATemplateCallsWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + Recursive + "func live()\n    var n = 3\n    let r = f(3, n@ref)\n    n = 4\n    _ = r@follow\n" +
            "func hid(z: ref/i32 during q)\n    var v = 1\n    var box = RawBox.init(p: Raw.allocate(1), r: z)\n    _ = putb(box@ref, v@ref, 3)\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(["live: n = 4", "hid: putb(box@ref, v@ref, 3) Unsupported_Kd"], Problems(c, program, [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
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
    // checks, D5), an exclusive capture entry (round 19's dt-cap-uniq, whose `[x@uniq]` of an i32 slot Binding rejects, InvalidAssignment_Kd:
    // the Binding-valid `[x@follow@uniq]` instead), and a write that stores a Reborrow of another slot into storage the body does not own,
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
                    let c = func [x@follow@uniq] () => x@follow
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

    // R4-M73 (R4-M98 D7): R26 records through the body hold, so a held template's (g1's loop inside the default) is the located
    // Unsupported_Kd at the Borrow, beside the hold's own record.
    [Fact]
    public void AHeldTemplatesSlotProblemIsUnsupported()
        => Assert.Equal(
            ["heldT: xs.append(b) Unsupported_Kd", "heldT: var xs: Array<T> = [] Unsupported_Kd", "heldT: x Unsupported_Kd"],
            AllProblems("""
                func heldT<T>(a: T, b: T, x: uniq/i32 during q, y: ref/i32 during q = label w: do
                    var xs: Array<T> = []
                    xs.append(a)
                    for e in xs
                        _ = e
                    xs.append(b)
                    exit to w x
                ) -> ()
                    T is Copy
                    return
                """));

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

    // A function's one problem's facts: kind and its site's first line.
    private static string[] FactsOf(Compilation c, MirProgram program, string function)
    {
        var problem = Array.FindIndex(program.Problems.All.ToArray(), x => program.Bodies[x.Body].Key.Decl?.Name == function);
        return [.. program.Problems.Facts(problem).ToArray().Select(x => $"{x.Kind} {Line(c, x.Site)}")];
    }

    // The codes of A5's Loan stratum, its template rules and its located Unsupported_Kd.
    private static DiagnosticCode[] DefaultCodes => [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.DefaultArgumentAccess_Kd,
        DiagnosticCode.DefaultArgumentBorrow_Kd];

    // The Loan, default and Unsupported_Kd problems of a program's own bodies.
    private static string[] AllProblems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        return Problems(c, Analyze(c), DefaultCodes);
    }
}
