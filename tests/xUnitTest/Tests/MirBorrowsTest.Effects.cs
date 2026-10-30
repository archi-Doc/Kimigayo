// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's effect rule (SPEC 15.6.4, 15.6.7, 16.3.3, 16.4, 22.4.3; docs/dev/DECISIONS.md R4-M92; prototype round 18's e1–e17): a
/// Call's or a Drop's effects, by A4's keys, against the live Loans on mutable static roots. Until P42 Binding reports every Borrow SPEC
/// lends of a static Place, written or implicit (a receiver's, an argument's adaptation), as the located Unsupported_Kd (R4-M90 PQ-1,
/// DECISIONS BU-25), so these programs, members of group State, reach A5 with no live static Loan: each test asserts those records and
/// what A5 still finds (a static's destruction, which takes no Loan), and its twin in MirBorrowsTest.Rooted keeps the rule's witnesses
/// (R4-M107).</summary>
public sealed partial class MirBorrowsTest
{
    // The effect rule's Types and group State: statics, an initializer that writes another static (x's), destructors that read, write or
    // pass A4's Deep (Lim.run destroys a growing Nest<i32>, past the destruction walk's bound; R4-M153), and helpers that read and write
    // statics.
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
        struct W5
            public var n: i32 = 0
            public init() => ()
            drop
                _ = State.y5.n
                _ = State.w5.n
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
            public let y5: Counter = State.makeY5()
            public var w5: W5 = W5.init()
            public var pair: (i32, i32) = (1, 2)
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
            public func makeY5() -> Counter
                let f: () -> () = func [] () => ()
                return Counter.init()
            public func both3(g: () -> ())
                _ = useLim()
                g()
            public func bumpPair() => State.pair = (3, 4)
            public func readPair() -> i32 => State.pair.1
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

    // SPEC 15.6.4, 15.6.7 (R1, W6; ACallsEffectsMeetTheLiveStaticLoansRooted): a Call's effects meet the live Loans on mutable static
    // roots. R4-M90 PQ-1: every receiver here (reserved, or shared with a retained result), the argument's adaptation and the `for` Subject's
    // receiver is Binding's Unsupported_Kd, so A5 meets nothing; a compound assignment reserves its target with no Borrow (e1's writeback).
    [Fact]
    public void ACallsEffectsMeetTheLiveStaticLoans()
        => Assert.Empty(
            Effects(
                """
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

                """,
                ["count", .. Enumerable.Repeat("counter", 13), "limits", "limits", "list", "list"]));

    // SPEC 16.4 with 16.3.1, 16.3.3 (R3) and 15.6.4: destroying a mutable static's old value holds an exclusive Loan on it until it
    // completes, so its own effects on that static conflict, a read (e8) or an erased destruction's unknown effects (e9), at the
    // assignment's Drop, with no Loan issued; a quiet destructor does not (e8b), and one past A4's Deep is the depth limit (e8c; the owner's
    // 「G272: (1)」, R4-M153).
    // R4-M90 PQ-1: exchange's way out (e9b) borrows the static and e9s's Loan is a receiver's, each Binding's Unsupported_Kd until P42, so
    // A5 checks neither (AnErasedDestructionMeetsTheLiveStaticLoansRooted). Fail-before: e8, e8c and e9.
    [Fact]
    public void ADestructionMeetsItsOwnStaticAndTheLiveStaticLoans()
        => Assert.Equal(
            ["e8: watch = Watch.init()", "e8c: limited = Limited.init() OwnershipDepthLimit_Kd", "e9: action = func []() => ()"],
            Effects(
                """
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

                """,
                ["action@uniq", "counter"]));

