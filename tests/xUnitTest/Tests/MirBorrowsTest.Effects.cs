// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's effect rule (SPEC 15.6.4, 15.6.7, 16.3.3, 16.4, 22.4.3; docs/dev/DECISIONS.md R4-M92; prototype round 18's e1–e17): a
/// Call's or a Drop's effects, by A4's keys, against the live Loans on mutable static roots. Binding accepts the implicit static Loans of a
/// group's own members only (a receiver reserved or shared with a retained result, an argument's adaptation, R4-M90's PQ-1), so the
/// programs are members of group State.</summary>
public sealed partial class MirBorrowsTest
{
    // The effect rule's Types and group State: statics, an initializer that writes another static (x's), destructors that read, write or
    // pass A4's Limit (Lim.run destroys a growing Nest<i32>, past the destruction walk's bound), and helpers that read and write statics.
    private const string EffectTypes = """
        struct Counter
            public var n: i32 = 0
            public init() => ()
            public func add(self: uniq/Self, k: i32) => self.n = self.n + k
            public func bump(self: uniq/Self) => _ = State.counter.n
            public func reset(self: uniq/Self) => State.counter = Counter.init()
            public func view(self: ref/Self) -> ref/i32 during self => self.n@ref
            public func viewU(self: uniq/Self) -> uniq/i32 during self => self.n@uniq
            public func guard(self: ref/Self) -> Guard during self => Guard.init(r: self.n@ref)
            public func peek(self: ref/Self) -> Peek during self => Peek.init(r: self.n@ref)
        struct Guard {a}
            public var r: ref/i32 during a
            public init(r: ref/i32 during a) => self.r = r
            drop => State.counter = Counter.init()
        struct Peek {a}
            public var r: ref/i32 during a
            public init(r: ref/i32 during a) => self.r = r
            drop => _ = State.counter.n
        struct Watch
            public var n: i32 = 0
            public init() => ()
            drop => _ = State.watch.n
        struct XReader
            public var n: i32 = 0
            public init() => ()
            drop => _ = State.x.n
        struct Quiet
            public var n: i32 = 0
            public init() => ()
            drop => ()
        struct Limited
            public var n: i32 = 0
            public init() => ()
            drop => Lim.run()
        group Lim
            public func run() => _ = Nest<i32>.init(value: 1)
        group State
            public var counter: Counter = Counter.init()
            public let limits: Counter = Counter.init()
            public var x: Counter = State.makeX()
            public var log: Counter = Counter.init()
            public var d: Counter = State.makeD()
            public var x2: Counter = State.makeX2()
            public let lim2: Counter = State.makeL()
            public var watch: Watch = Watch.init()
            public var quiet: Quiet = Quiet.init()
            public var limited: Limited = Limited.init()
            public var action: () -> () = func [] () => ()
            public var list: Array<i32> = []
            public var refs: Array<ref/i32> = []
            public var count: i32 = 0
            public func makeX() -> Counter
                log = Counter.init()
                return Counter.init()
            public func makeD() -> Counter
                log = Counter.init()
                return Counter.init()
            public func makeX2() -> Counter
                _ = d.n
                return Counter.init()
            public func makeL() -> Counter
                log = Counter.init()
                return Counter.init()
            public func useX() -> i32 => State.x.n
            public func useLim() -> i32 => State.lim2.n
            public func writeLog() => State.log = Counter.init()
            public func inspect<T>(value: ref/T) => ()
            public func reads() -> i32 => State.counter.n
            public func writes() -> i32
                State.counter = Counter.init()
                return 1
            public func sink<T>(value: T) -> i32 => 1
            public func takes(value: ref/i32) => State.count = 1
            public func clears() => State.list = []
            public func length() -> isize => list.length

        """;

