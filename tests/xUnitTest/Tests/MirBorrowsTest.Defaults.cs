// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5-F9's default rules (SPEC 7.2.3, 15.6.4, 15.6.7; docs/dev/DECISIONS.md R4-M90, R4-M93): a recursive default's template call
/// carries the Loans of every preceding argument into its value (P9), and one whose slot holds raw authority beside a Loan is a located
/// Unsupported_Kd (r3).</summary>
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

    // The Loan and Unsupported_Kd problems of a program's own bodies.
    private static string[] AllProblems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        return Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]);
    }
}