    // SPEC 15.6.4, 22.4.3 (R4, PD12 (ii) as written, OQ-F5-6; AnEnsureWhoseProofWouldDecideIsUnsupportedRooted): a staticEnsure runs its
    // static's initializer, whose effects meet the live Loans unless a live Loan on the ensured static proves it initialized. R4-M90 PQ-1:
    // every Loan here is a receiver's on a static, Binding's Unsupported_Kd until P42, so no ensure meets one.
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
        Assert.Empty(
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
                [.. Enumerable.Repeat("log", 5), .. Enumerable.Repeat("x", 5)],
                Cache));
    }

    // SPEC 15.6.4, 22.4.3 (DECISIONS R4-M92, R4-M89's PD12 (ii); AnInitializationProofDecidesWhereverAnInitializerComposesRooted): every
    // first access of a static is an ensure, which A4 composes into every site that may run it, so the proof applies wherever a conflict's
    // deciding effect lies in the initializer of a static a live Loan is on. R4-M90 PQ-1: every Loan here is a receiver's on a static,
    // Binding's Unsupported_Kd until P42, so no site meets one.
    [Fact]
    public void AnInitializationProofDecidesWhereverAnInitializerComposes()
        => Assert.Empty(
            Effects(
                """
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

                """,
                ["d", "d", "lim2", .. Enumerable.Repeat("log", 7), "x", "x"]));

    // SPEC 15.6.4, PD12 (ii) (R4-M92, the group review's R2-B1; AnUnprovedEffectStillDecidesRooted): the proof makes a conflict U only where
    // every effect that would decide it lies in a proved initializer; the first effect outside it decides the Language problem. R4-M90
    // PQ-1: the Loans here are receivers' on statics, Binding's Unsupported_Kd until P42, so multiImm meets none and r3Unproved's destructor,
    // which reads w5 while w5 is destroyed (R3, no Loan), has no proof to consult: the first effect, the unproved y5 initializer's.
    [Fact]
    public void AnUnprovedEffectStillDecides()
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + """
                public func multiImm(g: () -> ())
                    let lm = lim2.view()
                    let ll = log.view()
                    both3(g@move)
                    inspect(lm)
                    inspect(ll)
                public func r3Unproved()
                    let ly = y5.view()
                    w5 = W5.init()
                    inspect(ly)

            """);
        var program = AnalyzeUnsupported(c, ["lim2", "log", "y5"]);
        Assert.Equal(
            ["w5 = W5.init() ComparisonLoanConflict_Kd EffectUnknown"],
            program.Problems.All.ToArray().Select((x, i) => (x, i))
                .Where(static x => x.x.Subject.Module == 0 && (LoanCodes.Contains(x.x.Code) || x.x.Code == DiagnosticCode.Unsupported_Kd))
                .Select(x => $"{Line(c, x.x.Subject)} {x.x.Code} {string.Join(",", program.Problems.Facts(x.i).ToArray().Select(static f => f.Kind)
                    .Where(static k => k is MirFactKind.EffectRead or MirFactKind.EffectWrite or MirFactKind.EffectUnknown))}"));
    }

    // SPEC 15.6.2, 15.6.4 (R4-M92; DECISIONS BU-18, BU-21): an explicit Borrow of a qualified static's part is a Place's Borrow, never a
    // Temporary Value's, so Binding reports it as the located Unsupported_Kd (P42) in a writer's function (qp1) and a reader's (qp1r)
    // alike, and no effect rule meets it (2b-D §1.5's premise). Fail-before bu15: both (they bound, and 2b-3's effect rule judged qp1).
    [Fact]
    public void AQualifiedPartBorrowIsBindingsUnsupported()
    {
        const string Qualified = """
            func qp1()
                let r = State.pair.0@ref
                State.bumpPair()
                inspect(r)
            func qp1r()
                let r = State.pair.0@ref
                _ = State.readPair()
                inspect(r)

            """;
        var c = MirBuildTest.Compile(Prelude + EffectTypes + Qualified);
        Assert.Equal(["State.pair.0@ref Unsupported_Kd", "State.pair.0@ref Unsupported_Kd"], c.Binding.Issues.Select(static x => $"{x.Node} {x.Code}"));
    }

    // AGENTS Diagnostics, R4-M10 (A4's classes, R4-M91; ALimitIsUnsupportedAndADerivedEffectReportsNothingRooted): a call A4 does not
    // classify (Limit) is the located Unsupported_Kd where a live mutable-static Loan would be met, and a callee's dependent of an error
    // (Derived) reports nothing by itself. R4-M90 PQ-1: the Loans here are receivers' on a static, Binding's Unsupported_Kd until P42, so
    // neither a Limit nor a key beside a Derived effect meets one.
    [Fact]
    public void ALimitIsUnsupportedAndADerivedEffectReportsNothing()
    {
        Assert.Empty(
            Effects(
                """
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

                """,
                ["counter", "counter", "counter"]));
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
        Assert.Empty(RejectedProblems(EffectTypes + Derived));
    }

    // SPEC 15.6, 15.1.3, 15.6.3 (G136, DECISIONS R4-M92, OQ-F5-1 (a); AStaticsLoanNamesEAndAWholeWriteEndsItRooted): a whole write of a
    // static ends its Field's Loans, and a direct Loan on a static names E. R4-M90 PQ-1: the Loans on counter here are receivers', Binding's
    // Unsupported_Kd until P42, so no write meets one; refs' reserved receiver is one too, but append's call still receives a value derived
    // from refs, storage the body does not own (R4-M109), so y's Loan stored through it is met by y's write (stored).
    [Fact]
    public void AStaticsLoanNamesEAndAWholeWriteEndsIt()
        => Assert.Equal(
            ["stored: y = 2"],
            Effects(
                """
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

                """,
                ["counter", "counter", "counter", "refs"]));

    // SPEC 15.6.4's widened effects (tests/diagnostics/spec-cases.json 15.6.4-widened-effects, its Borrow implicit; R4-M87;
    // AWidenedEffectMeetsTheLiveStaticLoansRooted): a summary that would keep an effect on a projection with a recursive base has unknown
    // environment effects, which conflict with every live mutable-static Loan. R4-M90 PQ-1: count's receiver Borrow is Binding's
    // Unsupported_Kd until P42, so the walk meets no Loan.
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
        Assert.Empty(Problems(c, AnalyzeUnsupported(c, ["count"]), LoanCodes));
    }

    // PD31, AGENTS Diagnostics (R4-M92; AnEffectProblemNamesTheFieldItMetRooted): an effect problem records typed facts where it fails: its
    // Loan's anchor, its witness, and the static Field it met, by what decided it; R3's is anchored at its Drop and has no witness, since no
    // value carries the destruction's hold. R4-M90 PQ-1: the other Loans here are receivers' on a static, Binding's Unsupported_Kd until P42,
    // so only R3's problem remains.
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
        var program = AnalyzeUnsupported(c, ["counter", "counter", "counter"]);
        Assert.Equal(
            ["watch = Watch.init(): Anchor watch = Watch.init(), EffectRead public var watch: Watch = Watch.init()"],
            program.Problems.All.ToArray().Select((x, i) => (x, i)).Where(static x => LoanCodes.Contains(x.x.Code) && x.x.Subject.Module == 0).Select(x =>
                $"{Line(c, x.x.Subject)}: {string.Join(", ", program.Problems.Facts(x.i).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)}"))}"));
    }

    // R4-M11 (R4-M91, R4-M92; AnEffectConflictHasTheViewsWhoseKeysMeetItRooted): a body no view decides is checked once for its views, but
    // its calls' keys are each view's. R4-M90 PQ-1: c's receiver Borrow of x is Binding's Unsupported_Kd until P42, so forward's two views
    // meet no Loan.
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
        var program = AnalyzeUnsupported(c, ["x"]);
        var forward = Body(program, "forward");
        Assert.Equal(2, MirCases.Count(forward));
        Assert.DoesNotContain(program.Problems.All.ToArray(), x => x.Body == forward.Ordinal && LoanCodes.Contains(x.Code));
    }

    // SPEC 23.1.2 (R4-M90 PQ-1, DECISIONS BU-25, R4-M109): an implicit Borrow of a static is Binding's located Unsupported_Kd at its operand,
    // which its consumer receives as a derived value with no Loan, so a result kept past the statement meets nothing: a receiver's with a
    // retained result (retained, beside an unknown call in effect, parenthesized, a base method's BaseBorrow, a handle's PayloadProjection),
    // an exclusive one's retained (grabbed), a reserved receiver's, a value call's callee (called; calls, handles, qualified and handled:
    // parenthesized, a let, a struct Field, a handle's payload Field, an element, with an input, obj and rc handles, a followed one, an
    // Exclusive Closure's element, each call bound on), an argument's adaptation, a kept
    // argument's and an expected Type's, as a written Borrow's does not (fail-before 2x-E8's merge: a conflict at each retained form's
    // operand; called's before the callee's U: a conflict at fn).
    [Fact]
    public void AnUnsupportedStaticBorrowFormsNoLoan()
    {
        var c = MirBuildTest.Compile("""
            struct Counter
                public var n: i32 = 0
                public init() => ()
                public func add(self: uniq/Self, k: i32) => self.n = self.n + k
                public func view(self: ref/Self) -> ref/i32 during self => self.n@ref
                public func grab(self: uniq/Self) -> uniq/i32 during self => self.n@uniq
            open struct Base
                public var m: i32 = 0
                protected init() => ()
                public func look(self: ref/Self) -> ref/i32 during self => self.m@ref
            struct Leaf : Base
                public init() : base()
                    ()
            struct Holder
                public var fn: () -> i32 = func [] () => 1
                public init() => ()
            group Held
                public var counter: Counter = Counter.init()
                public var count: i32 = 0
                public var leaf: Leaf = Leaf.init()
                public var handle: obj/Counter = Kimi.Intrinsics.makeObj<Counter>(Counter.init())
                public var fn: () -> i32 = func [] () => 1
                public let fixedFn: () -> i32 = func [] () => 2
                public var f1: (i32) -> i32 = func [] (k: i32) => k
                public var rec: Holder = Holder.init()
                public var h: obj/Holder = Kimi.Intrinsics.makeObj<Holder>(Holder.init())
                public var fns: Array<() -> i32> = [func [] () => 3]
                public var oc: obj/((i32) -> i32) = Kimi.Intrinsics.makeObj<(i32) -> i32>(func [] (k: i32) => k)
                public var rcf: rc/((i32) -> i32) = Kimi.Intrinsics.makeRc<(i32) -> i32>(func [] (k: i32) => k)
                public var exs = [(func [] (k: i32) => func [var k] () -> i32 => ++k)(0)]
                public func inspect<T>(value: ref/T) => ()
                public func touch(value: uniq/i32) => ()
                public func takes(value: ref/i32) => ()
                public func keep(value: ref/i32 during a) -> ref/i32 during a => value
                public func parenthesized()
                    let c = (counter).view()
                    inspect(c)
                public func grabbed()
                    let g = counter.grab()
                    touch(g)
                public func based()
                    let b = leaf.look()
                    inspect(b)
                public func payload()
                    let p = handle.view()
                    inspect(p)
                public func called() -> i32 => fn()
                public func calls() -> i32 => (fn)() + fixedFn() + rec.fn() + h.fn() + fns[0]() + f1(count)
                public func handles() -> i32 => oc(1) + oc@follow(2) + rcf(3) + exs[0]()
                public func retained()
                    let c = counter.view()
                    inspect(c)
                public func effect(g: () -> ())
                    let c = counter.view()
                    g()
                    inspect(c)
                public func reserved() => counter.add(1)
                public func adapted() => takes(count)
                public func kept()
                    let r = keep(count)
                    inspect(r)
                public func expected()
                    let r: ref/Counter = counter
                    inspect(r)
                public func written()
                    let r = counter@ref
                    inspect(r)
            func qualified() -> i32 => Held.fn() + (Held.fn)() + Held.fixedFn() + Held.rec.fn() + Held.h.fn() + Held.fns[0]() + Held.f1(1)
            func handled() -> i32 => Held.oc(1) + Held.rcf(2) + Held.exs[0]()
            public func main() => ()

            """);
        string[] borrows = ["(Held.fn)", "(counter)", "(fn)", "Held.exs[0]", "Held.f1", "Held.fixedFn", "Held.fn", "Held.fns[0]", "Held.h.fn", "Held.oc",
            "Held.rcf", "Held.rec.fn", "count", "count", .. Enumerable.Repeat("counter", 5), "counter@ref", "exs[0]", "f1", "fixedFn", "fn", "fns[0]",
            "h.fn", "handle", "leaf", "oc", "oc", "rcf", "rec.fn"];
        Assert.Empty(Problems(c, AnalyzeUnsupported(c, borrows), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // The Loan problems and limit records (a located Unsupported_Kd, the depth limit) of a program: the prelude, the effect Types and group
    // State with the given members, then the given declarations, whose static Borrows are Binding's Unsupported_Kd (AnalyzeUnsupported).
    private static string[] Effects(string members, string[] borrows, string declarations = "")
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + members + declarations);
        return Problems(c, AnalyzeUnsupported(c, borrows), [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnershipDepthLimit_Kd]);
    }
}