    // SPEC 15.6.4, 15.6.7 (R1, W6, DECISIONS R4-M92, R4-M72 as clarified): a Call's effects meet the live Loans on mutable static roots in
    // the set its last operand takes, every operand's value with no holder left out: a write key or an unknown effect any Loan (e3, e6,
    // e11, the argument's adaptation, a `for` over a Slice of a static calling a writer, e15's abstract destruction), a read key a unique
    // one only (e2, e7, the reader), and a reservation the call activates is unique at its effects (W6: e4), while its operands' accesses
    // see it shared. A Loan found in a reservation holder the call does not activate is CallReservationConflict_Kd (DIAGNOSTICS' row 1:
    // e1, e3, e15), every other ComparisonLoanConflict_Kd, an activation's own conflict on the same Loan a second problem (e4c). Loans on
    // immutable statics are never met (e11b), nor dead ones (e14); a compound assignment secures its right operand before it reserves its
    // target (SPEC 13.7.2), so its call meets nothing (e1's writeback). Fail-before: each error, none reported before 2b-3.
    [Fact]
    public void ACallsEffectsMeetTheLiveStaticLoans()
        => Assert.Equal(
            [
                "e1: g() CallReservationConflict_Kd", "e3: writes() CallReservationConflict_Kd", "e4: counter.bump()",
                "e4c: counter.reset() CallActivationConflict_Kd", "e4c: counter.reset()", "e6: let g = counter.guard()", "e11: g()", "adapted: takes(count)",
                "forWriter: clears()", "e15: sink(x@move) CallReservationConflict_Kd",
            ],
            Effects("""
                    public func e1(g: () -> i32) => counter.add(g())
                    public func e1Writeback(g: () -> i32) => State.count += g()
                    public func e2() => counter.add(reads())
                    public func e3() => counter.add(writes())
                    public func e4() => counter.bump()
                    public func e5() => counter.add(1)
                    public func e4c()
                        let c = counter.view()
                        counter.reset()
                        inspect(c)
                    public func e6()
                        let g = counter.guard()
                        inspect(g)
                    public func e7()
                        let p = counter.peek()
                        inspect(p)
                    public func e11(g: () -> ())
                        let c = counter.view()
                        let l = limits.view()
                        g()
                        inspect(c)
                        inspect(l)
                    public func e11b(g: () -> ())
                        let l = limits.view()
                        g()
                        inspect(l)
                    public func e14(g: () -> ())
                        let c = counter.view()
                        inspect(c)
                        g()
                    public func adapted() => takes(count)
                    public func forWriter()
                        for v in list.slice()
                            clears()
                    public func forReader()
                        for v in list.slice()
                            _ = length()
                    public func e15<T>(x: T) => counter.add(sink(x@move))
                    public func e15b<T>(x: T)
                        T is Copy
                        counter.add(sink(x))

                """));

    // SPEC 16.4 with 16.3.1, 16.3.3 (R3) and 15.6.4: destroying a mutable static's old value holds an exclusive Loan on it until it
    // completes, so its own effects on that static conflict, a read (e8) or an erased destruction's unknown effects (e9), at the
    // assignment's Drop, with no Loan issued; a quiet destructor does not (e8b), one past A4's Limit is the located Unsupported_Kd (e8c), and
    // exchange is the way out (e9b). An erased destruction of a local meets every live mutable-static Loan (SPEC 16.3.3's example, e9s).
    // Fail-before: e8, e8c, e9 and e9s.
    [Fact]
    public void ADestructionMeetsItsOwnStaticAndTheLiveStaticLoans()
        => Assert.Equal(
            ["e8: watch = Watch.init()", "e8c: limited = Limited.init() Unsupported_Kd", "e9: action = func []() => ()", "e9s: local = func []() => ()"],
            Effects("""
                    public func e8() => watch = Watch.init()
                    public func e8b() => quiet = Quiet.init()
                    public func e8c() => limited = Limited.init()
                    public func e9() => action = func [] () => ()
                    public func e9b() => _ = Kimi.Intrinsics.exchange(action@uniq, with: func [] () => ())
                    public func e9s(g: () -> ())
                        var local: () -> () = g
                        let c = counter.view()
                        local = func [] () => ()
                        inspect(c)

                """));

    // SPEC 15.6.4, 22.4.3 (R4, PD12 (ii) as written, OQ-F5-6): a staticEnsure runs its static's initializer, whose effects meet the live
    // Loans (e10b) unless a live Loan on the ensured static proves it initialized. Liveness is a may-property, so a Loan carried on one path
    // proves nothing on another (e10f), and under P5 a key is its Field (e10d): wherever the proof would decide, each conflict is the located
    // Unsupported_Kd at the ensure (e10a, e10d, e10e, e10f); with nothing to meet, nothing (e10c). Fail-before: each.
    [Fact]
    public void AnEnsureWhoseProofWouldDecideIsUnsupported()
    {
        const string Cache = """
                struct Cache<T>
                    public group S
                        public var x: Counter = S.makeX()
                        public var log: Counter = Counter.init()
                        public func makeX() -> Counter
                            log = Counter.init()
                            return Counter.init()
                        public func inspect<U>(value: ref/U) => ()
                        public func e10e()
                            let lx = x.view()
                            let ll = log.view()
                            let v = x.n
                            inspect(lx)
                            inspect(ll)
                        public func e10d()
                            let lx = x.view()
                            let ll = log.view()
                            let v = Cache<string>.S.x.n
                            inspect(lx)
                            inspect(ll)

                """;
        Assert.Equal(
            ["e10a: x Unsupported_Kd", "e10b: x", "e10f: x Unsupported_Kd", "e10e: x Unsupported_Kd", "e10d: Cache<string>.S.x Unsupported_Kd"],
            Effects(
                """
                    public func e10a()
                        let lx = x.view()
                        let ll = log.view()
                        let v = x.n
                        inspect(lx)
                        inspect(ll)
                    public func e10b()
                        let ll = log.view()
                        let v = x.n
                        inspect(ll)
                    public func e10c()
                        let lx = x.view()
                        let v = x.n
                        inspect(lx)
                    public func e10f(k: bool)
                        var o = 1
                        var lx = o@ref
                        if k => lx = x.view()
                        let ll = log.view()
                        let v = x.n
                        inspect(lx)
                        inspect(ll)

                """,
                Cache));
    }

    // SPEC 15.6.4, 22.4.3 (DECISIONS R4-M92, R4-M89's PD12 (ii) as the planning session read it after the group review): every first
    // access of a static is an ensure, which A4 composes into every site that may run it, so the proof applies wherever a conflict's
    // deciding effect lies in the initializer of a static a live Loan is on and the site may first-access that static: a call that reads
    // x (pA), the ensure of x2, whose initializer reads d (pB), a call that reads the immutable lim2 (pQ), a destructor that reads x (the
    // early Drop): each the located Unsupported_Kd. Without the proof Loan each is the error (pA2, pB2), and so is a site that never
    // reaches d although a Loan on d lives, its conflict a direct one (direct). Fail-before: pA, pB, pQ and the early Drop (errors).
    [Fact]
    public void AnInitializationProofDecidesWhereverAnInitializerComposes()
        => Assert.Equal(
            [
                "pA: useX() Unsupported_Kd", "pA2: useX()", "pB: x2 Unsupported_Kd", "pB2: x2", "pQ: useLim() Unsupported_Kd",
                "direct: writeLog()", "early: let r = XReader.init() Unsupported_Kd",
            ],
            Effects("""
                    public func pA()
                        let lx = x.view()
                        let ll = log.view()
                        _ = useX()
                        inspect(lx)
                        inspect(ll)
                    public func pA2()
                        let ll = log.view()
                        _ = useX()
                        inspect(ll)
                    public func pB()
                        let ld = d.view()
                        let ll = log.view()
                        let v = x2.n
                        inspect(ld)
                        inspect(ll)
                    public func pB2()
                        let ll = log.view()
                        let v = x2.n
                        inspect(ll)
                    public func pQ()
                        let lm = lim2.view()
                        let ll = log.view()
                        _ = useLim()
                        inspect(lm)
                        inspect(ll)
                    public func direct()
                        let ld = d.view()
                        let ll = log.view()
                        writeLog()
                        inspect(ld)
                        inspect(ll)
                    public func early()
                        let lx = x.view()
                        let ll = log.view()
                        do
                            let r = XReader.init()
                        inspect(lx)
                        inspect(ll)

                """));

    // AGENTS Diagnostics, R4-M10 (A4's classes, R4-M91): a call A4 does not classify (Limit) is the located Unsupported_Kd where a live
    // mutable-static Loan would be met (e12a) and nothing where none is (e12b), Unknown beside it decides (e12c); a callee's dependent of an
    // error (Derived) reports nothing by itself (e13a), and a key beside it stays checked (e13b). Fail-before: e12a, e12c and e13b.
    [Fact]
    public void ALimitIsUnsupportedAndADerivedEffectReportsNothing()
    {
        Assert.Equal(
            ["e12a: Lim.run() Unsupported_Kd", "e12c: both(g@move)"],
            Effects("""
                    public func e12a()
                        let c = counter.view()
                        Lim.run()
                        inspect(c)
                    public func e12b()
                        let c = counter.view()
                        inspect(c)
                        Lim.run()
                    public func both(g: () -> ())
                        Lim.run()
                        g()
                    public func e12c(g: () -> ())
                        let c = counter.view()
                        both(g@move)
                        inspect(c)

                """));
        const string Derived = """
                    public func opaque() => _ = missing
                    public func opaqueWrite()
                        _ = missing
                        State.counter = Counter.init()
                    public func e13a()
                        let c = counter.view()
                        opaque()
                        inspect(c)
                    public func e13b()
                        let c = counter.view()
                        opaqueWrite()
                        inspect(c)

                """;
        Assert.Equal(["e13b: opaqueWrite()"], RejectedProblems(EffectTypes + Derived));
    }

    // SPEC 15.6, 15.1.3, 15.6.3 (G136, DECISIONS R4-M92, OQ-F5-1 (a)): a whole write of a static ends its Field's Loans, as destroying a Move
    // Path does, so a write meeting a live Loan is one problem however often the static is written again (e16b; two before), and a dead Loan
    // meets nothing (e16c). A direct Loan on a static names E, the storage the body does not own (SPEC 15.6), so a call's store through a
    // static's reservation lands in E, as a parameter's does (stored; fail-before: the store went nowhere).
    [Fact]
    public void AStaticsLoanNamesEAndAWholeWriteEndsIt()
        => Assert.Equal(
            ["e16a: counter = Counter.init()", "e16b: counter = Counter.init()", "stored: y = 2"],
            Effects("""
                    public func e16a()
                        let c = counter.view()
                        counter = Counter.init()
                        inspect(c)
                    public func e16b()
                        let c = counter.view()
                        counter = Counter.init()
                        counter = Counter.init()
                        inspect(c)
                    public func e16c()
                        let c = counter.view()
                        inspect(c)
                        counter = Counter.init()
                    public func stored()
                        var y = 1
                        refs.append(y@ref)
                        y = 2

                """));

    // R4-M73, R4-M70 (with R4-M92): an effect conflict is a Loan problem, so in a view a call's by-argument store holds it is the located
    // Unsupported_Kd at its call (e17), and one whose Loan only a default's tainted sets hold is the located Unsupported_Kd at the omitting
    // call (tainted). Fail-before: e17's g() and tainted; the `var xs` record is the held view's own (R4-M73), with or without the static
    // Loan.
    [Fact]
    public void AHeldOrTaintedEffectIsUnsupported()
        => Assert.Equal(
            ["e17: g() Unsupported_Kd", "e17: var xs: Array<T> = [] Unsupported_Kd", "tainted: same(counter.view()) Unsupported_Kd"],
            Effects("""
                    public func e17<T>(a: T, g: () -> ())
                        T is Copy
                        var xs: Array<T> = []
                        xs.append(a)
                        for e in xs
                            _ = e
                        let c = counter.view()
                        g()
                        inspect(c)
                    public func same(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y
                    public func tainted(g: () -> ())
                        let r = same(counter.view())
                        g()
                        inspect(r)

                """));

    // SPEC 15.6.4's widened effects (tests/diagnostics/spec-cases.json 15.6.4-widened-effects, its Borrow implicit; R4-M87): a summary
    // that would keep an effect on a projection with a recursive base has unknown environment effects, which conflict with every live
    // mutable-static Loan, even where the caller's premises would classify every effect it replaces. Fail-before.
    [Fact]
    public void AWidenedEffectMeetsTheLiveStaticLoans()
    {
        var c = MirBuildTest.Compile(EffectTypes + """
                contract Branch
                    associate Child is Branch
                    func child(self: ref/Self) -> Option<Self.Child>
                        effect confined
                contract CopyBranch: Branch, Copy
                    Self.(Branch).Child is CopyBranch
                group Trees
                    public func walk<T>(node: T, depth: i32)
                        T is Branch
                        if depth == 0 => return
                        match node.child()
                            .Some(let next) => Trees.walk(next@move, depth - 1)
                            .None => return
                group Counters
                    public var count: Counter = Counter.init()
                    public func inspect<T>(value: ref/T) => ()
                    public func visitCopies<U>(node: U)
                        U is CopyBranch
                        let c = count.view()
                        Trees.walk(node, 2)
                        inspect(c)
                struct Nest<T>
                    public var value: T
                    public var next: Option<rc/Nest<(T, T)>> = .None
                    public init(value: T) => self.value = value

                """);
        Assert.Equal(["visitCopies: Trees.walk(node, 2)"], Problems(c, Analyze(c), LoanCodes));
    }

    // PD31, AGENTS Diagnostics (R4-M92): an effect problem records typed facts where it fails: its Loan's anchor, its witness, and the
    // static Field it met, by what decided it, a write key, else Unknown, else a read key, at the Field's declaration; R3's is anchored at
    // its Drop and has no witness, since no value carries the destruction's hold. Fail-before: each problem.
    [Fact]
    public void AnEffectProblemNamesTheFieldItMet()
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + """
                public func e3() => counter.add(writes())
                public func e11(g: () -> ())
                    let c = counter.view()
                    g()
                    inspect(c)
                public func unique()
                    let u = counter.viewU()
                    _ = reads()
                    u@follow = 1
                public func e8() => watch = Watch.init()

            """);
        var program = Analyze(c);
        Assert.Equal(
            [
                "writes(): Anchor counter, Use counter.add(writes()), EffectWrite public var counter: Counter = Counter.init()",
                "g(): Anchor counter, Use inspect(c), EffectUnknown public var counter: Counter = Counter.init()",
                "reads(): Anchor counter, Use u@follow = 1, EffectRead public var counter: Counter = Counter.init()",
                "watch = Watch.init(): Anchor watch = Watch.init(), EffectRead public var watch: Watch = Watch.init()",
            ],
            program.Problems.All.ToArray().Select((x, i) => (x, i)).Where(static x => LoanCodes.Contains(x.x.Code) && x.x.Subject.Module == 0).Select(x =>
                $"{Line(c, x.x.Subject)}: {string.Join(", ", program.Problems.Facts(x.i).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)}"))}"));
    }

    // R4-M11 (R4-M91, R4-M92): a body no view decides is checked once for its views, but its calls' keys are each view's: forward's owner
    // case destroys the Noisy it moves into hold, whose destructor writes G.x while c lives, and its reference case does not, so the
    // conflict has view 0 only. Fail-before.
    [Fact]
    public void AnEffectConflictHasTheViewsWhoseKeysMeetIt()
    {
        var c = MirBuildTest.Compile(EffectTypes + """
            struct Noisy
                public var n: i32 = 0
                public init() => ()
                drop => G.x = Counter.init()
            group G
                public var x: Counter = Counter.init()
                public func inspect<T>(value: ref/T) => ()
                public func hold<s/T>(value: s/T)
                    s is owner or ref
                    ()
                public func forward<s/T>(value: s/Noisy, marker: s/T)
                    s is owner or ref
                    let c = x.view()
                    hold(value@move)
                    inspect(c)
            struct Nest<T>
                public var value: T
                public var next: Option<rc/Nest<(T, T)>> = .None
                public init(value: T) => self.value = value

            """);
        var program = Analyze(c);
        var forward = Body(program, "forward");
        Assert.Equal(2, MirCases.Count(forward));
        Assert.Equal(["hold(value@move) 1"], program.Problems.All.ToArray().Where(x => x.Body == forward.Ordinal && LoanCodes.Contains(x.Code))
            .Select(x => $"{Line(c, x.Subject)} {x.Views}"));
    }

    // V16 "F5 effects" (DECISIONS R4-M92): one body with M Loans on M mutable statics live across N calls, each to a function writing one
    // of K other statics, checks each call's keys once per effect root (A4's row, the root's two bits and its candidates): the work grows
    // linearly in N at M = 4, K = 8 and within the bound's M × (holders) term in M at N = 32. A guard, not a regression: no problem arises.
    [Fact]
    public void EffectsScaleWithCallsAndStaticLoans()
    {
        static string Shape(int n, int m, int k)
        {
            var text = new StringBuilder(EffectTypes).Append("group F\n");
            for (var i = 0; i < m; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var m{i}: Counter = Counter.init()\n");
            }

            for (var j = 0; j < k; j++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var k{j}: i32 = 0\n    public func w{j}() => F.k{j} = 1\n");
            }

            text.Append("    public func inspect<T>(value: ref/T) => ()\n    public func shape()\n");
            for (var i = 0; i < m; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        let c{i} = m{i}.view()\n");
            }

            for (var call = 0; call < n; call++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        w{call % k}()\n");
            }

            for (var i = 0; i < m; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        inspect(c{i})\n");
            }

            return text.Append(Prelude).ToString();
        }

        long[] inN = [.. new[] { 32, 64, 128 }.Select(n => this.Measured(Shape(n, 4, 8)).Work)];
        long[] inM = [.. new[] { 8, 16, 32 }.Select(m => this.Measured(Shape(32, m, 8)).Work)];
        output.WriteLine($"N 32, 64, 128 at M 4: work {string.Join(", ", inN)}; M 8, 16, 32 at N 32: work {string.Join(", ", inM)}");
        Assert.True(Grows(inN, 2), $"work in N {string.Join(", ", inN)}");
        Assert.True(Grows(inM, 4), $"work in M {string.Join(", ", inM)}");
    }

    // V16 (R4-M92): A5 with the effect rule on a program version it checked before allocates nothing: the effect roots, the Field numbers,
    // the activation stamps and A4's row keep their storage, each view's keys are A4's pooled row and an initializer's summary its stored
    // one (pA's proof). Fail-before: the effect problems.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmEffectRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + """
                public func e1(g: () -> i32) => counter.add(g())
                public func e6()
                    let g = counter.guard()
                    inspect(g)
                public func e8() => watch = Watch.init()
                public func e10a()
                    let lx = x.view()
                    let ll = log.view()
                    let v = x.n
                    inspect(lx)
                    inspect(ll)
                public func e16b()
                    let c = counter.view()
                    counter = Counter.init()
                    counter = Counter.init()
                    inspect(c)
                public func pA()
                    let lx = x.view()
                    let ll = log.view()
                    _ = useX()
                    inspect(lx)
                    inspect(ll)

            """);
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(4, program.Problems.All.ToArray().Count(static x => LoanCodes.Contains(x.Code) && x.Subject.Module == 0));
    }

    // The Loan problems and located Unsupported_Kd records of a program: the prelude, the effect Types and group State with the given
    // members, then the given declarations.
    private static string[] Effects(string members, string declarations = "")
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + members + declarations);
        return Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]);
    }
}
